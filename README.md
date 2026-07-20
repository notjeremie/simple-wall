# SimpleWall

Playout for video walls, LED strips and signage displays on Windows. Put clips
on a screen as tiles, click one — or hit a Stream Deck button — and it goes on
the wall with no visible black frame between clips.

Built for a newsroom LED wall that had to be driven by someone who is not a
video engineer, on a PC that could not be babied.

- **Up to 50 clip slots**, triggered by mouse, Stream Deck (OSC over UDP), a
  scheduler, or the boot default
- **Invisible clip changes** — measured 165–471 ms swaps with no black frame
- **Per-clip brightness/contrast**, remembered per clip rather than per wall
- **Scheduler** — weekly on any set of weekdays, or a one-off date
- **Unattended operation** — autostart, boot-into-a-default-clip, atomic config
  writes, log rolling, and a clock-jump guard

**Windows only** (.NET Framework 4.8 + LibVLC). Runs on Windows 7 and later.

→ **[Getting started](docs/GETTING-STARTED.md)** · **[Configuration](docs/CONFIGURATION.md)** · **[Operator runbook](docs/RUNBOOK.md)**

## Is this for you?

SimpleWall is deliberately small. It does one thing: reliably put one of N video
files on one of your displays, and let something external decide which.

It's a good fit if you have a display that isn't a desktop — an LED wall, a
lobby screen, a strip above a set, a shop window — and you want a handful of
loops that an operator switches between or that change on a schedule.

It is **not** a media server, a playlist system, or a compositor. No transitions,
no layering, no audio mixing, no network sync between machines. If you need
those, look at CasparCG or OBS.

## How it works

Your display is set up as an **extended** desktop (not mirrored). SimpleWall
puts a borderless window exactly over that display's region and plays video into
it. The main window — the tiles, the faders, the scheduler — stays on your
normal monitor.

```
   your monitor                    the wall (extended display)
┌──────────────────┐          ┌──────────────────────────────┐
│ ▣ ▣ ▣ ▣  tiles   │          │                              │
│ ▣ ▣ ▣ ▣          │  ──────▶ │      borderless output       │
│ ── ── faders     │          │                              │
└──────────────────┘          └──────────────────────────────┘
     control                      whatever you configured
```

Any resolution and position works — configure it once and it's remembered.

### The swap

The obvious approach — stop the player, load the next file, start it — showed a
measured ~290 ms of black on this hardware. On a wall behind a live news set,
that reads as a fault.

So there are two players driving two stacked video surfaces in one output
window. The front layer keeps playing while the next clip loads onto the hidden
back layer. A 15 ms poll waits up to a second for the back player to report an
actual decoded picture, and only then does the z-order flip and the outgoing
player stop. The cut is invisible because nothing is ever not-playing.

Core of it: [`VlcWallEngine.StartLoad`](src/SimpleWall/Engine/VlcWallEngine.cs)
and `CompleteSwap`, with the wait/give-up decision isolated in `SwapPolicy` so
it's unit-tested without a video card.

## Remote control (OSC)

Listens on UDP **7000** by default. Built for a Stream Deck, but anything that
speaks OSC works — Companion, TouchOSC, a Python script.

| Address | Effect |
|---|---|
| `/clip/N` | Play slot N |
| `/play` `/pause` `/toggle` `/stop` | Transport |
| `/brightness <0..2>` | Set the current clip's brightness |
| `/contrast <0..2>` | Set the current clip's contrast |

A trigger arriving with a leading `0` argument is treated as a button *release*
and ignored, so one Stream Deck press doesn't fire twice.

State is sent back on `/state/slot`, `/state/playing`, `/state/brightness` and
`/state/contrast` on every change — so your controller's faders follow the wall
even when someone changes it by mouse. See
[Configuration](docs/CONFIGURATION.md#osc) to enable replies.

## Install

Grab a [release](../../releases), unzip, run `install.bat`. It checks for
.NET Framework 4.8 and drops the app in place.

Or build it yourself — see [Getting started](docs/GETTING-STARTED.md).

## Build

**Windows only.** .NET Framework 4.8 WinForms, x64, with native LibVLC binaries.
It does not build or run on macOS or Linux. Needs .NET SDK 8.0.423 (pinned in
`global.json`).

```sh
dotnet build          # from the repo root
dotnet test           # 229 tests
```

Dependencies: `LibVLCSharp` 3.8.2 + `VideoLAN.LibVLC.Windows` 3.0.21,
`Rug.Osc`, `Newtonsoft.Json`, xunit.

## Layout

```
src/SimpleWall/
  Engine/          playout: IWallEngine, VlcWallEngine, WallCommand, SwapPolicy
  Model/           persisted state: WallConfig, ClipEntry, ClipLibrary, ConfigStore
  Osc/             OscListener (receive), OscParser (pure), OscReplySender (feedback)
  Scheduling/      Scheduler, ScheduledTask, TickGuard
  UI/              MainForm, OutputWindow, ClipBox, SchedulerTab, SettingsTab
  Logging/         Log, LogPaths
  Infrastructure/  Autostart (HKCU Run key)
tests/             229 tests, xunit
tools/RenderShot/  renders any WinForms Form to a PNG over SSH, headless
tools/calib/       LED wall calibration pattern generators
docs/plans/        design history — a record of how this was built, not a spec
```

## Testing without the hardware

The constraint that shaped this project: the target machine was reachable only
by VNC, with no debugger and a feedback loop measured in hours. Two tools exist
to close that gap, and they may be useful in your own projects:

- **`tools/RenderShot`** renders any WinForms form to a PNG over SSH with no
  desktop session, so UI states can be *looked at* from a build VM instead of
  reasoned about.
- **`LibVlcContractTests`** run real libvlc with `--vout=dummy`, which gives
  genuine playback behaviour with no video card — so the player boundary is
  tested rather than mocked.

**`tools/calib/`** generates LED calibration patterns — a fit-check ruler sheet
for confirming your output lands pixel-exact, and a fold-verification pattern for
walls that bend around a corner. See [`tools/calib/`](tools/calib/) if that's you.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Be warned that meaningful changes to the
playout path are hard to verify without a physical wall — the testing notes
above explain how far you can get without one.

## Status

Tagged [`v1.0`](../../releases/tag/v1.0) and running in production on a
newsroom LED wall. Verified on that hardware: 18h29m of continuous uptime under
real use, 5/5 scheduler events on time including one firing after an 8-hour idle
gap, zero errors, no swap-latency drift.

`docs/plans/STATUS.md` is the honest running account of what's done, what's been
verified on hardware versus only unit-tested, and what's still owed.

## License

MIT — see [LICENSE](LICENSE).
