# GOØN

![Build Status](https://github.com/experiment-peepo/TrainMe/actions/workflows/release.yml/badge.svg)
![License](https://img.shields.io/github/license/experiment-peepo/TrainMe)
![Architecture](https://img.shields.io/badge/Architecture-x64-blue)

**GOØN** is a specialized multi-screen video playback tool designed for immersive environments, therapy sessions, and installation art. It allows you to overlay video playback on specific monitors with independent opacity and audio controls, creating a seamless background experience without interfering with your primary workspace.

![GOØN UI](assets/screenshot.png)

## Table of Contents
- [Features](#features)

- [Quick Start](#quick-start)
- [Installation](#installation)
- [Supported Websites](#supported-websites)
- [Usage Guide](#usage-guide)
- [Building from Source](#building-from-source)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Multi-Monitor Support**: Assign videos to specific screens or span them across all monitors.
- **Independent Controls**: Adjust volume and opacity per video/layer.
- **Panic Button**: Instantly stop all playback with a global hotkey (Default: `Ctrl+Shift+End`).
- **Session Saving**: Automatically restores your last playlist, settings, and exact playback position.
- **Format Support**: Plays MP4, MKV, WebM, AVI, MOV, WMV, MPEG, and more - any format supported by Windows Media Foundation.
- **Advanced URL Import**: Hardened extraction engine for major media sites with specialized player support.
- **Modern Interface**: A minimalist, high-performance UI designed for ease of use.
- **Stealth Mode**: Designed to run as an unobtrusive, transparent overlay.


## Quick Start

1. **Download**: Grab the latest `GOØN.exe` from the [Releases](https://github.com/experiment-peepo/TrainMe/releases) page.
2. **Run**: Double-click `GOØN.exe`. No installation needed.
3. **Add Content**: Drag and drop video files or paste URLs into the launcher.
4. **Assign**: Select which monitor each video should play on.
5. **Start**: Click **Start All** to begin playback across your selected displays.

## Installation

GOØN is a **portable application**.
- **Prerequisites**: Windows 10/11 (x64).
- **Setup**: None. Just unzip and run.
- **Storage**: All settings and session data are stored in `%APPDATA%\GOON`.
- **Video Codec Requirements**: For playing **AV1** or **HEVC (H.265)** video files, you must install the official extensions from the Microsoft Store:
  - [AV1 Video Extension](https://www.microsoft.com/en-us/p/av1-video-extension/9mvzqvxjbq9v) (Free)
  - [HEVC Video Extensions](https://www.microsoft.com/en-us/p/hevc-video-extensions/9nmzlz57r3t7) (Paid) 

## Supported Websites

GOØN features a hardened extraction engine with support for GZip decompression and specialized player patterns. It can automatically import videos from:

- **Hypnotube** (hypnotube.com) - *Enhanced support for direct video, LD+JSON, and Plyr sources.*
- **Iwara** (iwara.tv) - *Support for direct video and JSON-embedded sources.*
- **PMVHaven** (pmvhaven.com) - *LD+JSON parsing for accurate playlist and video extraction.*
- **RULE34Video** (rule34video.com) - *Specialized token resolution and CDN fetching.*

You can also use **direct video URLs** (ending in `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, or `.m3u8` for HLS streams).

## Usage Guide

### The Launcher
The main window acts as your control center.
- **Playlist**: Manage your videos, drag to reorder, and see status/thumbnails.
- **Assignment**: Choose the target monitor and initial opacity per item.
- **Status Overlay**: Real-time feedback on active players and their status.
- **Volume/Opacity Mixer**: Global and individual sliders for perfect environmental tuning.

### Hotkeys
- **Panic Key**: `Ctrl+Shift+End` (configurable) immediately terminates all video windows.
- **Media Controls**: Quick access to Play/Pause/Skip within the launcher.

## Building from Source

To build GOØN yourself, you'll need the **.NET 8.0 SDK**.

1. **Clone the repo**
   ```bash
   git clone https://github.com/experiment-peepo/TrainMe.git
   cd TrainMe
   ```

2. **Build and Publish (PowerShell)**
   ```powershell
   .\publish.ps1
   ```
   *This script runs the tests and produces a single-file executable in the `publish/` folder.*

## Contributing

We welcome contributions!
- **Bug Reports**: Please use the [Bug Report Template](.github/ISSUE_TEMPLATE/bug_report.md).
- **Feature Requests**: Use the [Feature Request Template](.github/ISSUE_TEMPLATE/feature_request.md) for new ideas.

## License

Distributed under the [GNU General Public License v3.0](LICENSE). See `LICENSE` for more information.
