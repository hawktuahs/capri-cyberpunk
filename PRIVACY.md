# Project Capri — privacy and data

Effective 28 October 2026. Publisher: regressorslove. This policy describes the Project Capri 1.4 Store review build, which is not yet publicly released. Earlier downloadable versions use different speech components and may have different controls.

## Use and risk

Project Capri is experimental AI software. Speech recognition, generated replies and automated actions can be wrong or unexpected. The 1.4 agent tools block arbitrary terminal commands and executable downloads, and supported browser writes require review. These application-level guardrails are not a security sandbox or a guarantee of safety. The application runs with your Windows user permissions. Review actions, keep backups and enable only the capabilities you need.

To the maximum extent permitted by applicable law, the software is provided as is, without warranties, and the publisher and contributors disclaim liability for losses or damage arising from its use. Non-waivable rights and liabilities remain unaffected. See [TERMS.md](TERMS.md) for the full use and liability notice. This notice does not change the data practices described below.

## Local processing

The microphone starts only when you enable Mic Ears or click Mic Test. Cactus Whistle recognizes speech locally. Wake mode checks recognized utterances for “Capri”; background utterances rejected by that gate are not sent to the conversation provider. Wake mode still needs microphone access while listening. Audio is processed in memory and short-lived local temporary files used by the recognizer, normally removed after recognition. Capri does not keep a library of microphone recordings.

Chatterbox Nano generates the Capri and ASMR voices locally from the prepared reference profiles included with the app. The complete source recordings are not uploaded by the app.

## Optional providers

Selecting Groq or NVIDIA sends conversation text, recognized speech requests and recent conversation context to that provider. Connection setup identifies the provider. Their own privacy, retention and billing terms apply. Without a cloud brain, an installed local model can be used instead.

Selecting ElevenLabs sends spoken reply text to api.elevenlabs.io for speech synthesis using your own key and voice ID. It does not upload the bundled reference recordings. Chrome page-analysis replies use local speech even when ElevenLabs is selected, avoiding an additional disclosure of those replies to the speech provider.

## Screen and Chrome

Screen access is off by default. Enabling it allows desktop window titles and OCR text to be included with conversation requests, including requests to a selected cloud brain. Turn it off to stop that collection.

Chrome access is separately off by default. Local page preview, text search, highlighting and accessible-element perching run on your computer. To analyze a page through Groq or NVIDIA, Capri displays the selected destination and a bounded snapshot of accessible page text. You must choose “Send this page once” for each snapshot. URL query strings and fragments are omitted. Password controls are excluded, but ordinary page text can still contain personal information; inspect the preview. Page snapshots and their analysis replies are not added to Capri's saved chat history. Page-analysis replies cannot execute model action tags.

Typing or pressing named Chrome elements requires action review. Sensitive and destructive targets are blocked. Capri does not provide a general shell, installer or arbitrary file-control tool to the agent.

## Storage and controls

Avatar settings, provider selections, recent chat memory and diagnostic logs are kept in Windows local application data. Packaged installations may use Windows' package-specific data location. Provider keys are protected using Windows user-level data protection. Chat memory is not encrypted by Capri. Logs can include startup, error and local path information. No developer-operated conversation server or analytics collector is configured in this build.

Use Reset memory to clear the app's remembered conversation. Disconnect cloud keys to stop using those providers. Turn off Mic Ears, Screen and Chrome independently. The portable build checks GitHub for releases; Store installations use Microsoft Store for application updates. Explicit model setup may contact the model/runtime hosts listed in the bundled notices; Store builds do not bootstrap a downloaded executable engine. Search commands use the relevant search website and its terms.

## Contact and content reports

For private support, privacy questions or AI-content reports, email regressorslove@outlook.com. Do not include API keys or passwords.

Use “Report an AI reply” in the tray menu or https://github.com/hawktuahs/capri-cyberpunk/issues/new?template=ai-content.yml. This opens a reporting form; it does not send your conversation automatically. GitHub reports are public by default, so remove private information. Privacy questions may also be raised at https://github.com/hawktuahs/capri-cyberpunk/issues; do not post credentials or sensitive page contents.
