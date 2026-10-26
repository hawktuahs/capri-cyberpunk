param([switch]$SkipVoiceBuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$config = Get-Content -LiteralPath release.json -Raw | ConvertFrom-Json
if (-not $SkipVoiceBuild) {
    & .\build-voice.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Voice build failed.' }
}
& .\build.ps1
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'Capri.exe')).FileVersion
if ($actualVersion -ne "$($config.version).0") { throw 'AssemblyInfo.cs and release.json versions differ.' }
$requiredFiles = @('voice\CapriHelper.exe', 'voice\models\kokoro-v1.0.int8.onnx', 'voice\models\voices-v1.0.bin', 'voice\models\vosk-model-small-en-us-0.15\am\final.mdl', 'voice\licenses\packages.json')
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing bundled component: $file" }
}
$outputRoot = Join-Path $PSScriptRoot 'release-output'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$staging = Join-Path $outputRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
foreach ($file in @('Capri.exe','run.bat','launch_capri.ps1','README.md','RELEASING.md','THIRD_PARTY.md','release.json')) {
    Copy-Item -LiteralPath $file -Destination $staging
}
Copy-Item -LiteralPath voice -Destination $staging -Recurse
# Keep the complete helper source with its optional third-party engines and notices.
$sourceFolder = Join-Path $staging 'source'
New-Item -ItemType Directory -Path $sourceFolder | Out-Null
Copy-Item -LiteralPath capri_helper.py,bundled_audio.py,voice-requirements.txt,voice-requirements.lock.txt,voice-models.lock.json -Destination $sourceFolder
$zipPath = Join-Path $outputRoot $config.asset
if (Test-Path -LiteralPath $zipPath) { throw 'Output already exists. Move it aside before rebuilding this version.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($config.asset)" | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Ready: $zipPath" -ForegroundColor Green
