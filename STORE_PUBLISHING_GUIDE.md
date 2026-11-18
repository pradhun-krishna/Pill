# Publishing Pill to the Microsoft Store

Everything in the codebase is now Microsoft Store–ready (MSIX-aware `StartupTask`, versioned assembly metadata, all 12 Store visual assets, `AppxManifest.xml`, `PRIVACY.md`, and `package-msix.ps1`).

## Step 1: Register & Reserve Your App Name in Microsoft Partner Center
1. Go to **https://partner.microsoft.com/dashboard** and sign in with your Microsoft Developer account (Individual account — one-time registration fee, no annual renewal).
2. Click **Apps and games → New product → MSIX or PWA app**.
3. Reserve the name **Pill** (if "Pill" is taken, reserve e.g. **Pill — Dynamic Notch** or **Pill Notch**).
4. Open **Product management → Product identity** and copy these 3 exact values:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher` (starts with `CN=...`)
   - `Package/Properties/PublisherDisplayName`

## Step 2: Paste Your Identity into `store-config.json`
Open `d:\Apps\notch\NotchWin\store-config.json` and paste those 3 exact values:
```json
{
  "PackageName": "YOUR_PACKAGE_IDENTITY_NAME",
  "Publisher": "CN=YOUR-PUBLISHER-GUID",
  "PublisherDisplayName": "Your Display Name",
  "Version": "1.0.0.0"
}
```

## Step 3: Install Windows SDK Tooling (One-Time) & Build Your `.msix`
Install the Windows SDK (provides `makeappx.exe` and `signtool.exe`):
```powershell
winget install Microsoft.WindowsSDK.10.0.22621
```
Then build the Store-ready `.msix` package:
```powershell
d:\Apps\notch\NotchWin\package-msix.ps1
```
Output: **`d:\Apps\notch\NotchWin\dist\Pill_1.0.0.0_x64.msix`**

*(Optional: Run `d:\Apps\notch\NotchWin\package-msix.ps1 -TestSign` to locally test-install the `.msix` on your PC before uploading. Note: You do **NOT** need to buy a code-signing certificate for Microsoft Store — Microsoft signs your uploaded `.msix` automatically!)*

## Step 4: Host Your Privacy Policy URL
Upload `d:\Apps\notch\NotchWin\PRIVACY.md` to a public GitHub repo (or GitHub Gist/Pages) and copy its public HTTPS URL for Partner Center.

## Step 5: Complete Your Store Submission in Partner Center
1. **Packages:** Upload `dist\Pill_1.0.0.0_x64.msix`.
2. **Properties:**
   - Category: **Utilities & tools** (or **Personalization**)
   - Privacy Policy URL: paste your hosted `PRIVACY.md` link.
3. **Age ratings:** Complete the quick IARC questionnaire (All ages / 3+).
4. **Store listings (English):**
   - **App Icon (300×300):** Upload `d:\Apps\notch\NotchWin\Package\Assets\StoreListing_300x300.png`
   - **Screenshots:** Capture 3–5 crisp 16:9 screenshots showing Pill's Media Player (Album Sync), Control Center, Widgets Bento, Quick Sites, and Live Device Pill.
   - **Short Description:**
     > Minimal native Windows dynamic notch with live media controls, synced lyrics, quick controls, widgets, clipboard history, and device alerts.
   - **Key Features:**
     - Ultra-lightweight native .NET 8 WPF & WinRT (~10 MB RAM, 0% idle CPU)
     - Attached Notch & Floating Island modes with 7 themes including live Album Sync
     - Auto-minimizes to a 5px slim hairline on fullscreen & maximized apps
     - Now Playing with wavy scrub bar, album glow & synced LRCLIB lyrics
     - Quick Sites, Quick Capture (Screenshot / Snip / Record) & 6-slot App Launcher
     - Control Center with vertical Brightness & Volume capsules, Wi-Fi, Bluetooth, Mic Mute & Power Plans
     - 2-page Widgets Bento (Calendar week strip, Focus timer ring, Weather, live CPU/RAM/Net sparklines, World Clock & To-Do)
     - Live Dynamic Island pills for Bluetooth devices, Audio Output, Volume, Brightness, Charger, USB, Caps Lock & Clipboard
5. **Submission Options → Notes for Certification** (important because MSIX uses `runFullTrust`):
   > Pill is a native desktop top-edge dynamic notch utility for Windows 10/11. It uses `runFullTrust` to display a non-activating topmost desktop overlay (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`), control system volume/brightness/power plans via native Windows APIs, register global shortcuts (`Ctrl+Alt+N`, `Ctrl+Alt+M`, `Ctrl+Alt+1–6`), monitor clipboard history while excluding password managers, and capture screenshots when requested by the user.
6. Click **Submit to the Store**! Certification typically completes within 24–72 hours.
