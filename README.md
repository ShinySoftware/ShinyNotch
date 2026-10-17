<p align="center">
  <img src="docs/screenshots/icon.png" width="128" alt="Shiny Notch icon" />
</p>

# Shiny Notch

A menu-bar app for macOS that turns the MacBook camera notch into a live panel. It's built with .NET MAUI on the AppKit head ([maui-labs](https://github.com/dotnet/maui-labs)) and Shiny Controls' `NotchView` / `INotchWindowService`.

Hover the notch to open it, and move away to let it fold back into the camera. Apple silicon only.

<p align="center">
  <img src="docs/screenshots/peek.png" width="372" alt="The collapsed notch: album art left of the camera, the temperature right of it" />
</p>

## Features

### Peek

While collapsed, the notch shows a "wing" either side of the camera:
- **Left:** what's playing.
- **Right:** a running timer, a meeting starting in the next 15 minutes, or the temperature where you are, in that order.

With nothing to show, the notch disappears into the camera housing.

### Now Playing
Artwork, title, position and previous/play/next for Apple Music and Spotify, with volume and display brightness sliders.

<img src="docs/screenshots/media.png" width="528" alt="Now Playing tab" />

### Weather
Like the iOS widget: the conditions now, the next twelve hours, and the week. Each day's low-to-high bar sits on the week's range, and today's bar has a dot at the current temperature. It works from where you are, or any cities you add in Settings; switch between them with ‹ ›. Temperatures are °C or °F, automatic by region: change it in Settings, or tap the temperature to flip it.

<img src="docs/screenshots/weather.png" width="528" alt="Weather tab with hourly and 7-day forecast" />

### File Tray
Drop files onto the notch to shelve them. Copy them back out (⌘V in Finder pastes them), AirDrop them, reveal or open them, or clear the tray. Files that move or are deleted drop off on their own.

<img src="docs/screenshots/tray.png" width="528" alt="File Tray tab" />

### System
CPU with a sparkline, GPU, memory, disk read/write and network up/down, updated once a second.

<img src="docs/screenshots/stats.png" width="528" alt="System stats tab" />

### Calendar
The rest of today and tomorrow, from every calendar you have.

<img src="docs/screenshots/calendar.png" width="528" alt="Calendar tab" />

### Timer
Presets from 1 to 45 minutes, plus +1m, pause and cancel. The countdown shows in the peek, and the notch chimes and opens when it ends.

<img src="docs/screenshots/timer.png" width="528" alt="Timer tab" />

### Teleprompter
Your script scrolls right under the camera, so you read while looking at the lens. There's play/pause, nudge, speed and text size, and the notch stays open while it runs. Edit the script in Settings.

<img src="docs/screenshots/teleprompter.png" width="528" alt="Teleprompter tab" />

### Volume and brightness HUD
Changes from the keys or Control Centre flash in the notch.

<img src="docs/screenshots/hud.png" width="450" alt="Volume HUD in the notch" />

### And
- **Pin** (⦿) keeps the panel open.
- **Open at login**, from the menu-bar menu (a checkmark) or Settings. The first time, macOS usually asks you to approve it in System Settings ▸ General ▸ Login Items; the app says so and opens that page. If you turn it off there later, the app shows that too, rather than a switch that's lying.
- **Settings** (⚙ in the panel, or the menu-bar icon) holds the teleprompter script, weather places and units, the HUD toggles, permissions and open at login.
- It's an agent app (`LSUIElement`) with no Dock icon; quit it from the menu-bar icon.

## Build and run

```bash
dotnet build
open src/ShinyNotch/bin/Debug/net10.0-macos/osx-arm64/ShinyNotch.app
```

For a release build:

```bash
dotnet build -c Release
open src/ShinyNotch/bin/Release/net10.0-macos/osx-arm64/ShinyNotch.app
```

Debug builds accept `--open <Media|Weather|Tray|Stats|Calendar|Timer|Prompter>` to start with the panel pinned open on a tab, which makes the UI drivable without a mouse:

```bash
src/ShinyNotch/bin/Debug/net10.0-macos/osx-arm64/ShinyNotch.app/Contents/MacOS/ShinyNotch --open Weather
```

