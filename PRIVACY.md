# Privacy Policy for Pill

**Last Updated:** 2026

Pill is a native Windows desktop utility designed with a local-first architecture. We do not collect, store, sell, or transmit personal analytics, telemetry, or tracking data.

## 1. Data Stored Locally on Your Device
All user data created or managed by Pill is stored strictly locally inside your Windows user profile (`%AppData%\Pill`):
- **Preferences (`settings.json`):** Theme, notch shape, monitor selection, and display toggles.
- **Notes & To-Do items (`notes.json`)**
- **Pinned Quick-Launch Apps & Quick Sites (`apps.json`, `sites.json`)**
- **Pinned Clipboard Items (`pinned-clips.json`):** Clipboard history is held locally in memory (and only items you explicitly pin are saved locally). Pill automatically ignores password manager and sensitive clipboard entries flagged by Windows (`ExcludeClipboardContentFromMonitorProcessing` / `CanIncludeInClipboardHistory`).

## 2. Optional Network Requests
Pill connects to the internet only for two optional user-facing features:
- **Weather Widget:** Requests approximate city-level coordinates via IP geolocation (`ipapi.co` / `ipwho.is`) and fetches current weather and daily High/Low forecasts from **Open-Meteo** (`api.open-meteo.com`). No personal identifiers or account information are sent.
- **Synced Lyrics (On-Demand):** Only when you open the Lyrics drawer in the media player, Pill queries **LRCLIB** (`lrclib.net`) with the currently playing song's title, artist, album, and duration to retrieve synced `.lrc` lyrics.

## 3. System Capabilities Used Locally
- **Media Controls & Thumbnails:** Reads Windows System Media Transport Controls (SMTC) locally to display now-playing metadata and controls.
- **Microphone & Camera Privacy Indicator:** Reads Windows Registry `CapabilityAccessManager\ConsentStore` metadata locally to show an orange/green indicator when another app is using your microphone or camera. Pill **never** accesses, records, or transmits audio or camera feeds.
- **Bluetooth & Device Alerts:** Uses Windows Device Enumeration APIs locally to show brief connection pills when paired Bluetooth devices, power adapters, or USB drives connect or disconnect.
- **Screenshots:** When you click the Screenshot button, Pill saves the capture locally to your `Pictures\Screenshots` folder and Windows Clipboard.

## 4. Contact
For privacy questions or support, please contact the publisher via the support link listed on the Microsoft Store product page.
