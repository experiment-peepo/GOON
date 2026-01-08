# Local Release Build Script (Simulates GitHub Actions)
$ErrorActionPreference = "Stop"

Write-Host "Starting Local Release Build..." -ForegroundColor Cyan

# 1. Cleanup
if (Test-Path "publish") {
    Write-Host "Cleaning up previous publish directory..."
    Remove-Item "publish" -Recurse -Force
}

# 2. Build and Publish (Framework-dependent - works with WPF!)
Write-Host "Building GOON project..." -ForegroundColor Yellow
dotnet publish GOON\GOON.csproj `
    -c Release `
    -p:Version=1.0.0 `
    -p:InformationalVersion=1.0.0 `
    -p:AssemblyVersion=1.0.0.0 `
    -p:FileVersion=1.0.0.0 `
    -o publish

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# 3. Copy Dependencies from local folder
Write-Host "Copying External Dependencies..." -ForegroundColor Yellow

if (-not (Test-Path "Dependencies\ffmpeg.exe")) {
    Write-Error "ffmpeg.exe not found in Dependencies folder!"
    exit 1
}

if (-not (Test-Path "Dependencies\yt-dlp.exe")) {
    Write-Error "yt-dlp.exe not found in Dependencies folder!"
    exit 1
}

Copy-Item "Dependencies\ffmpeg.exe" "publish\ffmpeg.exe" -Force
Write-Host "Copied ffmpeg.exe from Dependencies"

Copy-Item "Dependencies\yt-dlp.exe" "publish\yt-dlp.exe" -Force
Write-Host "Copied yt-dlp.exe from Dependencies"

# 4. Verification
Write-Host "`nVerifying Artifacts..." -ForegroundColor Yellow

$goonExe = Get-ChildItem "publish/GO*.exe" | Select-Object -First 1
$ytDlp = Test-Path "publish/yt-dlp.exe"
$ffmpeg = Test-Path "publish/ffmpeg.exe"

if ($goonExe -and $ytDlp -and $ffmpeg) {
    Write-Host "SUCCESS! All artifacts present:" -ForegroundColor Green
    Get-ChildItem "publish" -Filter "*.exe" | ForEach-Object { 
        Write-Host "$($_.Name) - $([math]::Round($_.Length/1MB, 2)) MB" 
    }
    Write-Host "`nNote: This is a framework-dependent build." -ForegroundColor Cyan
    Write-Host "Users need .NET 8 Runtime installed: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan

    # 5. Create Zip Package
    Write-Host "`n[5] Creating GOON.zip package..." -ForegroundColor Yellow
    if (Test-Path "GOON.zip") { Remove-Item "GOON.zip" -Force }
    Compress-Archive -Path "publish/*" -DestinationPath "GOON.zip" -CompressionLevel Optimal
    
    $zipSize = [math]::Round((Get-Item "GOON.zip").Length / 1MB, 2)
    Write-Host "`n✅ SUCCESS! Created GOON.zip ($zipSize MB)" -ForegroundColor Green
}
else {
    Write-Error "Verification Failed! Missing files."
}
