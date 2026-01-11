# Flyleaf Fork Implementation Plan

This plan outlines the technical changes required for the `Flyleaf` fork to support your project's specific requirements: **Transparent Overlays (D3DImage)** and **Multi-Monitor Frame-Exact Sync (Shared Clock)**.

## Goal
Transform Flyleaf from a "Standalone Player" into a "Pluggable Graphics Engine".

## Compatibility Verification
> [!NOTE]
> **.NET 8 Support:** Flyleaf natively targets `.net8.0-windows`. This matches modern architectures, ensuring seamless integration without legacy interop layers.
>
> **Dependencies:** The project uses **Vortice.Windows** (v3.7+) for DirectX 11, which is the modern, .NET 8-friendly successor to SharpDX. All other dependencies (MaterialDesign, FFmpeg bindings) are up-to-date for .NET 8.

## Proposed Changes

### 1. D3DImage Rendering Pipeline
To solve the **Airspace Problem**, we will implement a new renderer that outputs to a shared D3D11 texture compatible with WPF's `D3DImage`.

#### [NEW] FlyleafLib/MediaFramework/MediaRenderer/Renderer.D3DImage.cs
- Implement a `D3DImageManager` that handles the D3D11-to-D3D9 texture sharing.
- Use `ID3D11Texture2D` with `MiscFlags.Shared` to share frames with `D3DImage`.

#### [NEW] FlyleafLib/Controls/WPF/FlyleafD3DImage.cs
- A new WPF control that inherits from `Image` (instead of `ContentControl` with `HwndHost`).
- Uses `D3DImage` as the `Source`.
- This will support `AllowsTransparency="True"` on the parent window.

### 2. External Synchronization (Shared Clock)
To achieve **Frame-Exact Sync**, we will allow multiple player instances to follow a single clock source.

#### [MODIFY] FlyleafLib/MediaPlayer/Player.cs
- Add a new property `IClock ExternalClock { get; set; }`.
- Add a new `MasterClock.External` enum option.

#### [MODIFY] FlyleafLib/MediaPlayer/Player.Screamers.VASD.cs
- Modify the `ScreamerVASD` loop to prioritize `ExternalClock` if `MasterClock == External`.
- Logic: `currentPosition = ExternalClock.Position;` instead of calculating based on audio/system timer.

### 3. API Ergonomics for Custom Apps
#### [MODIFY] FlyleafLib/Engine/Config.cs
- Add explicit `UserAgent` and `Cookies` properties to `DemuxerConfig`.
- These will automatically be injected into the `FormatOpt` dictionary passed to FFmpeg.

## Dependency Strategy

> [!IMPORTANT]
> **FFmpeg Shared Libraries:** Unlike the current `ffmpeg.exe` (monolithic), Flyleaf requires **FFmpeg 8.0 Shared DLLs** (`avcodec-61.dll`, etc.).
> - We will bundle the **SuRGeoNix-patched** versions from the Flyleaf AIO release.
> - These include a critical HLS patch for CDN compatibility.
>
> **yt-dlp:** Existing `yt-dlp` binaries (e.g. from 2024/2025) are excellent for extraction, while Flyleaf handles high-performance playback using the DLLs.

---

## Verification Plan

### Automated Tests
- **Clock Drift Test:** Initialize two `Player` instances with a single `SharedClock` and verify that their `Position` properties stay within 1ms of each other over 5 minutes of playback.
- **Header Injection Test:** Mock `avformat_open_input` to verify that custom cookies/user-agents are correctly passed to the FFmpeg demuxer.

### Manual Verification
- **Transparency Test:** 
  1. Create a WPF window with `AllowsTransparency="True"` and `WindowStyle="None"`.
  2. Embed the new `FlyleafD3DImage` control.
  3. Verify that the video is visible AND elements behind the window (like the desktop) are correctly visible through transparent regions.
- **Multi-Monitor Stress Test:**
  1. Run two instances of Flyleaf on separate monitors.
  2. Trigger a seek on the "Master" instance.
  3. Verify that the "Follower" instance snaps to the same frame near-instantly.

---

> [!IMPORTANT]
> This fork will require the **Vortice.Windows** DirectX wrapper (already used by Flyleaf v3.9+). Ensure your project file strictly enforces `x64` to avoid bitness mismatches with the shared texture handle.
