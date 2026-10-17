# Shiny Notch

A menu-bar app for macOS that turns the MacBook camera notch into a live panel. It's built with .NET MAUI on the AppKit head ([maui-labs](https://github.com/dotnet/maui-labs)) and Shiny Controls' `NotchView` / `INotchWindowService`.

- **Peek.** While collapsed, the notch shows what's playing (artwork) to the left of the camera, and a running timer or a meeting in the next 15 minutes to the right. With nothing to show, it disappears into the camera housing.
- **Hover to expand** into a panel. Its tabs sit either side of the camera:
  - **Now Playing**: artwork, title, position, previous/play/next for Apple Music and Spotify, plus volume and display brightness sliders.
  - **File Tray**: drop files onto the notch to shelve them. Copy them back out (⌘V in Finder pastes them), AirDrop, reveal or open them, or clear the tray. Files that move or are deleted drop off automatically.
  - **System**: CPU with a sparkline, GPU, memory, disk read/write and network up/down, once a second.
  - **Calendar**: the rest of today and tomorrow, from every calendar.
  - **Timer**: 1–45 minute presets, +1m, pause and cancel. It chimes and opens the notch when it ends.
  - **Teleprompter**: your script scrolls right under the camera, with play/pause, nudge, speed and text size. The notch stays open while it runs.
- **HUD**: volume and brightness changes flash in the notch.
- **Pin** (⦿) keeps the panel open. **Settings** (⚙ or the menu-bar icon) holds the teleprompter script, the HUD toggles, permissions and launch at login.

No Dock icon: it's an `LSUIElement` agent, so quit it from the menu-bar icon.

## Build and run

```bash
dotnet build
open src/ShinyNotch/bin/Debug/net10.0-macos/osx-arm64/ShinyNotch.app
```

Shiny Controls comes from NuGet (`1.9.0-beta-*`). To build against a local checkout of the controls repo at `../controls` instead, so you can change the notch control and the app together:

```bash
dotnet build -p:UseLocalShinyControls=true
```

Debug builds accept `--open <Media|Tray|Stats|Calendar|Timer|Prompter>` to start with the panel pinned open on a tab, which makes the UI drivable without a mouse:

```bash
src/ShinyNotch/bin/Debug/net10.0-macos/osx-arm64/ShinyNotch.app/Contents/MacOS/ShinyNotch --open Stats
```

## Permissions

| Feature | Asks for | Notes |
|---|---|---|
| Now Playing | Automation (Music, Spotify) | Asked once per player, the first time it's running. A player that isn't running is never contacted, because asking it would launch it. |
| Calendar | Calendars, full access | From the Calendar tab or Settings. |
| Volume, brightness, stats, file tray, timer, teleprompter | nothing | |

## How it works

- `NotchController` runs the notch in one of three modes (peek, panel, HUD). It rebuilds the content when its structure changes and updates text in place otherwise. The AppKit head never creates native views for children added to an on-screen layout, so a content swap is a fresh page.
- **Now Playing** uses AppleScript (`NSAppleScript`), not MediaRemote. Since macOS 15.4 the system-wide now-playing API returns nothing to apps without an Apple entitlement, so only Music and Spotify are seen.
- **Volume** uses CoreAudio's virtual main volume on the default output device. **Brightness** uses the private `DisplayServices` framework, the same call Control Centre uses; there is no public API on Apple silicon.
- **Stats** come from mach `host_statistics`, `sysctl hw.memsize`, IOKit (`IOAccelerator` for GPU utilisation, `IOBlockStorageDriver` for disk bytes) and `getifaddrs` for network bytes.
- **Text entry** lives in Settings, an ordinary window. The notch window is borderless and can never take keyboard focus.

## Known limits

- Now Playing only sees Apple Music and Spotify (see above).
- Brightness is the built-in display only; most external monitors don't report it.
- Launch at login uses `SMAppService` and only works for a signed app in `/Applications`.
- Files can't be dragged *out* of the tray yet (the AppKit head has no drag source). Use Copy, then paste in Finder.
