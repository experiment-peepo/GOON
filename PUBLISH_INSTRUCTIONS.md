# Publishing TrainMeX as Standalone Portable Executable

This document explains how to build TrainMeX as a standalone, portable executable that includes all dependencies and requires no installation.

## Prerequisites

- .NET 8.0 SDK installed on your development machine
- Windows operating system (for building Windows executables)

## Quick Start

### Option 1: Using the PowerShell Script (Recommended)
```powershell
.\publish.ps1
```

### Option 2: Using the Batch Script
```cmd
publish.bat
```

### Option 3: Manual Command
```bash
dotnet publish TrainMeX\TrainMeX.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Output

After successful build, the standalone executable will be located at:
```
publish\TrainMeX.exe
```

## What Makes It Portable?

1. **Self-Contained**: Includes the .NET 8.0 runtime, so no .NET installation is required on target machines.
2. **Single File**: All dependencies are bundled into one executable file.
3. **No Installation**: Can be run directly from any folder.
4. **User Data**: Settings, sessions, and logs are stored in `%APPDATA%\TrainMeX` (e.g., `C:\Users\<User>\AppData\Roaming\TrainMeX`), ensuring your configuration and debug info are preserved even if you move the executable.

## File Size

The executable is approximately 150-160 MB as it includes the entire .NET runtime and native libraries. This is normal for self-contained applications.

## Distribution

Simply copy the `TrainMeX.exe` file to any location. The application will:
- Automatically manage `settings.json` and `session.json` in the user's roaming AppData folder.
- Handle migration from legacy local settings if they exist.
- Work from any folder without requiring registry entries.

## Notes

- First launch may be slightly slower due to extraction, but subsequent launches are fast
- The executable is optimized for Windows x64 systems
- All data files are stored relative to the executable location


