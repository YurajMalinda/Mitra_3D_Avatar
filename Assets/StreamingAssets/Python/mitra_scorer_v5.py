#!/usr/bin/env python3
# mitra_scorer_v5.py - MITRA Pronunciation Scoring v5.4
# Usage: python3 mitra_scorer_v5.py <wav_path>
# Output: float in [0.0, 1.0]
import sys, os
import numpy as np, torch, torch.nn as nn, librosa

WEIGHTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mitra_scorer_v5_weights.pt")
SR = 16000
MAX_WORD_S = int(4.0 * SR)   # single word never exceeds 4s
MIN_WORD_S = int(0.1 * SR)   # reject clips shorter than 0.1s
_model = None

class _Scorer(nn.Module):
    def __init__(self, w2v):
        super().__init__()
        self.wav2vec2 = w2v
        self.head = nn.Sequential(
            nn.Linear(768,256), nn.ReLU(), nn.Dropout(0.3),
            nn.Linear(256,64),  nn.ReLU(), nn.Dropout(0.2),
            nn.Linear(64,1),    nn.Sigmoid())
    def forward(self, iv, mask=None):
        out = self.wav2vec2(iv, attention_mask=mask)
        return self.head(out.last_hidden_state.mean(dim=1)).squeeze(-1)

def _load():
    global _model
    if _model is not None:
        return
    from transformers import Wav2Vec2Model
    w2v    = Wav2Vec2Model.from_pretrained("facebook/wav2vec2-base-960h", local_files_only=True)
    _model = _Scorer(w2v)
    ck     = torch.load(WEIGHTS, map_location="cpu", weights_only=False)
    _model.load_state_dict(ck["model_state_dict"])
    _model.eval()

def score_wav(path):
    if not os.path.exists(path):
        return 0.0
    try:
        _load()
        y, _ = librosa.load(path, sr=SR, mono=True)

        # Remove leading/trailing silence so the model sees speech, not padding
        y, _ = librosa.effects.trim(y, top_db=25)

        if len(y) < MIN_WORD_S:
            return 0.0  # essentially silent — no speech detected

        if len(y) > MAX_WORD_S:
            y = y[:MAX_WORD_S]

        mv = np.abs(y).max()
        if mv > 0:
            y = y / mv
        y = np.clip(y, -1.0, 1.0).astype(np.float32)

        speech_len = len(y)
        iv   = torch.tensor(y).unsqueeze(0)
        mask = torch.ones(1, speech_len, dtype=torch.long)  # mask = actual speech only
        with torch.no_grad():
            sc = float(_model(iv, mask).item())
        return round(max(0.0, min(1.0, sc)), 4)
    except Exception as e:
        print(f"scorer error: {e}", file=sys.stderr)
        return 0.0

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("0.0")
        sys.exit(1)
    print(score_wav(sys.argv[1]))