The icon's source is `src/ShinyNotch/Resources/AppIcon/appicon.svg`. After editing it, re-render the PNG the build uses:

```bash
rsvg-convert -w 1024 -h 1024 src/ShinyNotch/Resources/AppIcon/appicon.svg -o src/ShinyNotch/Resources/AppIcon/appicon.png
```

## Libraries

| Package | Version | Used for |
|---|---|---|
| [Microsoft.Maui.Controls](https://www.nuget.org/packages/Microsoft.Maui.Controls) | 10.0.100 | The UI framework |
| [Microsoft.Maui.Platforms.MacOS](https://github.com/dotnet/maui-labs) | 0.1.0-preview.13 | .NET MAUI on native AppKit (`net10.0-macos`), not Mac Catalyst |
| [Microsoft.Maui.Platforms.MacOS.Essentials](https://github.com/dotnet/maui-labs) | 0.1.0-preview.13 | MAUI Essentials for the AppKit head |
| [Shiny.Core](https://www.nuget.org/packages/Shiny.Core) | 6.3.0 | `IAppStartupInstaller`: open at login (an `SMAppService` login item), with the approval and turned-off states |
| [Shiny.Maui.Controls](https://www.nuget.org/packages/Shiny.Maui.Controls) | 1.9.0-beta-0004 | `NotchView`, the surface that morphs to fit its content |
| [Shiny.Maui.Controls.Desktop](https://www.nuget.org/packages/Shiny.Maui.Controls.Desktop) | 1.9.0-beta-0004 | `INotchWindowService` (the notch window, hover tracking, camera cut-out size), the menu-bar tray icon, and window-level file drop |
| [Microsoft.Maui.DevFlow.Agent](https://github.com/dotnet/maui-labs) | 0.1.0-preview.13 | Debug builds only: UI inspection and automation |

| Apple framework / service | Used for |
|---|---|
| AppKit (`NSAppleScript`, `NSWorkspace`, `NSPasteboard`, `NSSharingService`) | Now Playing in Music and Spotify; the file tray's copy, reveal, open and AirDrop |
| CoreAudio / AudioToolbox | Output volume and mute |
| DisplayServices (private) | Built-in display brightness; there is no public API on Apple silicon |
| IOKit, mach `host_statistics`, `sysctl`, `getifaddrs` | GPU utilisation, disk bytes, CPU and memory, network bytes |
| EventKit | Calendar events |
| CoreLocation (`CLLocationManager`, `CLGeocoder`) | Where you are for the weather, and its name |
| [Open-Meteo](https://open-meteo.com) | Forecasts and city search: free, no API key |

## Permissions

| Feature | Asks for | Notes |
|---|---|---|
| Now Playing | Automation (Music, Spotify) | Asked once per player, the first time it's running. A player that isn't running is never contacted, because asking it would launch it. |
| Weather | Location | Only when "Show the weather where I am" is on. Cities you add need nothing. |
| Calendar | Calendars, full access | From the Calendar tab or Settings. |
| Everything else | nothing | |

## How it works

- `NotchController` runs the notch in one of three modes (peek, panel, HUD). It rebuilds the content when its structure changes and updates text in place otherwise. The AppKit head never creates native views for children added to an on-screen layout, so a content swap is a fresh page, and so is a change to the Settings page's lists.
- **Now Playing** uses AppleScript, not MediaRemote. Since macOS 15.4 the system-wide now-playing API returns nothing to apps without an Apple entitlement, so only Music and Spotify are seen.
- **Weather** comes from Open-Meteo, which converts units server-side, so switching °C/°F refetches. Forecasts are cached for 20 minutes per place.
- **Text entry** lives in Settings, an ordinary window. The notch window is borderless and can never take keyboard focus.

## Known limits

- Now Playing only sees Apple Music and Spotify (see above).
- Brightness is the built-in display only; most external monitors don't report it.
- Open at login registers the app wherever it is when you switch it on. Move it to `/Applications` first, or the login item points at the old location.
- Files can't be dragged *out* of the tray yet (the AppKit head has no drag source). Use Copy, then paste in Finder.
