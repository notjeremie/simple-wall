# SimpleWall spike -- RUNBOOK

For whoever is at the VNC session. You are **not** debugging. Follow the
numbered steps, record what you observe, and bring back the two files listed
at the end. If something looks wrong, that observation IS the result --
don't try to fix it.

Everything the app does gets written automatically to `spike-log.txt`, next
to `SimpleWall.exe`. You don't need to transcribe anything by hand; just
bring that file back along with the filled-in `FINDINGS.md`.

---

## 0. Before you start

Unzip the package somewhere on the Win7 machine, e.g. `C:\SimpleWallSpike\`.
You should see:

```
SimpleWallSpike\
  spike\            <- the app (SimpleWall.exe + VLC files)
  prereq\           <- ndp48.exe, VC_redist.x64.exe
  RUNBOOK.md         <- this file
  FINDINGS.md
```

---

## 1. Check .NET Framework 4.8 first

Open **Registry Editor** (`regedit`) and navigate to:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
```

Look at the `Release` value (a DWORD).

- **If `Release` is 528040 or higher**: .NET 4.8 is already installed. Skip to step 2.
- **If the key is missing, or `Release` is lower than 528040**: run `prereq\ndp48.exe` from the package. Let it finish. **If it asks to reboot, reboot.** Then come back and re-check the registry value before continuing.

---

## 2. Install the VC++ redistributable

Run `prereq\VC_redist.x64.exe` from the package. This is harmless to run even
if it's already installed -- it will just say so and exit.

---

## 3. Run the app

Go into the `spike\` folder and double-click `SimpleWall.exe`.

**If it fails to start** (crashes immediately, shows a Windows error dialog,
or nothing appears at all): **STOP HERE.** Do not try anything else. Send
back:
- `spike-log.txt` from the `spike\` folder (it may be empty or very short --
  send it anyway)
- a screenshot of whatever error appeared, if any

That is a complete, valid result of this trip. Do not attempt to diagnose it.

**If a window titled "SimpleWall Spike -- VLC on Win7 probe" appears**,
continue to step 4.

---

## 4. Load the two clips

In the **Clips** section:
- Click **Browse...** next to **Clip A**, pick a real clip that's already on
  this machine.
- Click **Browse...** next to **Clip B**, pick a *different* clip.

---

## 5. Get the output window onto the LED strip

Click **Play A**. A second, borderless black window will appear somewhere
(it starts at X=0, Y=0, 1920x256).

Using the **Output Geometry** fields (X, Y, W, H) and the **Apply** button,
nudge that window until it lines up exactly with the LED strip -- not the
desktop monitor, the physical strip.

**Write down the X, Y, W, H numbers that work in `FINDINGS.md`.** These
become the real app's default geometry -- this is one of the two facts this
whole trip exists to collect.

---

## 6. Watch a full loop

Let clip A play through at least one full loop cycle, watching the **LED
panel**, not the desktop preview.

Note in `FINDINGS.md`:
- Any stutter or hitching?
- Any tearing (a visible horizontal split/shear while it plays)?
- Is there a visible seam / glitch at the exact moment it loops back to the
  start?

---

## 7. Brightness and contrast

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

## 8. Switch clips and watch the wall

Click **Play B**, then **Play A**, then **Play B** again -- a few times in a
row. **Watch the wall, not the screen with the control window on it.**

Note in `FINDINGS.md`:
- Is there a visible black flash when it switches? How long does it feel
  like -- a blink, or long enough to notice as "off"?
- Anything else strange during the switch (a frozen frame, an old frame
  flashing briefly, garbage pixels)?

(The exact millisecond gap is already being measured automatically in
`spike-log.txt` from the Play/Stop event timestamps -- you don't need to
time it yourself.)

---

## 9. If it looked black, stuttery, or wrong: try the fixes

Only do this section if something in steps 6-8 looked wrong. If everything
looked good, skip to step 10.

1. Tick **Force software decode**. Click **Play A** again (re-triggering
   applies the option to the new clip). Repeat steps 6-8.
2. If still wrong, change the **vout** dropdown from `default` to
   `direct3d9`. (This recreates the output window -- you'll need to line it
   up again with X/Y/W/H/Apply.) Repeat steps 6-8.
3. If still wrong, try `directdraw` in the same way.

**Record in `FINDINGS.md` exactly which combination (software decode
on/off, which vout) looked best.** Task 9 of the real build carries that
combination forward as a hard-coded setting -- this is the other fact this
trip exists to collect.

---

## 10. Bring it back

Close the app. Collect these two files from the `spike\` folder and get them
back the same way the package arrived (WeTransfer, etc.):

- `spike-log.txt`
- `FINDINGS.md` (filled in)

That's it. Thank you.
