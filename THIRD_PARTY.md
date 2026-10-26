# Third-party components

Capri is an unofficial fan project based on lachydotmcg/capri-desktop. Character artwork belongs to its respective owners. The full package uses general Kokoro voices, not the optional character voice-cloning sample.

## Bundled speech models

| Component | Source | License |
| --- | --- | --- |
| Kokoro 82M v1.0, quantized ONNX | https://github.com/thewh1teagle/kokoro-onnx/releases/tag/model-files-v1.0 and https://huggingface.co/hexgrad/Kokoro-82M | Apache 2.0 |
| Kokoro ONNX wrapper 0.6.1 | https://github.com/thewh1teagle/kokoro-onnx | MIT |
| Vosk small English US 0.15 | https://alphacephei.com/vosk/models | Apache 2.0 |
| Vosk API 0.3.45 | https://github.com/alphacep/vosk-api | Apache 2.0 |
| eSpeak NG 1.52.0 | https://github.com/espeak-ng/espeak-ng/tree/4870adfa25b1a32b4361592f1be8a40337c58d6c | GPL 3.0 or later |
| phonemizer 3.4.0 | https://github.com/bootphon/phonemizer | GPL 3.0 or later |

The distribution includes full license files and corresponding eSpeak NG and phonemizer source archives under `voice/licenses/`. The loader's source archive includes its build recipe; eSpeak source is included separately because upstream uses a submodule. These components run in the separate CapriHelper process. Dependency changes require updating their notices and source archives before release.

`voice/models/provenance.json` records all downloaded model and source URLs, byte counts and SHA-256 hashes. `voice/licenses/packages.json` records Python package versions and project URLs, and package-provided license/notice files are retained under `voice/licenses/<package>/`. The embedded Python license is included separately. Dependencies include ONNX Runtime, NumPy, Vosk, PyAutoGUI, Pillow, RapidOCR, eSpeak NG, phonemizer and PyInstaller (with its bootloader exception).

The Share Tech Mono font includes its SIL Open Font License in the source assets. Large developer reference audio and optional Chatterbox conditionals are not included in the full voice package.
