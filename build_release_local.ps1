param(
    [switch]$Update
)

$ErrorActionPreference = "Stop"

if ($Update) {
    Write-Host "Updating dependencies before build..." -ForegroundColor Cyan
    & "$PSScriptRoot\update_dependencies.ps1"
}

# Path to Project File
$projectFile = Join-Path $PSScriptRoot "GOON\GOON.csproj"

if (-not (Test-Path $projectFile)) {
    Write-Error "Project file not found at $projectFile"
    exit 1
}

# 1. Read and Increment Version
Write-Host "Reading current version..." -ForegroundColor Cyan
$content = Get-Content $projectFile -Raw
$versionPattern = "(?<=<Version>)(.*?)(?=</Version>)"

if ($content -match $versionPattern) {
    $currentVersionStr = $matches[0]
    try {
        $version = [Version]$currentVersionStr
        # Increment Patch (Build) number. 
        # Note: [Version] parses "1.0.2" as Major=1, Minor=0, Build=2.
        # We want to increment 'Build' (the 3rd number) or 'Revision' if 4 numbers exists? 
        # Usually semantic versioning is Major.Minor.Patch. .NET Version is Major.Minor.Build.Revision.
        # So matching 3rd component is correct.
        
        $newVersion = [Version]::new($version.Major, $version.Minor, $version.Build + 1)
        
        Write-Host "Incrementing version: $currentVersionStr -> $newVersion" -ForegroundColor Green
        
        # Update Content - Use simple replacement to avoid backreference ambiguity
        # Replace <Version>...</Version> with <Version>NewVersion</Version>
        $newContent = $content -replace "<Version>.*?</Version>", "<Version>$newVersion</Version>"
        Set-Content -Path $projectFile -Value $newContent -NoNewline
    }
    catch {
        Write-Error "Failed to parse or increment version '$currentVersionStr'. Error: $_"
        exit 1
    }
}
else {
    Write-Error "Could not find <Version> tag in $projectFile"
    exit 1
}

Write-Host "Starting Release Build v$newVersion..." -ForegroundColor Cyan

# 2. Cleanup
if (Test-Path "publish") {
    Write-Host "Cleaning up previous publish directory..."
    Remove-Item "publish" -Recurse -Force
}

# 3. Build and Publish (Self-Contained Single File)
Write-Host "Building GOON project..." -ForegroundColor Yellow
dotnet publish GOON\GOON.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$newVersion `
    -p:InformationalVersion=$newVersion `
    -p:AssemblyVersion="$($newVersion.Major).$($newVersion.Minor).$($newVersion.Build).0" `
    -p:FileVersion="$($newVersion.Major).$($newVersion.Minor).$($newVersion.Build).0" `
    -o publish

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# 4. Copy Dependencies from local folder
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

Copy-Item "README.txt" "publish\README.txt" -Force
Write-Host "Copied README.txt to publish folder"

# 5. Create Data Folder (Portable Mode)
Write-Host "Creating Data folder for Portable Mode..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "publish\Data" | Out-Null

# 6. Verification
Write-Host "`nVerifying Artifacts..." -ForegroundColor Yellow

$goonExe = Get-ChildItem "publish/GO*.exe" | Select-Object -First 1
$ytDlp = Test-Path "publish/yt-dlp.exe"
$ffmpeg = Test-Path "publish/ffmpeg.exe"

if ($goonExe -and $ytDlp -and $ffmpeg) {
    Write-Host "SUCCESS! All artifacts present:" -ForegroundColor Green
    Get-ChildItem "publish" -Filter "*.exe" | ForEach-Object { 
        Write-Host "$($_.Name) - $([math]::Round($_.Length/1MB, 2)) MB" 
    }
    Write-Host "`nNote: This is a Self-Contained Single-File build." -ForegroundColor Cyan
    Write-Host "Runs on Windows x64 without external .NET installation." -ForegroundColor Cyan

    # 7. Create Zip Package
    Write-Host "`n[7] Creating GOON.zip package..." -ForegroundColor Yellow
    if (Test-Path "GOON.zip") { Remove-Item "GOON.zip" -Force }
    Compress-Archive -Path "publish/*" -DestinationPath "GOON.zip" -CompressionLevel Optimal
    
    $zipSize = [math]::Round((Get-Item "GOON.zip").Length / 1MB, 2)
    Write-Host "`n✅ SUCCESS! Created GOON.zip ($zipSize MB) for version $newVersion" -ForegroundColor Green
}
else {
    Write-Error "Verification Failed! Missing files."
}
