<div align="center">

# 🎵 AuraStream

**A modern, lightweight desktop audio player and streaming suite built with .NET 10 & Avalonia UI.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-12.1.2-8E44AD?style=flat-square)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20Cross--Platform-FCC624?style=flat-square&logo=linux&logoColor=black)](https://github.com/cyberps96/AuraStream)
[![License](https://img.shields.io/badge/License-MIT-success?style=flat-square)](LICENSE)

*Stream music seamlessly from YouTube Music with a modern Spotify-inspired dark emerald interface, dynamic playlist management, and native LibVLC audio.*

</div>

---

## ✨ Features

- **🎧 High-Fidelity Audio Streaming**: Powered by native LibVLC and YoutubeExplode for low-latency, gapless playback.
- **🎨 Modern Dark Emerald Interface**: Crafted with Avalonia UI, scalable vector icons, fluent design, and responsive layouts.
- **🔍 Instant Search & Discovery**: Search songs, artists, and albums in real time with interactive top results and audio metadata analysis.
- **📑 Playlist & Library Management**: Create, organize, and manage custom playlists and Liked Songs with full right-click context menu control.
- **⚡ Smart Stream Caching**: In-memory caching and predictive preloading of adjacent tracks for instant, interruption-free track switching.
- **🔒 Google Account Integration**: Seamless 1-click browser sign-in syncing account name and avatar picture securely.
- **🎛️ Responsive Player Controls**: Microsecond-accurate seek slider, persistent volume controls, and deterministic play/pause state synchronization.

---

## 🛠️ Tech Stack

| Component | Technology | Description |
| :--- | :--- | :--- |
| **Runtime** | [.NET 10](https://dotnet.microsoft.com/) | High-performance C# runtime |
| **GUI Framework** | [Avalonia UI](https://avaloniaui.net/) | Cross-platform XAML GUI |
| **Audio Engine** | [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) | Native LibVLC C binding for robust audio decoding |
| **Stream Extraction** | [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) | YouTube stream resolution and metadata extraction |
| **Image Loading** | [AsyncImageLoader.Avalonia](https://github.com/IvanJosipovic/AsyncImageLoader.Avalonia) | High-performance async thumbnail rendering |

---

## 🚀 Getting Started

### Prerequisites

1. **.NET 10 SDK**:
   Install the .NET 10 SDK from the [official Microsoft website](https://dotnet.microsoft.com/download/dotnet/10.0) or your distribution's package manager.

2. **LibVLC runtime libraries**:
   - **Arch / CachyOS / Manjaro**:
     ```bash
     sudo pacman -S vlc
     ```
   - **Ubuntu / Debian**:
     ```bash
     sudo apt install vlc libvlc-dev
     ```
   - **Fedora**:
     ```bash
     sudo dnf install vlc vlc-devel
     ```

### Installation & Launch

1. **Clone the repository**:
   ```bash
   git clone https://github.com/cyberps96/AuraStream.git
   cd AuraStream
   ```

2. **Restore dependencies & build**:
   ```bash
   dotnet build
   ```

3. **Run the application**:
   ```bash
   dotnet run
   ```

---

## 📂 Project Structure

```text
AuraStream/
├── Models/
│   ├── PlaylistModel.cs         # Playlist data contracts
│   └── TrackItem.cs             # Track metadata & playback state
├── Services/
│   ├── AudioEngine.cs           # LibVLC audio playback & event engine
│   ├── AuthManager.cs           # Google profile & session management
│   ├── BrowserAuthService.cs    # Chromium CDP 1-click Google auth
│   ├── PlaylistManager.cs       # Persistent local playlist storage
│   └── StreamCacheService.cs    # YouTube audio stream resolver & cache
├── Views/
│   ├── CreatePlaylistDialog.*   # Playlist creation dialog
│   └── LoginWindow.*            # Google sign-in modal window
├── App.axaml                    # Application styling & FluentTheme
├── MainWindow.axaml             # Main desktop player view
├── Program.cs                   # Application entry point
└── sopfiy.csproj                # Project dependencies & build config
```

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!
Feel free to check the [issues page](https://github.com/cyberps96/AuraStream/issues) if you want to contribute.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
