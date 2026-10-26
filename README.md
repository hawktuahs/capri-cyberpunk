# Capri Desktop

An unofficial Windows desktop companion with animated sprites, text and voice conversations, screen OCR, and basic desktop controls.

**[Download Capri](https://hawktuahs.github.io/capri-cyberpunk/#download)** · **[Setup guide](https://hawktuahs.github.io/capri-cyberpunk/guide.html)** · **[Releases](https://github.com/hawktuahs/capri-cyberpunk/releases)**

## Install

1. Download the latest `Capri-Desktop-v…-Windows-x64.zip` from Releases.
2. Extract the whole ZIP into a permanent folder. Keep `voice/` beside `Capri.exe`.
3. Open `Capri.exe`. Use the Cyberdeck window, click the Capri tray icon, or press **Ctrl+Shift+C**.
4. Connect a conversation brain below. Try typed chat, then turn on **Mic Ears**.

Requires Windows 10/11 x64 and .NET Framework 4.8. The full v1.3 package includes **Kokoro ONNX TTS**, **Vosk English STT**, their models, OCR, and a bundled runtime. End users do not need Python, CUDA or `setup_voice.bat`. Speech is English; local LLMs are separate downloads.

## Connect an AI brain

| Provider | Setup | Default model |
| --- | --- | --- |
| Groq | Create a key at [Groq Console](https://console.groq.com/keys), paste `gsk_…` into the Cyberdeck cloud field, click Connect. | `openai/gpt-oss-20b` |
| NVIDIA NIM | Get a hosted API key at [NVIDIA Build](https://build.nvidia.com/), paste `nvapi-…`, click Connect. No local NVIDIA GPU required. | `meta/llama-3.1-8b-instruct` |
| Ollama | Install [Ollama](https://ollama.com), run `ollama pull llama3.2:3b`, disconnect any cloud key, Rescan and choose the model. | Your selected local model |
| Built-in local | Disconnect cloud, then use the Cyberdeck's local model installer. Downloads a runtime and model appropriate to the detected hardware. | Hardware-dependent |

You do not create a separate agent at Groq/NVIDIA: Capri supplies the prompt and conversation. Provider quotas, billing and model availability apply. OpenRouter keys are not supported. Without a brain, scripted replies and animations remain available.

## Voice and controls

Set **Voice Synth: ON**, click **Speak Test**, choose **STT: EN**, allow Windows desktop microphone access, and toggle **Mic Ears**. The first synthesis loads the bundled model. Choose an echo or warm voice profile. ElevenLabs is an optional, separate online conversation path without the local tool/history pipeline.

- **Ctrl+Shift+C:** open Cyberdeck. **Ctrl+Alt+C:** hop off while riding the cursor.
- Drag to move; double-click to ride the cursor; pencil control to type.
- Try “open calculator”, “next song”, or “what is on my screen?”.

Screen awareness uses window titles and OCR, not full image understanding. Desktop actions are basic and should be supervised. Scripted browser searches are independent of the PC Control toggle.

## Updates and data

Capri checks this fork's stable GitHub Releases and announces updates. Quit from the tray, extract the new full package over the app folder and reopen. Updates are not silently installed. Settings, local LLMs and plain-text chat memory are in `%LOCALAPPDATA%\Capri`; keep this directory during upgrades.

Bundled speech and local LLM inference stay local. Cloud conversations send messages, history and collected window/OCR text to the selected provider. Cloud keys are protected using Windows user encryption. Never share keys or the settings directory. The helper binds to `127.0.0.1:17777`; do not expose it to a network.

## Build and release

See [RELEASING.md](RELEASING.md) for reproducible packaging, Pages deployment and the Release app workflow. Developer voice setup scripts are for legacy installations and are not required by the full package. See [THIRD_PARTY.md](THIRD_PARTY.md) for model and dependency notices.

Based on [lachydotmcg/capri-desktop](https://github.com/lachydotmcg/capri-desktop). Unofficial fan project; character artwork belongs to its respective owners.
