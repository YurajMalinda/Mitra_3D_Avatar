#!/usr/bin/env python3
"""
vosk_transcribe.py  —  MITRA speech transcription helper
Usage: python3 vosk_transcribe.py <wav_path> [vocab_csv]
       vocab_csv: comma-separated word list, e.g. "apple,ball,cat"
Prints a single line: the recognised word (lower-case), or empty string.
"""
import sys
import wave
import json
from vosk import Model, KaldiRecognizer

MODEL_PATH = "/home/yuraj/mitra-env/models/vosk-model-small-en-us-0.15"

def main():
    if len(sys.argv) < 2:
        print("")
        sys.exit(0)

    wav_path = sys.argv[1]
    vocab_csv = sys.argv[2] if len(sys.argv) > 2 else None

    model = Model(MODEL_PATH)

    with wave.open(wav_path, "rb") as wf:
        sample_rate = wf.getframerate()

        if vocab_csv:
            words = [w.strip() for w in vocab_csv.split(",") if w.strip()]
            vocab_json = json.dumps(words)
            rec = KaldiRecognizer(model, sample_rate, vocab_json)
        else:
            rec = KaldiRecognizer(model, sample_rate)
        rec.SetWords(True)

        while True:
            data = wf.readframes(4000)
            if not data:
                break
            rec.AcceptWaveform(data)

    result = json.loads(rec.FinalResult())
    text  = result.get("text", "").strip().lower()
    words = result.get("result", [])
    conf  = sum(w.get("conf", 0.0) for w in words) / len(words) if words else 0.0
    print(text)
    print(f"{conf:.4f}")

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        import sys
        print("", flush=True)  # C# expects a line; empty = no speech
        print(f"vosk error: {e}", file=sys.stderr, flush=True)
