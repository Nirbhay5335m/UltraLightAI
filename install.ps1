
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$downloadUrl = "https://github.com/Nirbhay5335m/UltraLightAI/releases/download/v1.0.0/UltraLightAI.exe"
$expectedHash = "616643CAB1FCDF5B5FA6BF517EDC14B1D4CAFB7D00846E33DEEF76EE3B11EE73"

$installDir = Join-Path $env:LOCALAPPDATA "UltraLightAI"
$exePath = Join-Path $installDir "UltraLightAI.exe"

New-Item -ItemType Directory -Path $installDir -Force | Out-Null

Write-Host "Downloading UltraLightAI..."

Invoke-WebRequest -Uri $downloadUrl -OutFile $exePath

Write-Host "Verifying downloaded file..."

$actualHash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash

if ($actualHash -ne $expectedHash) {
    Remove-Item $exePath -Force
    throw "SHA256 verification failed. Application was not launched."
}

Write-Host "Verification successful."
Write-Host "Launching UltraLightAI..."

Start-Process -FilePath $exePath
