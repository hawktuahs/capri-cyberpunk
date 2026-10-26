# Build on Windows x64 with Python 3.10.2+. End users do not need Python.
param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$buildRoot = Join-Path $PSScriptRoot '.build-voice'
$pythonVersion = & $Python -c 'import sys; print(".".join(map(str, sys.version_info[:2])))'
if ($LASTEXITCODE -ne 0) { throw 'Python is unavailable.' }
$venvPath = Join-Path $buildRoot "venv-$pythonVersion"
$venvPython = Join-Path $venvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    & $Python -m venv $venvPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated build environment.' }
}
& $venvPython -m pip install -r voice-requirements.lock.txt
if ($LASTEXITCODE -ne 0) { throw 'Voice dependencies failed to install.' }
& $venvPython scripts/prepare_voice.py
if ($LASTEXITCODE -ne 0) { throw 'Model download failed.' }
& $venvPython -m PyInstaller --noconfirm --onedir --console --name CapriHelper `
    --distpath "$buildRoot\dist" --workpath "$buildRoot\work" --specpath $buildRoot `
    --collect-all kokoro_onnx --collect-all espeakng_loader --collect-all vosk `
    --collect-all rapidocr_onnxruntime --collect-all onnxruntime `
    --copy-metadata kokoro-onnx --exclude-module torch --exclude-module torchaudio `
    --exclude-module transformers --exclude-module kokoro --exclude-module chatterbox `
    --exclude-module needle --exclude-module speech_recognition --exclude-module duckduckgo_search capri_helper.py
if ($LASTEXITCODE -ne 0) { throw 'Voice runtime bundling failed.' }
Copy-Item -Path "$buildRoot\dist\CapriHelper\*" -Destination (Join-Path $PSScriptRoot 'voice') -Recurse -Force
# Retain dependency metadata, licenses and GPL source locations with the distribution.
& $venvPython scripts/voice_notices.py
if ($LASTEXITCODE -ne 0) { throw 'Could not collect dependency notices.' }
& '.\voice\CapriHelper.exe' --self-test "$buildRoot\voice-test.json"
if ($LASTEXITCODE -ne 0) { throw 'Bundled speech self-test failed.' }
Write-Host 'Bundled voice runtime and both models passed the offline self-test.' -ForegroundColor Green
