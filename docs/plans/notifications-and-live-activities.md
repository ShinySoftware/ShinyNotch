# Plan: notifications and Live Activities

Shiny Notch sits over the top-centre of the screen, above the menu bar and every app. macOS shows its own transient UI up there too: notification banners and alerts, and (from macOS 26) Live Activities mirrored from a paired iPhone. This plan covers two things:

1. **Get out of the way.** The notch must never cover a banner, an alert or a Live Activity.
2. **Mirror notifications, as an option.** Show incoming notifications in the notch, Dynamic Island style.

Intercepting (stopping macOS from showing its own banner) is out of scope: there is no public API for it. The only way to suppress system banners is the user's own Focus or notification settings.

## What we know today

Checked on this Mac (macOS 27.0.1, Apple silicon) on 21 Oct 2026.

- **The window list needs no permission for owners and positions.** `CGWindowListCopyWindowInfo` returns every on-screen window's owner, layer and bounds without Screen Recording permission. Only window *titles* need it. `MacShotService.IsRecording` already relies on this to spot macshot's recording outline.
- **No banner or Live Activity was on screen during the check**, so we don't yet know which process owns those windows on macOS 27, their window layer, or where they sit relative to the notch. Phase 0 finds out.
- **Banners may not overlap the notch at all on some screens.** On this 1800pt-wide display, banners sit at the top right while the panel spans at most 528pt around the centre (about x 636–1164). Phase 0 confirms where the real overlaps are (alerts, stacked banners, Notification Center, Live Activities, smaller displays, scaled resolutions) before we build behaviour around guesses.
- **The notification database exists and is SQLite**: `~/Library/Group Containers/group.com.apple.usernoted/db2/db`, with tables `app`, `categories`, `dbinfo`, `delivered`, `displayed`, `record`, `requests`. It was readable from Terminal, but Terminal probably already has Full Disk Access; the notch app would likely need Full Disk Access too (or at least trigger the "access another app's data" prompt the macshot tab can). The format is undocumented and has changed between macOS versions.
- **Live Activities have no API for third-party apps to read.** The only way to copy one is to capture its pixels with ScreenCaptureKit, which needs Screen Recording permission and constant capture.

## Phase 0: find out what's actually on screen

Goal: replace the guesses above with facts, before writing any app code.

1. Write a small Swift script, `tools/overlay-watch.swift`. It polls `CGWindowListCopyWindowInfo(.optionOnScreenOnly)` every 100ms and prints each window that appears or disappears near the top of the screen, with its owner, PID, layer and bounds. It also records how long each call to `CGWindowListCopyWindowInfo` takes.
2. With it running, trigger each of these on purpose:
   - a banner (`osascript -e 'display notification "test" with title "Shiny"'`);
   - a persistent alert (an app whose notifications are set to Alerts);
   - several banners at once, so they stack;
   - Notification Center opened from the clock;
   - a Live Activity from the paired iPhone (a timer, a ride or a sports score);
   - the volume and brightness HUDs, for comparison with ours.
3. Repeat on the built-in display at its default and "More Space" scaling, and on an external display with no notch (where the notch floats as an island).
4. Write down, for each kind: owner process, layer, bounds, how long it stays, and whether it intersects the peek, the HUD or the full panel (528 × up to 480pt, centred on the camera).

**Exit criteria:** a table of overlay kinds with the owner and layer to match on, which ones can collide with the notch, and the cost of one window-list call. If nothing ever collides, Phase 1 shrinks to the Live Activity case or is dropped.

## Phase 1: get out of the way (no permission)

### Detection: `SystemOverlayService`

A new service in `Services/SystemOverlayService.cs`:

- `IReadOnlyList<Rect> Overlays`: the frames of system windows that match the Phase 0 table (owner and layer), in the notch window's screen coordinates.
- `event EventHandler? Changed`: raised only when that list changes.
- `bool Covers(Rect notchFrame)`: whether any overlay intersects a given rect.

Move the `CGWindowListCopyWindowInfo` interop out of `MacShotService` into `Native/Interop.cs` as a small `WindowList.Snapshot()` helper that returns (owner, PID, layer, bounds) records, so both services share one implementation.

**Polling, with the app's low-power rule in mind.** There is no public event for "a banner appeared", so without Accessibility permission this has to poll. Keep it cheap and narrow:

- Poll only while the notch is wider than the camera, which means while the peek has wings, a HUD is showing or the panel is open. The empty notch hides in the camera and can't cover anything, so it doesn't poll at all.
- Poll every 500ms. A banner slides in over about 300ms, so the notch reacts within about half a second.
- The cost of one call comes from Phase 0. The budget is under 0.1% CPU for polling; if that's exceeded, drop to 1s.
- If the user grants Accessibility for Phase 2, switch from polling to an `AXObserver` on the overlay owner's process (window created/destroyed) and stop polling entirely.

### Behaviour: `NotchController`

Add a `yielding` flag, set while `SystemOverlayService.Covers(...)` is true for what's on screen:

