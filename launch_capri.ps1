# The application owns helper startup, single-instance restore and local brain discovery.
$ErrorActionPreference = 'Stop'
$capriExecutable = Join-Path $PSScriptRoot 'Capri.exe'
if (-not (Test-Path -LiteralPath $capriExecutable)) {
    throw 'Capri.exe is missing. Extract the entire release ZIP before launching.'
}
Start-Process -FilePath $capriExecutable -WorkingDirectory $PSScriptRoot
