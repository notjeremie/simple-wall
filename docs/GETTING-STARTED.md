# Getting started

From nothing to a clip on your wall. Assumes no prior knowledge of the project.

## 1. Set the display up as an extended desktop

This is the step people get wrong, and everything else depends on it.

Your wall or second screen must be an **extended** display, not a **mirrored**
one. Mirrored means Windows shows the same picture on both, so there is no
separate region for SimpleWall to draw into, and you'll get either nothing on
the wall or your desktop duplicated onto it.

In Windows display settings, choose **Extend these displays**. Then note two
things about the wall display:

- its **resolution** — e.g. 1664×256
- its **position** — where its top-left corner sits in the virtual desktop,
  e.g. X=1920, Y=0 if it's to the right of a 1920-wide primary monitor

You'll type both into SimpleWall in step 3. Windows display settings shows you
the arrangement; the position is the coordinate of the wall's top-left corner
relative to your primary monitor's top-left.

## 2. Install

**From a release:** download, unzip, run `install.bat`. It verifies .NET
Framework 4.8 is present and puts the app in place.

**From source:** on Windows with .NET SDK 8.0.423,

```sh
git clone https://github.com/notjeremie/simple-wall
cd simple-wall
dotnet build -c Release
```

The executable lands in `src/SimpleWall/bin/Release/net48/SimpleWall.exe`.

## 3. Point the output at the wall

Launch SimpleWall. The main window opens on your normal monitor.

Go to the **Settings** tab → **Output window** and fill in **X**, **Y**,
**Width** and **Height** from step 1.

A borderless black window should appear covering the wall exactly. If it lands
on your desktop instead, the X/Y are wrong. If it's the wrong size, the width
and height are. **Reset output window** puts it back if you lose it.

> SimpleWall deliberately ships with **no default geometry**. A plausible-looking
> default (say 1920×256 at 0,0) is a valid window on the *operator's* desktop, it
> passes validation, and the wall just stays dark — a confusing failure. Being
> forced to set it once means the first run lands on the wall.

## 4. Add clips

Click the **+** tile to browse, or drag video files straight onto it. Each lands
in a numbered **slot**. Slots are the stable identity in SimpleWall: they're what
the scheduler and the Stream Deck address, so a clip keeps its trigger even when
you swap the file behind it.

Dropping a file on the **+** tile *adds* it. Dropping one on an existing tile
*replaces* that slot, keeping its number and triggers — the same as right-click →
**Replace clip N…**.

Click a tile to put it on the wall.

Formats: whatever your libvlc build handles — H.264 in `.mp4` is the safe choice.
Encode at your wall's exact pixel dimensions; scaling a mismatched clip costs
quality and CPU.

## 5. Tune the look

With a clip playing, drag the **Brightness** and **Contrast** faders. LED walls
are usually far brighter than a desktop monitor, so this is often needed.

The look is saved **on the clip**, not on the wall. Each clip comes up at its own
setting whenever it plays, from any trigger. What you see is what's saved — there
is no separate save button.

The faders are disabled when nothing is on the wall, because there's no clip to
hold the setting.

## 6. Wire up a Stream Deck (optional)

SimpleWall listens for OSC on UDP port 7000.

In Companion (or any OSC-capable controller), add a button sending an OSC message
to the SimpleWall PC's IP on port 7000 with address `/clip/3` to play slot 3.

Two things that bite people:

- **Windows Firewall drops inbound UDP silently.** You need an inbound rule
  allowing UDP 7000. Nothing in the log will tell you this is the problem — the
  packets simply never arrive.
- **Send from a machine that can reach the wall PC.** Same subnet, no client
  isolation on the Wi-Fi.

To make your controller's faders follow the wall, set **OSC reply host** to your
controller's IP in Settings. Replies are off until you do.

See [Configuration](CONFIGURATION.md#osc) for the full address list.

## 7. Schedule changes (optional)

The **Scheduler** tab runs commands at a time of day, on any set of weekdays, or
on a single one-off date. Typical use is a wall that follows the day: a morning
loop at 06:00, a different one at sunset, something else overnight.

A scheduled event can play a clip, play/pause/stop, or set a brightness or
contrast value.

## 8. Make it survive a reboot

For an unattended wall, two settings in **Settings**:

- **Autostart** — adds SimpleWall to the Windows Run key, so it comes back after
  a reboot or a power cut.
- **Default clip** — right-click a tile → *Make this clip default*. The wall
  boots straight into that clip instead of sitting dark until the next scheduled
  event.

Together these mean a power cut at 3am fixes itself.

## Troubleshooting

**The wall is black and nothing happens.** Check the output window is actually
over the wall region — set the geometry again and watch where the black window
lands. Check the display is extended, not mirrored.

**Stream Deck does nothing.** Almost always the firewall rule. See step 6.

**A clip won't play.** Check the log — the path is shown in the app's title bar,
usually next to the executable or in `%LOCALAPPDATA%\simple-wall\`. A missing
file or a codec libvlc can't open is logged by name.

**Something else.** The log is append-only, rolls at ~5 MB, and records every
trigger, load, swap (with timing) and scheduler firing. It's usually enough to
reconstruct exactly what happened — start there, and include the relevant lines
if you open an issue.
