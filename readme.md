# TrainMeX

TrainMeX is a hypnosis software application built using WPF (Windows Presentation Foundation) framework. It enables video playback in fullscreen mode across multiple screens with independent settings for opacity, sound, and video playlists per screen.

## Features

- **Multi-screen video playback** with independent settings
- **Per-screen controls** for opacity and sound
- **Custom video playlists** per screen
- **Global hotkey support** for quick access
- **Portable standalone executable** - no installation required
- **Comprehensive test suite** for reliability

## Requirements

- **Windows** operating system (x64)
- **.NET 8.0 Runtime** (included in self-contained builds, not required for standalone executables)

## Quick Start

1. Download or build the application (see [Building](#building) section)
2. Run `TrainMeX.exe`
3. Add video files and assign them to screens
4. Configure opacity and volume per screen
5. Start playback

For detailed build instructions, see [PUBLISH_INSTRUCTIONS.md](PUBLISH_INSTRUCTIONS.md).

## Building

The application can be built as a standalone, portable executable. See [PUBLISH_INSTRUCTIONS.md](PUBLISH_INSTRUCTIONS.md) for detailed build instructions.

### Quick Build Options

**PowerShell (Recommended):**
```powershell
.\publish.ps1
```

**Batch:**
```cmd
publish.bat
```

**Manual:**
```bash
dotnet publish TrainMeX\TrainMeX.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The executable will be generated in the `publish` directory.

## Usage

- Add video files through the launcher interface
- Assign videos to specific screens
- Configure opacity (transparency) and volume settings per screen
- Use global hotkeys for quick control (default: Ctrl+Shift+End for panic/stop)
- Settings and session data are automatically saved to JSON files in the executable directory

## Testing

The project includes comprehensive unit and integration tests. See [TrainMeX.Tests/README.md](TrainMeX.Tests/README.md) for testing information.

Run tests with:
```bash
dotnet test
```

## Known Issues

- **Playlist Import**: The playlist import functionality is currently broken and needs further testing. Importing playlists from external sources may not work as expected. Manual file addition is recommended as an alternative.

## License

This software is licensed under the **GNU General Public License version 3 (GPLv3)**.

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [GNU General Public License](https://www.gnu.org/licenses/) for more details.

You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

See [license.txt](license.txt) for the full license text.

## Copyright

Copyright (C) 2021 Damsel
