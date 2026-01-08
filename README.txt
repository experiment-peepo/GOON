GOON v1.0
=========

A specialized video player designed for multi-monitor playback with high-performance overlay capabilities.

KEY CAPABILITIES
----------------
* Multi-Monitor Support: Assign different videos to specific screens or play across "All Monitors".
* Overlay Mode: High-performance transparent overlays that play directly on your desktop.
* Truly Portable: Stores all settings, logs, and sessions in a local 'Data' folder if write access is available.
* Web Integration: Stream directly from supported sites using integrated yt-dlp support.

DEPENDENCIES
------------
* .NET 8 Desktop Runtime: Required to be installed (https://dotnet.microsoft.com/download/dotnet/8.0).
* Bundled Tools: 'ffmpeg' and 'yt-dlp' are already included in this package.

QUICK START
-----------
1. Extract the contents of this zip to a folder.
2. Run GOON.exe.
3. Drag and drop videos or paste URLs to get started.

KNOWN NOTES
-----------
* Folder Distribution: This version is distributed as a folder bundle for maximum UI stability on .NET 8.
* Data Storage: If the app cannot create a local 'Data' folder (e.g., in a protected Program Files directory), it will fall back to %AppData%\GOON.

---
Support the development on Ko-fi: https://ko-fi.com/vexfromdestiny
