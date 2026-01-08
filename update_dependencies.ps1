# Update Dependencies Script
# Downloads the latest versions of yt-dlp and ffmpeg to the Dependencies folder.

$ErrorActionPreference = "Stop"

$depsDir = Join-Path $PSScriptRoot "Dependencies"
if (-not (Test-Path $depsDir)) {
    New-Item -ItemType Directory -Path $depsDir | Out-Null
}

Write-Host "Updating External Dependencies..." -ForegroundColor Cyan

# 1. Download yt-dlp
Write-Host "Fetching latest yt-dlp.exe..." -ForegroundColor Yellow
$ytDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
Invoke-WebRequest -Uri $ytDlpUrl -OutFile (Join-Path $depsDir "yt-dlp.exe") -UserAgent "Mozilla/5.0"
Write-Host "yt-dlp updated." -ForegroundColor Green

# 2. Download ffmpeg
Write-Host "Fetching latest ffmpeg..." -ForegroundColor Yellow
$ffmpegZipUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
$tempZip = Join-Path $depsDir "ffmpeg.zip"
$tempExtract = Join-Path $depsDir "ffmpeg_temp"

Invoke-WebRequest -Uri $ffmpegZipUrl -OutFile $tempZip -UserAgent "Mozilla/5.0"

if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
Expand-Archive -Path $tempZip -DestinationPath $tempExtract

$ffmpegExe = Get-ChildItem -Path $tempExtract -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
if ($ffmpegExe) {
    Copy-Item -Path $ffmpegExe.FullName -Destination (Join-Path $depsDir "ffmpeg.exe") -Force
    Write-Host "ffmpeg updated." -ForegroundColor Green
}
else {
    Write-Error "Could not find ffmpeg.exe in the downloaded archive."
}

# Cleanup
Remove-Item $tempZip -Force
Remove-Item $tempExtract -Recurse -Force

Write-Host "`n✅ Dependencies updated in folder: $depsDir" -ForegroundColor Green