| Notch state | While an overlay covers it |
|---|---|
| Peek with wings | Fold into the camera: render as if there's nothing to show. The rebuild key includes `yielding`. |
| HUD | Don't show it; the system HUD or banner wins. If one is already showing, end it early. |
| Panel, not hovered and not pinned | Fold back to the peek, which then folds into the camera as above. |
| Panel, hovered or pinned | Stay open. The user is using it, and moving it out from under the pointer is worse than the overlap. |
| Opening on hover | Don't open while an overlay covers the panel's full frame. Open as soon as it's gone if the pointer is still there. |

When the overlay goes, clear `yielding` and re-render. The existing rebuild-key check means nothing redraws unless the state really changed.

Use the panel's *maximum* frame for the hover-open check and the *current* notch frame for everything else, so a banner beside a small peek doesn't block the panel from opening if the panel wouldn't reach it.

### Setting

Settings ▸ Notch: **Get out of the way of notifications and Live Activities**, on by default. Turned off, the service stops polling.

### Tests

The controller has no unit tests today. Put the decision logic in a pure function, `YieldDecision.For(mode, hovered, pinned, notchFrame, panelFrame, overlays)`, so it can be tested without AppKit. Test it against each row of the table above. Check the real behaviour by hand with the Phase 0 triggers, and measure CPU with the overlay setting on and off, as was done for the music bars.

## Phase 2: mirror notifications in the notch (opt-in, Accessibility)

### Source: Accessibility, not the database

| | Accessibility (`AXObserver`) | Notification database |
|---|---|---|
| Permission | Accessibility | Full Disk Access (likely) |
| Timing | Real time, as the banner appears | Polling, with some lag |
| Content | What the banner shows: app, title, body | Everything delivered, including notifications that never showed a banner |
| Stability | Banner layout can change between macOS versions | Undocumented schema and blob format that has changed before |
| Click to open | Can press the real banner (`AXPress`) | Has to guess how to open the source app |

Use Accessibility. It's real time, needs a narrower permission than Full Disk Access, and lets a click in the notch do exactly what clicking the banner does. Revisit the database only if Accessibility turns out to miss content.

### Pieces

- **`NotificationMirrorService`:** registers an `AXObserver` on the owner process from the Phase 0 table, for window-created and destroyed notifications. On a new banner window, it walks the window's accessibility tree for the app name, title, body and icon. It raises `Received(MirroredNotification)`.
- **A new `Notice` mode in `NotchController`**, alongside Peek, Panel and HUD. It grows out of the notch for a few seconds, like the HUD. It shows the app icon, title and one line of body, and goes back to the peek on its own. Clicking it presses the real banner through Accessibility, which opens the source app. Hovering it holds it open.
- **Optional, later:** a "Recent" list of the last 10 mirrored notifications in a tab, kept in memory only. Never written to disk.
- **Settings ▸ Notifications:**
  - "Show notifications in the notch", off by default. Turning it on explains the Accessibility permission and opens System Settings ▸ Privacy & Security ▸ Accessibility.
  - A note that macOS still shows its own banner. To see notifications only in the notch, set an app's alert style to None in System Settings ▸ Notifications. That keeps them in Notification Center but stops the banner. Then confirm whether the Accessibility route still sees anything in that case; if it doesn't, this is where the database route becomes worth it.

### Privacy

Notification text is personal. Keep it in memory only, never log it (the HUD and peek logging style must not leak into this), and drop the recent list when the setting is turned off.

## Phase 3: Live Activities

- **Default: yield only.** Phase 1 already keeps the notch out of their way once Phase 0 tells us their owner and frame.
- **Mirroring: not planned.** It would need Screen Recording permission and continuous ScreenCaptureKit capture of the Live Activity's window, which works against the app's low-power design and puts a screen-recording indicator in the menu bar. Revisit only if Apple adds an API, or if users ask for it and accept the cost.

## Order and size

| Phase | Depends on | Size | Ships on its own |
|---|---|---|---|
| 0: find out | — | Half a day | No, it's research |
| 1: get out of the way | 0 | 1–2 days | Yes, the main fix |
| 2: mirror notifications | 0, and 1 for the shared observer | 3–4 days | Yes, behind a setting |
| 3: Live Activities | 0 | — | Yield comes with phase 1; mirroring isn't planned |

## Open questions

1. On macOS 27, does a Live Activity from the iPhone sit next to the camera, where it can collide with the notch, or among the menu-bar status items? Phase 0 answers this.
2. Does the notch window's level put it above or below banners? If banners are already drawn above the notch, Phase 1 is about not obscuring the area around the banner, not the banner itself.
3. When an app's alert style is None, does Accessibility see anything at all? If not, mirroring those notifications needs the database.
4. Should a hovered or pinned panel ever yield? This plan says no. Revisit once it's been tried.

## Docs to update when this ships

- README: a "Notifications and Live Activities" feature section, the Accessibility row in the Permissions table, and a Known limits note that macOS still shows its own banner.
- `Entitlements.plist` needs nothing new: Accessibility is a TCC permission, not an entitlement, and the app isn't sandboxed.
