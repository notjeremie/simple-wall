# simple-wall — design

**Date:** 2026-07-16
**Status:** validated, not yet implemented

## Purpose

Play looping mp4 clips on a strip LED wall, triggered by mouse, by a Stream Deck over the network, or by a built-in scheduler. A deliberately minimal alternative to Resolume for a setup that only ever needs one clip on screen at a time.

The wall is driven as a region of a normal Windows monitor: the monitor reports 1920x1080, but the LED only reproduces a strip across the top, and the clips are sized to that strip (e.g. 1964x256). Neither the monitor resolution nor the clip dimensions are hardcoded — the output is positioned and sized freely.

## Constraints

- Runs on Windows 7 Home Premium SP1 64-bit and every later Windows.
- Must survive a power cycle with its layout, output geometry, image settings and schedule intact.
- Runs unattended: an optional autostart brings it back after a reboot, and the scheduler drives the wall with nobody present.
- Never take the wall down because of a bad file, a missing monitor, or a corrupt config.

## Stack

C# on **.NET Framework 4.8** with **LibVLCSharp** (VLC 3.x) for playback.

- .NET Framework 4.8 installs on Win7 SP1 and ships in-box on Win10/11 — one build covers the whole target range.
- VLC 3.x officially supports Win7 SP1, decodes arbitrary mp4, loops natively, and provides brightness/contrast as a built-in adjust filter applied to decoded frames.
- Distributed as a folder: EXE plus VLC DLLs. No installer.

Rejected: Electron (dropped Win7 in v23), Python + PyQt5 (pins the toolchain to Python 3.8 forever), libmpv (dropped Win7 support in current releases).

## Architecture

Two windows, one engine.

- **Control window** — main monitor. Clip grid, transport, sliders, scheduler, settings.
- **Output window** — borderless, no title bar, always-on-top. Dragged onto the LED strip and sized to it once; geometry persists.
- **Engine** — a single VLC media player instance attached to the output window's handle. Video decodes and draws directly there with hardware acceleration; the control window never touches video pixels.

There are **three input sources — mouse, OSC and scheduler — and one command path** into the engine. A scheduled "play clip 7" and a clicked box are the same call. The UI reflects engine state rather than its own, so the grid stays correct whether the Stream Deck or the clock is what changed things.

### Config

One JSON file next to the EXE, saved on change, loaded on start:

- clip list (stable slot number + absolute path per box)
- output window geometry (x, y, width, height)
- brightness, contrast
- OSC listen port, optional reply host/port
- scheduled tasks, and the scheduler master-enable state
- autostart preference

## Clip grid

Dynamic, not a fixed field of empties. Starts empty with a trailing **+** tile.

- Add: click **+**, or drag mp4s onto the window. Dropping N files creates N boxes.
- Remove: right-click → Remove. Boxes reflow to close the gap.
- Rearrange: drag a box onto another.
- Ceiling of 50 boxes; the **+** tile disappears at the limit.
- Grid reflows to window width — boxes are a fixed size, so a wider window fits more per row.

Each box shows a thumbnail (first frame, extracted once and cached to disk), the filename, and its **stable slot number** in the corner.

**Stable slot numbers**: a box keeps its number for life. Removing a box does not renumber the others; a new box takes the lowest free number. The number on the box is the OSC address, so a Stream Deck mapping never silently drifts when the grid is edited.

Boxes hold absolute paths, not copies of the media.

### Triggering

Single-click plays that clip immediately, looping. The playing box gets a bright border. Clicking the already-playing box does nothing — no restart, no stop — so a stray double-click can't glitch the wall. Stopping is a separate deliberate action.

Clips load on trigger rather than being preloaded. A local mp4 opens in well under 100ms, so switching feels instant, but there is a brief black frame between clips. This is the accepted cost of cutting layers and crossfades. If it proves visible on the wall, the fix is the two-layer mixer deliberately scoped out (see Out of scope).

## Transport and image adjustment

- **Play/Pause** — one toggle button, icon swaps. Pause freezes on the current frame; the wall keeps showing it and does not go black.
- **Stop** — output to black, box un-highlighted. The end-of-night panic button.

Brightness and contrast drive VLC's adjust filter on decoded frames — no re-encode, no lag, immediate. Both range 0–2, default 1.0, each showing its numeric value with a **Reset** that snaps back to 1.0.

Both are **global**, not per-clip: they belong to the wall and the room, calibrated once. Both persist and reapply on startup.

Only brightness and contrast are exposed, though VLC's filter also offers saturation, gamma and hue. Every extra slider is another thing to knock out of place mid-show.

## OSC

Listens on UDP, default port **7000**, editable in settings.

| Address | Argument | Action |
|---|---|---|
| `/clip/N` | none, or `1` | Trigger the clip in slot N |
| `/play` | none, or `1` | Play / resume |
| `/pause` | none, or `1` | Pause on current frame |
| `/toggle` | none, or `1` | Play/pause toggle |
| `/stop` | none, or `1` | Stop, output to black |
| `/brightness` | float 0–2 | Set brightness |
| `/contrast` | float 0–2 | Set contrast |

`N` is the stable slot number shown on the box.

