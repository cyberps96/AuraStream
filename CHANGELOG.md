# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned
- **Phase 2**:
  - Playback priority queue ("Play Next", "Add to Queue") and right-side slide-out queue drawer.
  - Storage directory migration from legacy `sopfiy` to `~/.config/AuraStream/` (with automatic data migration preserving existing playlists and login sessions).
  - Official Application Icon & Branding: Set high-resolution AuraStream icon (`assets/icon.png`) for desktop window titlebar and sidebar header logo.
- **Phase 3**: Linux MPRIS2 D-Bus service (`org.mpris.MediaPlayer2`) for hardware media keys, lock screen controls, and desktop sound applet.
- **Phase 4**: YouTube Music Account Playlist Sync:
  - Synchronize user playlists and "Liked Music" directly from the connected Google / YouTube Music account session.
  - Automatically populate the sidebar with cloud playlists alongside local playlists.
  - Cloud-sync track likes between AuraStream and YouTube Music library.

## [1.1.0] - 2026-09-21

### Added
- **Playback Coordinator Service** (`Services/PlaybackCoordinator.cs`): Decoupled controller encapsulating playback sequencing, repeat policies, shuffle permutations, and mute memory.
- **3-State Repeat Modes**: Interactive cycle between `Off`, `Repeat All`, and `Repeat One` with dynamic vector icon and tooltip state synchronization.
- **Zero-Latency Single-Track Looping**: In `Repeat One` mode, tracks seamlessly seek to `0:00` without redundant YouTube stream re-resolutions.
- **Fisher-Yates Shuffle Engine**: Non-repeating random sequence generation that pins the currently playing song to index `0` to prevent abrupt interruptions or duplicates.
- **1-Click Volume Mute & Restore**: Clickable speaker icon toggles audio mute, caches the last active volume (default 75%), and smoothly restores it upon unmute.
- **Stream Failure Circuit Breaker**: Resilient 3-retry fallback mechanism that automatically skips corrupted or rate-limited streams to keep music playing.

### Changed
- Transformed static placeholder controls (`Shuffle`, `Repeat`, `Volume`) in `MainWindow.axaml` into fully interactive player widgets.
- Updated track skipping and auto-advancement pipelines in `MainWindow.axaml.cs` to query `PlaybackCoordinator` resolution logic.
- Playlist switching and search queries automatically synchronize and re-index the shuffle permutation pool without index-out-of-bounds exceptions.

## [1.0.0] - 2026-09-20

### Added
- Initial standalone release of AuraStream for Linux x64 (.NET 10 & Avalonia UI 12.1.2).
- Native LibVLC audio decoding pipeline via `LibVLCSharp` for low-latency playback.
- Dynamic YouTube stream resolution and metadata indexing via `YoutubeExplode`.
- Dark emerald Spotify-inspired interface with responsive player controls and seek slider.
- Real-time search and discovery with interactive top result matching and audio analytics.
- Custom playlist management, JSON persistence, and Liked Songs library.
- Google sign-in and user avatar synchronization via Chromium CDP.
- High-performance asynchronous thumbnail rendering via `AsyncImageLoader.Avalonia`.
