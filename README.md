# Pill — Dynamic Notch for Windows

A minimal, ultra-lightweight native Windows 10/11 dynamic notch built in **C# / .NET 8 WPF + WinRT/Win32** (~9–12 MB RAM, 0% idle CPU).

## Features
- **Attached Notch & Floating Island** modes with 7 themes including live **Album Sync** (adapts background & rim glow to active cover art)
- **Auto Slim Hairline** on fullscreen & maximized apps (unobstructed browser tabs, Alt+Tab stability, works over overlapping overlays)
- **Now Playing** with wavy sine-wave scrub bar, inline volume slider & on-demand **LRCLIB synced lyrics**
- **Quick Sites** (one-click tinted web shortcuts) & **Quick Capture** (`Screenshot`, `Snip`, `Record`) + 6-slot App Launcher (`Ctrl+Alt+1–6`)
- **Control Center** with vertical Brightness & Volume capsules, Wi-Fi, Bluetooth, Global Mic Mute (`Ctrl+Alt+M`) & Windows Power Plans
- **2-Page Widgets Bento** (7-day Calendar strip, Focus timer ring, Weather H/L, live CPU/RAM/Net sparklines, World Clock, To-Do & System Rings)
- **Clipboard History** (deduplicated text, links & images, password-manager exclusion)
- **Live Dynamic Island Pills** for Bluetooth devices, Audio Output, Volume, Brightness, Charger/Battery, USB drives, Caps/Num Lock, Track Changes & Clipboard

## Build & Package
- **Local Release Build & Run:** `.\build.ps1`
- **Microsoft Store MSIX Package:** `.\package-msix.ps1`

## Privacy
See [PRIVACY.md](PRIVACY.md).
