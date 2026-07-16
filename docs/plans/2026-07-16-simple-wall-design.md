# simple-wall — design

**Date:** 2026-07-16
**Status:** validated, not yet implemented

## Purpose

Play looping mp4 clips on a strip LED wall, triggered by mouse, by a Stream Deck over the network, or by a built-in scheduler. A deliberately minimal alternative to Resolume for a setup that only ever needs one clip on screen at a time.

The wall is driven as a region of a normal Windows monitor: the monitor reports 1920x1080, but the LED only reproduces a strip across the top, and the clips are sized to that strip (e.g. 1964x256). Neither the monitor resolution nor the clip dimensions are hardcoded — the output is positioned and sized freely.

**Verified on the real machine 2026-07-16** (this corrects the original brief): the LED is an **extended second display at X=1920**, not a mirror of the primary's top strip. Working geometry is **X=1920, Y=0, W=1964, H=256** — width 1964, *wider than the 1920 panel*, deliberately: the clip is 1964 wide, so at W=1920 VLC downscales it and the wall looks soft, while at 1964 the overhang is cropped and the visible area is pixel-for-pixel 1:1 and visibly sharper.

Two consequences for the code: the output defaults onto the non-primary display's bounds rather than 0,0, and output width is **never clamped** to the screen — wider-than-screen is a legitimate setting. (Also: the LED enumerates as `DISPLAY1` while the primary is `DISPLAY2`. Device names are not an ordering; read `Bounds` and `Primary`.)

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

- **Control window** — main monitor. Clip grid, transport, sliders, scheduler, settings. An **ordinary window: NOT always-on-top** — the operator needs to browse Explorer for clips without it fighting them. (The spike made this window top-most, but only to work around its own wrong geometry default putting the output window on top of the desktop. With the output on the LED display at X=1920 the two never overlap, and the hack must not survive into the product.)
- **Output window** — borderless, no title bar, **always-on-top**. This one genuinely needs it: it must sit above everything else on the wall. Positioned on the LED strip and sized to it once; geometry persists.
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

Each box shows a **thumbnail**, the filename, and its **stable slot number** in the corner.

The thumbnail is the point, not decoration — with clips named like `INNONATION_WALL_3.mp4` and `WALL_BEFORE_SUNSET_1964X256.mp4`, filenames alone don't tell you what's on screen. It's the first frame, extracted once via VLC's snapshot and cached to disk (keyed by path + last-write-time, so replacing a file re-thumbnails it). Extraction is async and never blocks startup: a wall that takes ten seconds to open because it's decoding fifty first-frames is a worse wall.

**Stable slot numbers**: a box keeps its number for life. Removing a box does not renumber the others; a new box takes the lowest free number. The number on the box is the OSC address, so a Stream Deck mapping never silently drifts when the grid is edited.

**Config repair on load.** A config file could hold duplicate slot numbers or slots outside 1–50 — hand-edited, or written by a future bug. That state is already ambiguous (`/clip/7` has no defined meaning if two clips claim slot 7), and worse, it creates a path to exactly the drift stable numbering exists to prevent: removing one of the duplicates frees the number, and a later unrelated add silently reuses it, so a Stream Deck button ends up triggering a clip nobody mapped to it.

So the clip library normalizes when it loads: walking in file order, the first entry claiming a slot keeps it, and any duplicate or out-of-range entry is reassigned to the lowest free slot. Nothing is dropped (losing a clip silently is worse than renumbering one), except entries beyond the 50th — there are only 50 slots. A healthy config is never touched. When a repair happens the app says so and saves the corrected file, rather than fixing it silently on every load.

Boxes hold absolute paths, not copies of the media.

### Triggering

Single-click plays that clip immediately, looping. The playing box gets a bright border. Clicking the already-playing box does nothing — no restart, no stop — so a stray double-click can't glitch the wall. Stopping is a separate deliberate action.

**Two layers, hard cut** — decided by measurement on the real wall (2026-07-16), reversing this design's original guess.

The first draft loaded each clip on trigger and accepted "a brief black frame". Measured on the actual LED panel, that black is **~290ms** (`GAP A->B: 112 ms`, `FIRST PICTURE: 286 ms`) — plainly visible, and read as "half a second" by the operator, because black on a bright wall feels longer than it measures. Not acceptable on a live wall.

That time is inherent to opening the file and bringing up a decoder; VLC won't do it much faster. But the wall only goes black because the outgoing clip is torn down the instant the new one is triggered. So the output window holds **two stacked layers**. On trigger, the new clip loads into the hidden layer, and the swap happens only once it actually has a picture — the outgoing clip stays on the wall until then. The wall never goes black.

The cost is ~290ms between trigger and change, which is a far better trade than a dropout. It is still **one clip at a time**: no mixer, no crossfade slider, no extra controls, and no change to the OSC contract. Layers are an implementation detail here, not a feature.

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

- **Crossfades / a mixer** — the wall now hard-cuts with no black (see above), which is what was actually needed. Blending two clips is a different feature; VLC doesn't do it natively, and nobody asked for it.
- **Saturation, gamma, hue** — free from VLC's filter, still cut.
- **Audio** — it's an LED wall.
- **Clip trimming, speed, playback range, effects, BPM sync** — Resolume's actual feature set. Not this.
- **Cron expressions** — daily / weekly / one-off covers the real use; cron syntax in a text box is power nobody asked for.
- **Scheduled playlists / sequences** — a task fires one command. Chaining clips over an evening is a different feature; get one-shot tasks right first.
- **Catch-up on missed tasks** — see the scheduler section. Deliberate, with a known mitigation if it bites.
