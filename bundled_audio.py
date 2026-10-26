"""CPU speech engines for the redistributable Capri package. No model downloads."""
import io
import json
import sys
import threading
import wave
from pathlib import Path

import numpy as np

ROOT = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).parent / "voice"
MODELS = ROOT / "models"
AVAILABLE = (MODELS / "kokoro-v1.0.int8.onnx").is_file()
_tts = None
_stt = None
_tts_lock = threading.Lock()
_stt_lock = threading.Lock()


def synthesize(text, voice=None):
    global _tts
    from kokoro_onnx import Kokoro
    with _tts_lock:
        if _tts is None:
            _tts = Kokoro(str(MODELS / "kokoro-v1.0.int8.onnx"), str(MODELS / "voices-v1.0.bin"))
        warm = any(x in (voice or "").lower() for x in ("asmr", "heart", "mommy"))
        audio, rate = _tts.create(text, voice="af_heart" if warm else "af_sarah", speed=0.93 if warm else 1.0, lang="en-us")
        if not warm:
            delay = int(rate * 0.11)
            echoed = np.zeros(len(audio) + delay, dtype=np.float32)
            echoed[:len(audio)] += audio
            echoed[delay:] += audio * 0.25
            audio = echoed
        pcm = (np.clip(audio, -1, 1) * 32767).astype("<i2").tobytes()
        output = io.BytesIO()
        with wave.open(output, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(rate)
            wav.writeframes(pcm)
        return output.getvalue()


def transcribe(raw, lang="en"):
    global _stt
    if lang not in ("en", "auto", "", None):
        raise ValueError("The bundled speech model supports English. Select STT: EN in Mainframe.")
    from vosk import KaldiRecognizer, Model, SetLogLevel
    with _stt_lock:
        if _stt is None:
            SetLogLevel(-1)
            _stt = Model(str(MODELS / "vosk-model-small-en-us-0.15"))
        with wave.open(io.BytesIO(raw), "rb") as wav:
            if wav.getnchannels() != 1 or wav.getsampwidth() != 2:
                raise ValueError("Expected mono 16-bit PCM WAV.")
            recognizer = KaldiRecognizer(_stt, wav.getframerate())
            recognizer.AcceptWaveform(wav.readframes(wav.getnframes()))
            return json.loads(recognizer.FinalResult()).get("text", "").strip()
