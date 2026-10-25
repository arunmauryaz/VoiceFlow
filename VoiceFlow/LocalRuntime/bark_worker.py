"""VoiceFlow's offline Bark worker. JSON lines over stdin/stdout; no network."""
import contextlib
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"


def main():
    with contextlib.redirect_stdout(sys.stderr):
        import torch
        import soundfile as sf
        from bark_loader import load_model, transcribe
        torch.set_num_threads(max(1, min(8, (os.cpu_count() or 2) // 2)))
        device = "cuda" if len(sys.argv) > 2 and sys.argv[2] == "GPU" else "cpu"
        if device == "cuda" and not torch.cuda.is_available():
            raise RuntimeError("GPU unavailable. Select Default or CPU.")
        model = load_model(sys.argv[1], device=device)
        # Warm the audio processor before reporting Ready.
        import numpy as np
        from bark_loader import _processor
        _processor()([np.zeros(16000, dtype=np.float32)], sampling_rate=16000,
                     return_tensors="pt", padding=True)
    print(json.dumps({"ready": True}), flush=True)
    for line in sys.stdin:
        try:
            request = json.loads(line)
            with contextlib.redirect_stdout(sys.stderr):
                wave, sr = sf.read(request["audio"], dtype="float32", always_2d=True)
                text = transcribe(model, wave.mean(1), sr)
            print(json.dumps({"text": text}), flush=True)
        except Exception as exc:
            print(json.dumps({"error": str(exc)}), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(json.dumps({"error": str(exc)}), flush=True)
        sys.exit(1)
