# Sutra Island for Windows

**Sutra Island** is a live desktop island for Windows — a polished, always-on-top pill that
sits at the top-center of your screen and expands on hover into a wide, restrained
smoked-charcoal panel showing **now-playing media, system volume, an audio
equalizer, the clock, date, weather, RAM/network, battery, and a timer/alarm**.

Built in **C# + WPF on .NET 10**, rewritten from an original Python/Tkinter
prototype. The complete evolution of Python prototypes that led here is preserved
in [`python-versions/`](python-versions/).

The native build also includes **Q**, an on-demand visual assistant that reads the
active window, uses OCR and optional vision input, and streams answers directly
inside the Island. See [`Q.md`](Q.md) for setup, provider configuration, and the
privacy model.

## What's new in v1.0.15

- **Capture updated notifications:** refresh the Windows snapshot on notification events, serialize overlapping polls, and detect changed message content even when Windows reuses a notification ID.

### v1.0.14

- **More reliable notification delivery:** capture Windows notification-added events immediately, with snapshot polling as a recovery path.
- **Clear delayed banners:** a single notification keeps its app, title, and body after a delay; a summary is shown only when multiple notifications are waiting.

### v1.0.13

- **Music visualizer reliability:** animated fallback bars keep moving when loopback audio is too quiet for the spectrum analyzer, and real-spectrum response returns when a usable signal is available.

### v1.0.12

- **Settings redesign:** sidebar navigation, searchable sections, an Overview
  page, Shortcuts & behavior, and a collapsible live preview with accurate proportions.
- **Q conversation UI:** message bubbles, a smaller header, one composer, polished
  dropdowns, and a subtle three-dot thinking animation.
- **Provider, model, and effort controls:** integrated in the composer. Labels
  distinguish OpenAI API-key billing from Codex subscription/account access.
- **Current models:** automatic discovery, manual refresh, and exact model-ID
  entry. Suggestions include GPT-6 and Claude Sonnet/Opus 5.5; access depends on
  your account. Codex effort options come from its live catalog, while API
  providers use model-specific effort rules.
- **Command palette:** run media, volume, timer, and Q commands. Enter
  `ask why is the sky blue` to capture context and send the question automatically.
- **Rapid media controls:** repeated volume and seek clicks accumulate, and
  playback commands queue.

See [release notes](RELEASE_NOTES.md) for the complete update.

The native build also detects a paired, connected **AirPods** or compatible Beats
device through Windows Bluetooth LE advertisements. The Island can show the model,
connection state, left/right/case battery, charging state, case-lid state, and
in-ear state without requiring a driver or a companion service.

<p align="center">
  <img src="media/current-expanded-island.png" alt="Current Sutra Island expanded media, audio, and system status surface" width="900">
</p>

<p align="center">
  <a href="../../releases/latest">
    <img src="https://img.shields.io/badge/Download-Sutra%20Island-2ea44f?style=for-the-badge&logo=windows&logoColor=white" alt="Download Sutra Island">
  </a>
  &nbsp;
  <a href="../../releases/latest"><img src="https://img.shields.io/badge/Releases-all%20versions-24292e?style=for-the-badge" alt="All releases"></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
</p>

