# Release Capri

## Website

`site/` is the canonical GitHub Pages source. Do not edit `docs/` separately; it is a compatibility mirror. `.github/workflows/pages.yml` uploads `site/` on a push to main or a published release. The deployment refreshes its fallback snapshot from the latest stable release. Repository Settings → Pages → Source must be **GitHub Actions**. The site uses relative asset links and works under `/capri-cyberpunk/`.

Download links, version and package size come from this repository's latest non-prerelease GitHub Release. `site/release.json` is an offline/API-rate-limit fallback snapshot; refresh it after publishing a release. The site never points to another fork's package.

## One-click release workflow

1. Update `release.json` (version and asset filename), `app/AssemblyInfo.cs` (assembly/file versions), and `CHANGELOG.md`.
2. Commit and push to main.
3. In GitHub Actions, open **Release app** → **Run workflow**. Use main.
4. The Windows job compiles the app, builds the isolated speech runtime, downloads both speech models, and runs the frozen speech round-trip test. It then packages the full ZIP, records SHA-256 and uploads a draft release.
5. Review the draft and publish it. Existing Capri installs detect its version; the website picks up the new release automatically.

The draft is intentionally left unpublished by the recurring release workflow so a maintainer can check a new version before users receive it.

## Build locally

Use Windows x64, PowerShell, .NET Framework 4.8 compiler, and Python 3.12 (3.10.0 has a known bytecode inspection problem with PyInstaller). Python is needed only on the build machine.

```powershell
.\build-voice.ps1 -Python python
.\package_release.ps1 -SkipVoiceBuild
```

`build-voice.ps1` installs the pinned `voice-requirements.lock.txt` into an isolated venv under `.build-voice/`, freezes the runtime into `voice/`, downloads a quantized Kokoro model, voice styles and Vosk English model, and runs TTS → WAV → STT with the frozen executable. The generated `voice/models/provenance.json` records upstream URLs, byte sizes and content hashes. `voice/licenses/` retains dependency notices. See THIRD_PARTY.md for source provenance.

`package_release.ps1` checks required components and matching versions, compiles Capri, and writes `release-output/<asset>` plus `SHA256SUMS.txt`. It uses a fresh staging directory and refuses to overwrite an existing package of the same name. Never commit model files, virtual environments, API keys or user settings.

For a release already tested locally:

```powershell
gh release create v1.3.1 release-output/Capri-Desktop-v1.3.1-Windows-x64.zip release-output/SHA256SUMS.txt --target main --title 'Capri v1.3.1' --notes-file CHANGELOG.md
```

Use the actual configured version/asset. To refresh the website snapshot:

```powershell
gh release view --repo hawktuahs/capri-cyberpunk --json tagName,name,body,publishedAt,url,assets
```

Convert it to the GitHub API field names (`tag_name`, `html_url`, `published_at`, `assets[].browser_download_url`, `assets[].name`, `assets[].size`) in `site/release.json`, then synchronize the compatibility mirror and push main.

## Validation

- `python scripts/check_site.py` checks relative assets, local anchors and tab targets.
- `node --check site/app.js` checks JavaScript syntax.
- `voice/CapriHelper.exe --self-test .build-voice/voice-test.json` exercises both speech models without network access.
- `python scripts/test_voice_http.py` tests the frozen helper over its real HTTP interface on an isolated test port.
- Open the site at desktop and narrow widths; test provider tabs and the release download.

API keys are not bundled. Live Groq/NVIDIA requests require maintainer-provided test credentials; never put them in workflow logs or release files.
