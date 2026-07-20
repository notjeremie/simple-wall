# SimpleWall

Video wall playout for a **1664×256 three-face corner LED wall** in a television
newsroom. Sixteen clips on screen as tiles, one click or one Stream Deck button
to put any of them on the wall, a scheduler for the ones that should change
themselves, and no visible black frame when a clip changes.

Running in production at i24NEWS on a Windows 7 PC.

```
┌─────────┬───────────────────────────────────────────────┬────────┐
│  LEFT   │                 FRONT FACE                    │ RIGHT  │
│ RETURN  │            191–1440 · safe zone                │ RETURN │
│  191px  │                  1249px                        │ 224px  │
└─────────┴───────────────────────────────────────────────┴────────┘
   0     191                                            1440    1664
```

The wall is a single flat 1664×256 canvas that physically folds at two
90° creases. Content is authored against the full width, but anything that
must stay readable — a logo, a face — lives inside the front face.

## What it does

- **16 clip slots**, each a tile with a thumbnail. Triggered by mouse, Stream
  Deck (OSC over UDP), the scheduler, or the boot default.
- **Invisible clip changes.** Swaps measured at 165–471 ms on the hardware with
  no black frame — see [below](#the-swap).
- **Per-clip looks.** Brightness and contrast belong to the *clip*, not to the
  wall. Every trigger brings a clip up at its own saved look, applied to the
  incoming layer *before* the swap, so the outgoing frame never flashes wrong.
- **Scheduler.** Weekly recurrence on any subset of weekdays, or a one-off date.
  A scheduled event can play a clip, play/pause/stop, or set a look.
- **Replace a clip in place**, keeping its slot number — and therefore its
  Stream Deck mapping — while the file behind it changes.
- **Survives the newsroom.** Single-instance, autostart, atomic config writes
  with corrupt-file quarantine, a log that rolls at ~5 MB, and a clock-jump
  guard on the scheduler.

## The swap

The naive approach — stop the player, load the next file, start it — showed a
measured ~290 ms of black on this hardware. On a wall behind a live news set,
that reads as a fault.

So there are two `MediaPlayer`s driving two stacked `VideoView`s in one
borderless output window. The front layer keeps playing while the next clip
loads onto the hidden back layer. A 15 ms poll waits up to a second for the back
player to report an actual decoded picture, and only then does the z-order flip
and the outgoing player stop. The cut becomes invisible because nothing is ever
not-playing.

Core of it: [`VlcWallEngine.StartLoad`](src/SimpleWall/Engine/VlcWallEngine.cs)
and `CompleteSwap`, with the wait/give-up decision isolated in `SwapPolicy` so
it can be unit-tested without a video card.

## Remote control (OSC)

Listens on UDP **7000** by default. Built for a Stream Deck, but anything that
speaks OSC works.

| Address | Effect |
|---|---|
| `/clip/N` | Play slot N |
| `/play` `/pause` `/toggle` `/stop` | Transport |
| `/brightness <0..2>` | Set the current clip's brightness |
| `/contrast <0..2>` | Set the current clip's contrast |

A trigger arriving with a leading `0` argument is treated as a button *release*
and ignored, so a Stream Deck press doesn't fire twice.

State feedback is sent back on `/state/slot`, `/state/playing`,
`/state/brightness` and `/state/contrast` on every state change — so the faders
on the Stream Deck follow the wall, including when someone changes it by mouse.
Replies go to `OscReplyHost`/`OscReplyPort` (default 9000), disabled until a
host is set.

## Build

**Windows only.** .NET Framework 4.8 WinForms, x64, with native LibVLC binaries.
It does not build or run on macOS or Linux — the target machine is a Windows 7
PC, and that constraint shapes the whole project.

Needs .NET SDK 8.0.423 (pinned in `global.json`).

```sh
dotnet build          # from the repo root, builds the .sln
dotnet test           # 229 tests
```

Main dependencies: `LibVLCSharp` 3.8.2 + `VideoLAN.LibVLC.Windows` 3.0.21,
`Rug.Osc`, `Newtonsoft.Json`, xunit.

`packaging/build-release-package.sh` produces the deployable zip;
`packaging/install.bat` is what runs on the wall PC.

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
  Program.cs       composition root
tests/             229 tests, xunit
tools/RenderShot/  renders any WinForms Form to a PNG over SSH, headless
tools/calib/       LED wall calibration pattern generators
docs/plans/        design docs, STATUS.md is the real handoff
docs/RUNBOOK.md    for whoever is at the wall PC
```

Config lives next to the executable as `config.json` (falling back to
`%LOCALAPPDATA%\simple-wall\` then the Desktop — first writable location wins).
Writes are temp-file-then-atomic-replace; a corrupt config is quarantined to
`config.json.bad-<timestamp>` rather than silently overwritten.

## Testing without the hardware

The interesting problem in this project was that the target is a Windows 7 PC
reachable only by VNC, with no debugger and no fast feedback loop. Two tools
exist to close that gap:

- **`tools/RenderShot`** renders any WinForms form to a PNG over SSH with no
  desktop session, so UI states can be *looked at* from a build VM instead of
  reasoned about.
- **`LibVlcContractTests`** run real libvlc with `--vout=dummy`, which turns out
  to give genuine playback behaviour with no video card — so the player boundary
  is tested rather than mocked.

`tools/calib/` generates the LED calibration patterns: a fit-check/ruler sheet
for the graphics department, and a fold-verification pattern for re-checking
that the creases still land where they're documented.

## Status

Tagged [`v1.0`](../../releases/tag/v1.0). Verified on the real wall: 18h29m of
continuous uptime under production use, 5/5 scheduler events on time including
one firing after an 8-hour idle gap, zero errors, and no swap-latency drift.

`docs/plans/STATUS.md` is the honest running account — what's done, what's
verified on hardware versus only unit-tested, and what's still owed.
