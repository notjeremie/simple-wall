# SimpleWall spike -- RUNBOOK

For whoever is at the VNC session. You are **not** debugging. Follow the
numbered steps, record what you observe, and bring back the files listed
at the end. If something looks wrong, that observation IS the result --
don't try to fix it.

Everything the app does gets written automatically to a log file. **The exact
folder is shown in the control window's title bar** -- it normally sits next
to `SimpleWall.exe`, but if that folder turns out to be read-only (e.g. the
package was unzipped into `C:\Program Files\`) the app falls back to
`%LOCALAPPDATA%\simple-wall-spike\` and then the Desktop, and the title bar
will say which one it actually used. Read the title bar rather than assuming
the log is next to the EXE. You don't need to transcribe anything by hand;
just bring the files back along with the filled-in `FINDINGS.md`.

---

## 0. Before you start

Unzip the package somewhere on the Win7 machine, e.g. `C:\SimpleWallSpike\`.
You should see exactly this:

```
SimpleWallSpike\
  spike\                 <- the app (SimpleWall.exe + VLC files)
  prereq\
    ndp48-offline.exe    <- .NET Framework 4.8 offline installer
    kb4474419-x64.msu    <- only needed if ndp48-offline.exe refuses -- see step 1
  RUNBOOK.md              <- this file
  FINDINGS.md
```

There is no VC++ redistributable in this package and none is needed --
already confirmed the app doesn't require it.

---

## 1. Check .NET Framework 4.8 first

Open **Registry Editor** (`regedit`) and navigate to:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
```

Look at the `Release` value (a DWORD).

- **If `Release` is 528040 or higher**: .NET 4.8 is already installed. Skip to step 2.
- **If the key is missing, or `Release` is lower than 528040**: run `prereq\ndp48-offline.exe` from the package. Let it finish. **If it asks to reboot, reboot.** Then come back and re-check the registry value before continuing.

**If `ndp48-offline.exe` refuses to run or errors out**, this is not
automatically a dead end. The work computer you're VNC-ing from has internet
access and the wall PC (probably) doesn't, so a missing prerequisite doesn't
have to cost a round-trip:

1. If the error mentions a missing update or SHA-2/signing support: install
   `prereq\kb4474419-x64.msu` (already in the package), reboot if asked, then
   retry `ndp48-offline.exe`.
2. If the error instead names **KB4019990** or **d3dcompiler_47.dll**: this
   one isn't bundled. On the **work computer** (not the wall PC), go to
   `https://www.catalog.update.microsoft.com/Search.aspx?q=KB4019990` and
   download the update listed for **Windows 7 ... x64-based Systems**. Push
   that file over VNC to the wall PC, install it, reboot if asked, then retry
   `ndp48-offline.exe`.
3. **Only if neither of those resolves it: STOP.** Copy the EXACT error text
   (or a screenshot of it) into `FINDINGS.md` under "Prerequisite install
   problems" and send it back. That is a valid, complete result of this trip
   -- do not improvise beyond the two things above.

---

## 2. Run the app

Go into the `spike\` folder and double-click `SimpleWall.exe`.

**If it fails to start** (crashes immediately, shows a Windows error dialog
that isn't the one described below, or nothing appears at all): **STOP
HERE.** Do not try anything else. Send back:
- `spike-log.txt`, if you can find it (check next to `SimpleWall.exe` first,
  then `%LOCALAPPDATA%\simple-wall-spike\`, then the Desktop -- it may be
  empty or very short if the crash was immediate; send it anyway)
- a screenshot of whatever error appeared, if any

That is a complete, valid result of this trip. Do not attempt to diagnose it.

**If a dialog titled "Spike -- VLC init failed" appears** (the main control
window will already be open behind it): **also STOP HERE.** This is the
app's own error handling doing exactly its job -- it means VLC itself
couldn't start on this machine, which is precisely what question 1 of this
whole spike is asking. Screenshot the dialog, then send back:
- `spike-log.txt`
- any `vlc-log*.txt` files present in the log folder
- the screenshot

That is a complete, valid result too -- it answers question 1 with "no."

**If a window titled "SimpleWall Spike -- VLC on Win7 probe..." appears with
no error dialog**, continue to step 3.

**One thing that can look like a hang but isn't:** the app rescans all of its
bundled VLC plugins every time it starts, and again every time the vout
dropdown is changed in step 8. There's no plugin cache shipped in this
package, so on a slow disk this can take several seconds of apparent
silence. Give it a little time before assuming something is stuck.

---

## 3. Load the two clips

In the **Clips** section:
- Click **Browse...** next to **Clip A**, pick a real clip that's already on
  this machine.
- Click **Browse...** next to **Clip B**, pick a *different* clip.

---

## 4. Get the output window onto the LED strip

Click **Play A**. A second, borderless black window will appear somewhere
(it starts at X=0, Y=0, 1920x256).

Using the **Output Geometry** fields (X, Y, W, H) and the **Apply** button,
nudge that window until it lines up exactly with the LED strip -- not the
desktop monitor, the physical strip.

**Write down the X, Y, W, H numbers that work in `FINDINGS.md`.** These
become the real app's default geometry -- this is one of the two facts this
whole trip exists to collect.

---

## 5. Watch a full loop

Let clip A play through at least one full loop cycle, watching the **LED
panel**, not the desktop preview.

Note in `FINDINGS.md`:
- Any stutter or hitching?
- Any tearing (a visible horizontal split/shear while it plays)?
- Is there a visible seam / glitch at the exact moment it loops back to the
  start?

---

## 6. Brightness and contrast

Drag the **Brightness** slider left and right while clip A plays. Then do
the same for **Contrast**.

Note in `FINDINGS.md`:
- Does the **LED panel** (again: the panel, not the desktop monitor) change
  brightness/contrast live, with no stutter or restart?
- Does it look right, or does it wash out / clip strangely at either
  extreme?

Use the **Reset** button next to each slider to put it back to 1.00 before
moving on.

---

## 7. Switch clips and watch the wall

Click **Play B**, then **Play A**, then **Play B** again -- a few times in a
row. **Watch the wall, not the screen with the control window on it.**

Note in `FINDINGS.md`:
- Is there a visible black flash when it switches? How long does it feel
  like -- a blink, or long enough to notice as "off"?
- Anything else strange during the switch (a frozen frame, an old frame
  flashing briefly, garbage pixels)?

(Two numbers get logged automatically for every switch, in `spike-log.txt` --
you don't need to time anything yourself, just copy both lines into
`FINDINGS.md`:
- `GAP <from>-><to>: N ms` -- time from clicking Play to VLC reporting it's
  playing. This is a state-machine transition, not necessarily a frame on
  screen.
- `FIRST PICTURE: N ms` -- time from clicking Play to a frame actually
  reaching the video output. This is closer to what the wall shows, and can
  legitimately be a different number than GAP. If it instead says `FIRST
  PICTURE: NOT REACHED after 10s`, that's not a logging glitch -- it means no
  video output ever came up, which is itself an important result.)

---

## 8. If it looked black, stuttery, or wrong: try the fixes

Only do this section if something in steps 5-7 looked wrong. If everything
looked good, skip to step 9.

1. Tick **Force software decode**. Click **Play A** again (re-triggering
   applies the option to the new clip). Repeat steps 5-7.
2. If still wrong, change the **vout** dropdown from `default` to
   `direct3d9`. **Changing vout restarts VLC and stops playback; your
   X/Y/W/H are kept, so you don't need to re-position the window.** Click
   **Play A** again, then repeat steps 5-7.
3. If still wrong, try `directdraw` the same way (change the dropdown, click
   **Play A** again, repeat steps 5-7).

**Record in `FINDINGS.md` exactly which combination (software decode
on/off, which vout) looked best.** Task 9 of the real build carries that
combination forward as a hard-coded setting -- this is the other fact this
trip exists to collect.

---

## 9. Bring it back

Close the app. Collect these files -- check the folder shown in the window's
title bar (see the note at the top of this document) -- and get them back the
same way the package arrived (WeTransfer, etc.):

- `spike-log.txt` -- everything the app itself logged, including the GAP /
  FIRST PICTURE lines
- `vlc-log*.txt` (there may be several -- one per vout/decode combination you
  tried in step 8, each named after the setting it recorded) -- VLC's own
  internal diagnostic log, written by libvlc itself in the same folder; bring
  all of them back even if nothing looked wrong
- `FINDINGS.md` (filled in)

That's it. Thank you.
