# Future Features & Enhanced Site Support

## Summary
This document outlines the implementation strategy for the next wave of features focused on the PMV/HMV/Sissy Hypno niche.

## Proposed New Site Support

### 1. [Iwara.tv](https://iwara.tv) (High Priority)
- **Goal**: Add support for the large 3D/anime PMV community.
- **Method**: Use `api.iwara.tv`.
- **Flow**:
  1. Extract ID from URL (`/video/abcxyz` -> `abcxyz`).
  2. Request metadata from `https://api.iwara.tv/video/{id}`.
  3. Resolve video source (may require auth header for some content).
- **Challenge**: Iwara uses a custom player and often rotates keys/endpoints.

### 2. [Erome.com](https://erome.com) (High Priority)
- **Goal**: Support popular album-based hosting.
- **Method**: Custom scraper.
- **Challenge**: Erome often uses multiple shards/CDNs.

---

## Proposed Advanced Features

### 1. Hypnotic Visual Overlays
- **Spiral Overlays**: Implement a `SpiralOverlay` control in XAML using `Composition` or `DrawingVisual` for high-performance animated spirals.
- **Color Tinting**: Add a `ColorMatrix` effect to the `MediaElement` (or its overlay) to tint video output.
- **Subliminal Flashes**: A background timer that toggles the visibility of an overlay text/image for 16-32ms.

### 2. Playback Optimization
- **Seamless Looping (A-B)**: Allow users to mark Start/End points for a specific segment.
- **Crossfade**: Difficult with WPF `MediaElement`, but can be simulated using TWO media elements and animating their Opacity in `HypnoWindow`.
- **Speed Ramping**: Dynamic speed control (0.5x to 2.0x) with keyboard shortcuts.

### 3. Audio Enhancement
- **Background Music/Ambient Layer**: A secondary `MediaPlayer` that plays a local file or a configured "Binaural Beats" track alongside the main video.

---

## Technical Feasibility

| Feature | Difficulty | WPF Component |
|---------|------------|---------------|
| Iwara Support | 🟡 Medium | `VideoUrlExtractor` |
| Spiral Overlays | 🟡 Medium | `DrawingVisual` / `Canvas` |
| Crossfade | 🔴 Hard | Double `MediaElement` |
| Color Tinting | 🟢 Easy | `ShaderEffect` |
| Binaural Beats | 🟢 Easy | `MediaPlayer` |

---

## Verification Plan
1. **Site Support**: Test specific URLs from each site in the `PlaylistImporter`.
2. **Visuals**: Measure CPU/GPU impact of overlays in `HypnoWindow`.
3. **Crossfade**: Ensure no "flicker" during element switching.
