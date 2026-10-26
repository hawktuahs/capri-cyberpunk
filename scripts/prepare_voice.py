"""Fetch redistributable models for build-voice.ps1; record their provenance."""
import concurrent.futures
import hashlib
import json
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / ".build-voice" / "downloads"
MODELS = ROOT / "voice" / "models"
LICENSES = ROOT / "voice" / "licenses"
EXPECTED = json.loads((ROOT / "voice-models.lock.json").read_text(encoding="utf-8"))
FILES = {
    "kokoro-v1.0.int8.onnx": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.int8.onnx",
    "voices-v1.0.bin": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin",
    "vosk-model-small-en-us-0.15.zip": "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
    "Kokoro-APACHE-2.0.txt": "https://raw.githubusercontent.com/hexgrad/kokoro/main/LICENSE",
    "Vosk-APACHE-2.0.txt": "https://raw.githubusercontent.com/alphacep/vosk-api/v0.3.45/COPYING",
    "kokoro-onnx-MIT.txt": "https://raw.githubusercontent.com/thewh1teagle/kokoro-onnx/main/LICENSE",
    "phonemizer-3.4.0-source.tar.gz": "https://files.pythonhosted.org/packages/bc/7d/5a96ddb130552f6365a090b5fd12ace803a95a858e3f67258f2ad13dc51f/phonemizer-3.4.0.tar.gz",
    "espeak-ng-source.tar.gz": "https://github.com/espeak-ng/espeak-ng/archive/4870adfa25b1a32b4361592f1be8a40337c58d6c.tar.gz",
    "espeak-ng-GPL-3.0.txt": "https://raw.githubusercontent.com/espeak-ng/espeak-ng/4870adfa25b1a32b4361592f1be8a40337c58d6c/COPYING",
    "espeakng-loader-source.tar.gz": "https://github.com/thewh1teagle/espeakng-loader/archive/0ddc87adf77e5850d7eeb542ac8a87d421b64daa.tar.gz",
}


def fetch(item):
    name, url = item
    target = CACHE / name
    if not target.exists():
        temporary = target.with_suffix(target.suffix + ".partial")
        with urllib.request.urlopen(url, timeout=120) as response, temporary.open("wb") as out:
            while chunk := response.read(1024 * 1024):
                out.write(chunk)
        temporary.replace(target)
    digest = hashlib.sha256(target.read_bytes()).hexdigest()
    if name in EXPECTED and digest != EXPECTED[name]:
        raise ValueError(f"Checksum mismatch for {name}. Remove the cached file and retry.")
    print(f"Ready: {name} ({target.stat().st_size:,} bytes)", flush=True)
    return {"file": name, "source": url, "sha256": digest, "bytes": target.stat().st_size}


def main():
    import shutil
    for directory in (CACHE, MODELS, LICENSES):
        directory.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        manifest = list(pool.map(fetch, FILES.items()))
    for name in FILES:
        if name.endswith(".zip"):
            with zipfile.ZipFile(CACHE / name) as archive:
                for entry in archive.infolist():
                    if not (MODELS / entry.filename).resolve().is_relative_to(MODELS.resolve()):
                        raise ValueError("Unsafe archive entry")
                archive.extractall(MODELS)
        else:
            shutil.copy2(CACHE / name, (LICENSES if name.endswith((".txt", ".tar.gz")) else MODELS) / name)
    (MODELS / "provenance.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
