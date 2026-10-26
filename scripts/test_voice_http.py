"""Integration test the distributed helper, not the build machine's Python engines."""
import io
import json
import socket
import subprocess
import tempfile
import time
import urllib.request
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
base = f"http://127.0.0.1:{port}"
with tempfile.TemporaryFile() as log:
    process = subprocess.Popen([str(ROOT / "voice" / "CapriHelper.exe"), str(port)], cwd=ROOT / "voice", stdout=log, stderr=log, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    try:
        for attempt in range(120):
            if process.poll() is not None:
                log.seek(0)
                raise RuntimeError(log.read().decode(errors="replace"))
            try:
                with urllib.request.urlopen(base + "/health", timeout=1) as response:
                    assert json.load(response)["bundled_speech"]
                break
            except OSError:
                time.sleep(0.5)
        else:
            raise RuntimeError("Frozen helper failed to become ready")
        for profile in ("construct_echo", "mommy_asmr"):
            request = urllib.request.Request(base + "/tts", json.dumps({"text": "Hello Capri. Open the calculator.", "voice": profile}).encode(), {"Content-Type": "application/json"})
            with urllib.request.urlopen(request, timeout=90) as response:
                audio = response.read()
            with wave.open(io.BytesIO(audio), "rb") as wav:
                assert wav.getnchannels() == 1 and wav.getnframes() > wav.getframerate()
            request = urllib.request.Request(base + "/stt?lang=en", audio, {"Content-Type": "audio/wav"})
            with urllib.request.urlopen(request, timeout=30) as response:
                result = json.load(response)
            assert "calculator" in result["text"], result
            print(f"PASS frozen helper HTTP TTS/STT ({profile}): {result['text']}")
        # This only inspects the dispatch boundary; it must not launch anything.
        request = urllib.request.Request(base + "/action", b'{"action":"open_app","arg":"not-a-supported-app"}', {"Content-Type":"application/json"})
        with urllib.request.urlopen(request, timeout=5) as response:
            assert json.load(response)["ok"] is False
        print("PASS: unknown application names are rejected")
    finally:
        process.terminate()
        process.wait(timeout=10)
