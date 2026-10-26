# Capri v1.3.0

- Full Windows x64 package includes the CPU speech runtime, quantized Kokoro TTS, and Vosk English STT. No end-user Python or CUDA setup.
- New website and setup guide covering Groq, NVIDIA NIM, Ollama, voice, controls, data handling and upgrades.
- Groq and NVIDIA keys automatically select provider-specific default models. New cloud keys use Windows user encryption.
- Cloud connection fields remain visible during local-brain setup. Provider failures are surfaced rather than replaced with canned replies.
- GitHub Pages deploys from site/; website downloads resolve the actual latest release asset.
- Repeatable Windows release packaging with speech validation, SHA-256 checksums and model/dependency notices.

Speech recognition in this package supports English. The conversation LLM is configured separately. Updates notify users and are installed by replacing the application files.
