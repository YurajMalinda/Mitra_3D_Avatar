#!/usr/bin/env python3
# mitra_scorer_server.py - MITRA Persistent Scoring Server
# Loads Wav2Vec2 model ONCE at startup, then scores WAV files on demand via TCP.
# Port: 55123 | Protocol: client sends path\n, server replies score\n
import socket, os, sys
import numpy as np, torch, torch.nn as nn, librosa

# Speed optimisation - use all CPU cores for faster model loading and inference
torch.set_num_threads(4)
torch.set_num_interop_threads(2)

WEIGHTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'mitra_scorer_v5_weights.pt')
PORT    = 55123
HOST    = '127.0.0.1'
SR      = 16000
MAX_S   = int(8.0 * 16000)

class _Scorer(nn.Module):
    def __init__(self, w2v):
        super().__init__()
        self.wav2vec2 = w2v
        self.head = nn.Sequential(
            nn.Linear(768, 256), nn.ReLU(), nn.Dropout(0.3),
            nn.Linear(256, 64),  nn.ReLU(), nn.Dropout(0.2),
            nn.Linear(64, 1),    nn.Sigmoid())
    def forward(self, iv, mask=None):
        out = self.wav2vec2(iv, attention_mask=mask)
        return self.head(out.last_hidden_state.mean(dim=1)).squeeze(-1)

import warnings
warnings.filterwarnings('ignore')
from transformers import Wav2Vec2Model

print('MITRA Scorer: Loading model...', flush=True)
w2v   = Wav2Vec2Model.from_pretrained('facebook/wav2vec2-base-960h', local_files_only=True)
model = _Scorer(w2v)
ck    = torch.load(WEIGHTS, map_location='cpu', weights_only=False)
model.load_state_dict(ck['model_state_dict'])
model.eval()
print('MITRA Scorer: Model ready', flush=True)

# Bind socket only after model is fully loaded so Unity's TCP probe is a true readiness signal
server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.bind((HOST, PORT))
server.listen(5)
print(f'MITRA Scorer: Ready on port {PORT}', flush=True)

def score_wav(path):
    try:
        y, _ = librosa.load(path, sr=SR, mono=True)
        if len(y) < MAX_S:
            y = np.pad(y, (0, MAX_S - len(y)))
        else:
            y = y[:MAX_S]
        mv = np.abs(y).max()
        if mv > 0:
            y = y / mv
        y    = np.clip(y, -1.0, 1.0).astype(np.float32)
        iv   = torch.tensor(y).unsqueeze(0)
        mask = torch.ones(1, len(y), dtype=torch.long)
        with torch.no_grad():
            sc = float(model(iv, mask).item())
        return round(max(0.0, min(1.0, sc)), 4)
    except Exception as e:
        print(f'score_wav error: {e}', file=sys.stderr, flush=True)
        return 0.0

while True:
    conn = None
    try:
        conn, _ = server.accept()
        path = conn.recv(4096).decode().strip()
        if path == 'QUIT':
            conn.sendall(b'OK\n')
            conn.close()
            break
        score = score_wav(path)
        conn.sendall(f'{score}\n'.encode())
        conn.close()
    except Exception as e:
        print(f'Server error: {e}', file=sys.stderr, flush=True)
        try:
            if conn: conn.close()
        except: pass