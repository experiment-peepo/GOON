$ErrorActionPreference = "Stop"

# Setup
if (Test-Path "test_deps") { Remove-Item "test_deps" -Recurse -Force }
New-Item -ItemType Directory -Path "test_deps" | Out-Null

Write-Host "Testing yt-dlp download..."
$ytUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
try {
    Invoke-WebRequest -Uri $ytUrl -OutFile "test_deps\yt-dlp.exe" -UserAgent "Mozilla/5.0"
    Write-Host "yt-dlp downloaded." -ForegroundColor Green
}
catch {
    Write-Error "yt-dlp download failed: $_"
}

Write-Host "Testing ffmpeg download..."
$ffmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
try {
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile "test_deps\ffmpeg.zip" -UserAgent "Mozilla/5.0"
    
    Expand-Archive -Path "test_deps\ffmpeg.zip" -DestinationPath "test_deps\ffmpeg_temp"
    
    $ffmpegExe = Get-ChildItem -Path "test_deps\ffmpeg_temp" -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    
    if ($ffmpegExe) {
        Copy-Item -Path $ffmpegExe.FullName -Destination "test_deps\ffmpeg.exe"
        Write-Host "ffmpeg extracted and copied." -ForegroundColor Green
    }
    else {
        Write-Error "ffmpeg.exe not found in zip!"
    }
}
catch {
    Write-Error "ffmpeg download failed: $_"
}

# Verify
$yt = Test-Path "test_deps\yt-dlp.exe"
$ff = Test-Path "test_deps\ffmpeg.exe"

if ($yt -and $ff) {
    Write-Host "SUCCESS: Both files valid." -ForegroundColor Green
}
else {
    Write-Error "FAILURE: Missing files."
}