<!-- Optional: add a live version badge once pushed by replacing OWNER/REPO:
     ![Latest release](https://img.shields.io/github/v/release/OWNER/REPO) -->


<p align="center">
  <b>No install, no build</b> — download the self-contained <code>.exe</code> and double-click. Nothing else required.
</p>

---

## The current implementation

<p align="center">
  <img src="media/current-timer-panel.png" alt="Current native WPF timer panel" width="900">
</p>

The current native WPF build keeps the Apple-style shell while adding a dedicated
timer/alarm surface, settings customization, live activities, and Q. The timer
panel above is a clean implementation capture rather than a design mockup.

The expanded island combines album art, track details, a progress timeline, media
transport controls, volume, and clock/system cards. Enabled widgets can add weather,
AirPods, connectivity, and other live activities. Settings control what appears.

Rewind and fast-forward use the configured seek amounts and clamp to the start/end
of the track. Controls disable when the active media app cannot seek. Repeated taps
build on the latest accepted seek target instead of a stale timeline position.

---

## Screenshots

| Expanded Island | Compact Island | Settings surface | Timer surface |
| --- | --- | --- | --- |
| <img src="media/current-expanded-island.png" alt="Expanded Sutra Island" width="220"> | <img src="media/current-island-preview.png" alt="Compact Sutra Island" width="220"> | <img src="media/current-settings-preview.png" alt="Sutra Island settings window" width="220"> | <img src="media/current-timer-panel.png" alt="Timer panel" width="220"> |

These captures show earlier native WPF layouts; v1.0.12 redesigns Settings and Q
as described above. The clean crops
are used here intentionally; raw desktop captures in `test-artifacts/` may include
the contents of the active test window and should not be treated as public promo
assets. The timer comparison capture is retained in `media/current-timer-comparison.png`
for QA reference. The older animation and design renders remain in `media/` as historical
references under `media/legacy/` and are no longer used as the README hero.

---

## Core upgrade preview

This branch adds activity pinning, queued notifications, adaptive overflow, integration status, and independent timers/alarms. See [CORE_UPGRADES.md](CORE_UPGRADES.md) for the isolated preview launcher, validation, and rollback instructions.

## Features

- **Compact ↔ expanded** — a rounded pill that animates smoothly to a wide glass
  panel on hover (120–220 ms, no bounce) and collapses again after a short delay.
- **Media** — title/artist, artwork, explicit & favorite indicators, a gold
  progress bar with elapsed/remaining time, and **rewind-10 / previous /
  play-pause / next / forward-10** controls via the Windows
  `GlobalSystemMediaTransportControls` (GSMTC) API. When several apps have media
  sessions, the most relevant one is scored and chosen — never blindly the first.
- **Seek** — 10-second backward/forward seeking with clamping at 0 and track end;
  disables gracefully when the provider doesn't support position changes.
- **Audio** — master volume, mute, output-device readout, and a live
  WASAPI-loopback **equalizer** (with a tasteful idle animation when nothing is
  playing), via CoreAudio interop.
- **System monitor** — live **RAM** usage bar and a **network** throughput
  sparkline.
- **Clock, date & weather** — 12/24-hour time on a one-second tick, long date, and
  a current-conditions weather card.
- **Battery & charging** — percentage plus a dedicated charging capsule; degrades
  cleanly on desktops with no battery.
- **AirPods** - native BLE advertisement detection for paired, connected AirPods
  and compatible Beats devices, with rotating-address tolerance, model detection,
  left/right/case battery, charging flags, case status, and in-ear status. Public
  advertisements expose battery values in 10% steps, so the UI displays honest
  ranges such as 90-99% instead of implying an exact value.
- **Timer & alarm** — presets and custom durations, a *done* state with sound,
  snooze/dismiss, and state that survives a restart.
- **Q visual assistant** — enable multiple shortcuts: `Ctrl+Alt+Q`, `Shift+A`,
  `Shift+comma`, `Shift+period`, or lowercase `var` typed within two seconds.
  Capture the active window or
  monitor, extract OCR text, optionally send the captured PNG to a vision-capable
  provider, and stream an answer in the Island. Q supports Ask and Say modes,
  typed follow-ups, Windows dictation, retry/copy/recapture/new-question actions,
  automatic provider model discovery, model refresh, reasoning-effort selection,
  and configurable one-click shortcuts. Provider, model, and effort controls live
  together in the composer; Ask/Say is no longer a composer toggle.
- **ChatGPT / Codex for Q** - sign in through the official Codex device-code flow
  and use eligible Codex subscription limits without an OpenAI API key. The
  API-key providers remain available separately. Provider labels show whether
  Codex is using subscription access or API-key authentication.
- **Command palette** — run island commands, search actions, and submit Q prompts
  with `ask <question>`. Configure its shortcut in Settings → Shortcuts & behavior.
- **Settings** — sidebar navigation, section search, Overview, and a collapsible
  live preview with explicit Compact/Expanded selection.
- **Quick actions** — timer, settings, collapse, and a menu, as
  one row of equal circular buttons.
- **System integration** — per-monitor-V2 DPI aware, multi-monitor aware,
  single-instance, hidden from Alt-Tab, optional launch-on-startup, optional
  click-through when compact, reduced-motion support, full light/dark theming, and
  a tray menu.
- **Privacy** — no analytics or Sutra Island account required. Enabled online
  widgets and Q providers can make network requests. Q can query model catalogs
  when opened, when its provider changes, or when refreshed; Codex sign-in also
  queries account status. Q credentials use Windows user-scoped
  DPAPI storage and captured context stays in memory for the current session.

---

## Design language

Restrained premium Windows-style **smoked charcoal**, not flashy glassmorphism:
dark graphite surfaces, low-opacity translucency, a faint backdrop, one thin
gray-blue outline, and soft shadows. Color is used sparingly — **muted gold** only
for the media progress, and **muted blue** only for live system visualizations
(equalizer, RAM fill, network sparkline). Iconography is **Segoe Fluent Icons**
throughout; type is **Segoe UI Variable**.

---

## Get it

### Option 1 — Download the app (easiest)

1. Go to the **[latest release](../../releases/latest)** and download the app executable
   (or use the green button above).
2. Double-click it. The build is **self-contained** — no .NET, no installer,
   nothing else to set up.

To enable Q, open **Settings → Q Assistant**, choose a provider and model, add the
provider key if needed, choose whether to capture the active window or monitor,
and use **Test connection**. The first Q session also presents a disclosure before
screen context is sent to a provider. Local Ollama setups can use the configurable
base URL without an API key.

For ChatGPT/Codex testing, the release also includes
the Codex test ZIP. Extract the complete folder and
launch its app executable; the ZIP includes a pinned, SHA-256-verified official
Codex runtime. The standalone executable remains available separately and
uses an existing supported official Codex installation. Signing out of Codex in Sutra Island also
signs out official Codex apps for that Windows user. See [`Q.md`](Q.md) for the
subscription limits, privacy model, and security boundaries.

The island appears at the top-center of your primary monitor and lives in the
system tray (right-click for Settings, Recenter, Quit). Windows SmartScreen may
warn about an unsigned app the first time — choose *More info → Run anyway*.

### AirPods support

AirPods support is automatic: pair the device in Windows, connect it, and leave
Bluetooth enabled. The app listens passively for Apple manufacturer data (0x004C)
and only accepts advertisements when exactly one matching AirPods/Beats device is
reported as connected by Windows. Rotating BLE addresses are tolerated; the app does
not write pairing records or connect to the earbuds.

Battery values from the public advertisement are coarse 10% buckets. A displayed
90-99% means the device reported the 90% bucket, not that the app measured an exact
90% charge. A missing component battery is omitted rather than shown as 0%.

ANC, transparency, gestures, and other controls are intentionally not exposed:
Windows does not provide a safe public API for controlling those AirPods features
from this driver-free integration. Availability also depends on a Bluetooth adapter
that supports LE advertisements and on Windows exposing the paired device as
connected.

> The `.exe` bundles the .NET runtime and exceeds GitHub's
> 100 MB per-file limit, so it's distributed as a **release asset** rather than
> committed to the repo.

### Option 2 — Build from source

Requirements: **Windows 10 1809+ (Windows 11 recommended)** and the **.NET 10
SDK**. From the repository root:

```powershell
dotnet build .\DynamicIsland.slnx -c Debug
dotnet run --project .\DynamicIsland.Windows
```

Run the tests:

```powershell
dotnet test .\DynamicIsland.Windows.Tests
```

Publish a self-contained single-file executable (no .NET needed on the target):

```powershell
dotnet publish .\DynamicIsland.Windows -c Release -r win-x64
```

> This project was developed with a workspace-local .NET SDK. If a global
> `dotnet` reports "No .NET SDKs were found", install the .NET 10 SDK (or use the
> local toolchain that shipped during development).

### Publishing a release (maintainers)

The release workflow runs tests and publishes the standalone app, Codex test
bundle, and `SHA256SUMS.txt`. To prepare a release, update the project version,
README, Q setup notes, and `RELEASE_NOTES.md`, then validate the build:

```powershell
dotnet publish .\DynamicIsland.Windows -c Release -r win-x64
```

Push a matching `vMAJOR.MINOR.PATCH` tag to trigger `.github/workflows/release.yml`.
The workflow rejects a tag that differs from the project version. Bundled runtime
files must match their pinned SHA-256 hashes. The download button points to the
latest release page; earlier releases remain accessible from the release history.

---

## Repository layout

```
DynamicIsland/
├── DynamicIsland.Windows/         Main app — the Windows version (C# + WPF, .NET 10)
│   ├── Models/                    Plain state records (settings, media, audio, battery, …)
│   ├── Services/                  One responsibility each (media, audio, monitor, weather, …)
│   ├── Interop/                   Isolated P/Invoke + CoreAudio COM interfaces
│   ├── ViewModels/                MVVM view models
│   ├── Views/                     IslandWindow, SettingsWindow, TimerAlarmWindow (XAML)
│   └── Infrastructure/            ObservableObject, RelayCommand, SeekMath, …
├── DynamicIsland.Windows.Tests/   xUnit tests (e.g. 10s-seek clamping)
├── DynamicIsland.slnx             Solution
├── DynamicIsland.Q.Core/           Provider contracts, prompt composition, and streaming session state
├── Q.md                            Q assistant setup, controls, providers, and privacy notes
├── MIGRATION_REPORT.md            Python → C# audit, feature mapping, test results
├── design-qa.md                   Visual QA notes and known capture limitations
├── media/                         Current screenshots used by this README (`legacy/` holds old renders)
├── test-artifacts/                Local implementation/QA captures; review for private content before sharing
└── python-versions/               Archived Python/Tkinter prototypes (not built)
```

All Windows interop is isolated under `Interop/`; P/Invoke is not scattered through
the UI. See [`MIGRATION_REPORT.md`](MIGRATION_REPORT.md) for the full feature audit
and what was preserved vs. improved. See [`Q.md`](Q.md) for the native assistant
workflow and [`design-qa.md`](design-qa.md) for visual validation notes.

---

## The Python prototypes

Before the native rewrite, the island went through many Python/Tkinter
explorations — different shapes, dock positions, an animated progress ring, a
and premium builds. They're all kept, with a
guide to each, in **[`python-versions/`](python-versions/)**.

---

## License

Released under the **MIT License** — see [`LICENSE`](LICENSE). (Swap in a different
license if you prefer.)