The optional `1` argument accommodates Stream Deck OSC plugins that send a button value; a bare message works too. A `0` (button release) is ignored, preventing double-triggers.

The listener runs on its own thread and marshals commands onto the UI thread, so OSC and mouse converge on the same code path.

**Reply (optional):** with a reply host and port configured, the app pushes state — current slot, play state, brightness, contrast — on every change, so the Stream Deck lights the right button when a clip is triggered by mouse. Blank reply host = silent.

Settings displays the machine's IP addresses and the listening port as plain text, so Stream Deck setup means reading the screen, not running `ipconfig`.

Windows will prompt to allow the app through the firewall on first listen. It must be accepted or no OSC arrives.

## Scheduler

A tab in the control window: a list of tasks with Add / Edit / Remove, and a per-task checkbox to disable one without deleting it.

Each task is **when** + **what**.

**When** — a time (HH:MM), plus either:
- a set of weekdays (tick Mon–Sun; all ticked means daily), or
- a single calendar date, for a one-off. One-offs show as spent once fired.

**What** — one command from the same set OSC exposes: play clip *N*, play, pause, stop, set brightness, set contrast. The editor adapts to the choice: *play clip* offers a dropdown of the actual boxes by slot number and filename; *brightness* / *contrast* offer a value field.

Each task renders as a sentence — *"Every Sun at 13:00 → play clip 7 (intro.mp4)"* — so a mistake is visible at a glance instead of decoded from columns.

**Master enable** switch above the list. Off means nothing fires — protection while driving the wall by hand. State persists, and the tab shows it loudly when off; a silently disabled scheduler is a Sunday-afternoon discovery.

**Implementation:** a one-second timer checks for tasks due since the last tick and fires them through the shared command path. Every firing is logged with a timestamp, so "did it run on Sunday?" has an answer.

### Missed tasks: no catch-up

Only tasks whose time arrives while the app is running fire. Boot at 13:20 and the 13:00 task does not run — the wall stays black until the next task, or until someone triggers a clip by hand.

Chosen deliberately: nothing unexpected ever appears on the wall. The mitigation for a late boot is autostart (below). If a late boot leaving the wall dark proves to be a problem in practice, the change is a configurable grace window — fire the most recent missed task if it was due within the last N hours.

### Autostart

Optional checkbox in settings: register the app to launch at Windows login (per-user registry `Run` key — no admin rights, no service, unticking removes it cleanly). It restores the grid, output geometry and image settings, then waits for the scheduler.

This is what makes no-catch-up safe. Without autostart, a 03:00 reboot means a black wall until someone notices; with it, the app is back before the next task is due.

## Failure handling

Fail visibly before showtime, never mid-show.

| Failure | Behaviour |
|---|---|
| Clip file missing at startup | Box turns red; clicking does nothing; wall keeps showing current clip |
| Clip unplayable / bad codec | Box turns red; previous clip keeps playing |
| Saved output geometry fits no connected monitor | Snap window back onto the main screen. **Reset output window** button in settings as backstop |
| Config JSON corrupt | Renamed to `.bad`, app starts fresh. Lose the layout, not the evening |
| OSC port in use | Message in settings ("port 7000 in use, OSC disabled"). App runs fine without OSC |
| Scheduled task targets a removed or missing clip | Skipped and logged; the task shows red in the list. Whatever is playing keeps playing |
| System clock jumps (DST, time sync) | The tick fires tasks due since the last tick, so a backward jump can't replay a task and a forward jump fires what was skipped over, once |
| Unhandled exception | Logged with timestamp to a file next to the EXE |

## Testing

- **Engine logic** (slot numbering across add/remove, config load/save, corrupt-config recovery, geometry validation against monitor layout, OSC message parsing including the `0`-argument case) is unit-testable and should be, since these are exactly the paths that fail quietly.
- **Scheduler due-calculation** is the highest-value thing to unit test, because its bugs surface once a week at the worst moment and can't be reproduced on demand. The tick takes the current time as a parameter rather than reading the clock, so "is this due?" is a pure function: weekday matching, one-off dates, spent one-offs, the no-catch-up rule, disabled tasks, master enable, and clock jumps in both directions are all testable in milliseconds without waiting for Sunday.
- **Playback and the adjust filter** need the real VLC engine and a real second display; verified by hand on the target machine.
- **OSC** is verifiable from another machine with any OSC sender before the Stream Deck is involved.
- Final verification runs on the actual Win7 SP1 box driving the actual wall. Win7 is the risk surface, not Win11.

## Out of scope

Deliberately cut, listed so the reasoning survives:

- **Layers / crossfades / mixer** — the reason clip switching shows a black frame. First thing to add if that flash proves visible.
- **Saturation, gamma, hue** — free from VLC's filter, still cut.
- **Audio** — it's an LED wall.
- **Clip trimming, speed, playback range, effects, BPM sync** — Resolume's actual feature set. Not this.
- **Cron expressions** — daily / weekly / one-off covers the real use; cron syntax in a text box is power nobody asked for.
- **Scheduled playlists / sequences** — a task fires one command. Chaining clips over an evening is a different feature; get one-shot tasks right first.
- **Catch-up on missed tasks** — see the scheduler section. Deliberate, with a known mitigation if it bites.
