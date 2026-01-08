# Self-Extracting Single-File Launcher for GOON
# This creates a single .exe (or .ps1) that extracts and runs GOON

$ErrorActionPreference = "Stop"

Write-Host "Creating Single-File GOON Package..." -ForegroundColor Cyan

# 1. Create a compressed archive of the release folder
Write-Host "`n[1] Compressing release folder..." -ForegroundColor Yellow
if (Test-Path "GOON.zip") {
    Remove-Item "GOON.zip" -Force
}
Compress-Archive -Path "release\*" -DestinationPath "GOON.zip" -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item "GOON.zip").Length / 1MB, 2)
Write-Host "  Created GOON.zip ($zipSize MB)" -ForegroundColor Green

# 2. Create launcher script that will be embedded
$launcherScript = @'
# GOON Self-Extracting Launcher
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$tempDir = Join-Path $env:TEMP "GOON_$(Get-Random)"
$zipPath = Join-Path $tempDir "app.zip"

try {
    # Extract embedded zip from this script
    $scriptContent = Get-Content $PSCommandPath -Raw
    $zipStart = $scriptContent.IndexOf("PK") # ZIP magic bytes
    if ($zipStart -lt 0) { throw "Embedded archive not found" }
    
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    $bytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    [System.IO.File]::WriteAllBytes($zipPath, $bytes[$zipStart..($bytes.Length-1)])
    
    # Extract
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $tempDir)
    
    # Run GOON
    $goonExe = Join-Path $tempDir "GOON.exe"
    if (Test-Path $goonExe) {
        Start-Process -FilePath $goonExe -WorkingDirectory $tempDir -Wait
    }
} finally {
    # Cleanup
    if (Test-Path $tempDir) {
        Start-Sleep -Seconds 2
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
'@

# 3. Create the self-extracting executable
Write-Host "`n[2] Creating self-extracting executable..." -ForegroundColor Yellow

# Save launcher script
Set-Content "launcher.ps1" $launcherScript -Encoding UTF8

# Combine launcher + zip
$launcherBytes = [System.IO.File]::ReadAllBytes("launcher.ps1")
$zipBytes = [System.IO.File]::ReadAllBytes("GOON.zip")
$combined = $launcherBytes + $zipBytes
[System.IO.File]::WriteAllBytes("GOON.ps1", $combined)

Remove-Item "launcher.ps1" -Force

$finalSize = [math]::Round((Get-Item "GOON.ps1").Length / 1MB, 2)
Write-Host "  Created GOON.ps1 ($finalSize MB)" -ForegroundColor Green

Write-Host "`n✅ SUCCESS!" -ForegroundColor Green
Write-Host "`nSingle-file package created: GOON.ps1" -ForegroundColor Yellow
Write-Host "`nTo run: Right-click -> Run with PowerShell" -ForegroundColor Cyan
Write-Host "Or: powershell -ExecutionPolicy Bypass -File GOON.ps1`n" -ForegroundColor Cyan
