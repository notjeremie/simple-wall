# SimpleWall — deployment RUNBOOK

For whoever is at the wall PC (over VNC or in person). This is the real
deployment, not the spike. Follow the numbered steps in order. Where a step
says **record**, write what you saw into `acceptance.md` — that file is the
sign-off for this trip.

The app writes everything it does to `simple-wall.log`, next to `SimpleWall.exe`.
**The exact folder is shown in the app's title bar** — if the app was unzipped
somewhere read-only it falls back to `%LOCALAPPDATA%\simple-wall\` and then the
Desktop, and the title bar says which. Bring that log back with the filled-in
`acceptance.md`.

---

## 0. What's in the package

```
SimpleWall\
  app\             <- SimpleWall.exe + VLC files (this is the whole app)
  install.bat      <- adds the OSC firewall rule (run once, as admin)
  RUNBOOK.md       <- this file
  acceptance.md    <- the checklist to fill in
```

No runtime installers ship, and none are needed — checked on this exact
machine on 2026-07-16:
- **.NET Framework 4.8** — `Release = 0x80eb1` (528049), ≥ 528040. Installed.
- **VC++ redistributable** — not needed. VLC's libraries are MinGW-built and
  import only `msvcrt.dll`, which every Windows 7 already has.
- **`d3dcompiler_47.dll`** — present in `System32`.

---

## 1. Check the machine's clock first

Thirty seconds, and it protects the schedule. On an unattended Win7 box a dead
CMOS battery is the classic failure: it boots believing it is 2019, w32time
later corrects it, and the scheduler would otherwise try to walk every calendar
date in between. `TickGuard` makes that survivable — but a working clock makes
it moot.

- Confirm the date and time in the tray are correct.
- If they are wildly wrong, the CMOS battery is likely dead. **Record it** and
  flag it for replacement; don't let this machine run the schedule until the
  clock holds across a reboot.

## 2. Confirm the runtime (guards everything after it)

Open `cmd` and run:

```
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```

- **Expected**: `0x80eb1` or any value ≥ `528040`.
- **If missing or lower**: you are not on the machine this was built for.
  **STOP** and report it. The .NET installer is deliberately not in this
  package.

## 3. Extract somewhere writable

Unzip to e.g. `C:\SimpleWall\`. **Not `C:\Program Files\`** — the app writes its
log next to itself, and the log is how anyone knows what happened at 3am. (It
will fall back to `%LOCALAPPDATA%` if it must, but next-to-the-EXE is simplest.)

## 4. Add the firewall rule

Right-click `install.bat` → **Run as administrator** (or just double-click — it
elevates itself and asks). It adds one inbound UDP allow on port 7000.

**This is not optional and it is not something you'll notice is missing.** With
the firewall on and no rule, Stream Deck packets are dropped silently — the app
still says "listening on 7000", loopback still works, and nothing on screen or
in the log says otherwise. Proven on the dev VM. If you later change the OSC
port in Settings, re-run as `install.bat <newport>`.

**Record**: did `install.bat` report `[ok] Firewall rule added`?

## 5. Launch

Go into `app\` and run `SimpleWall.exe`.

- The control window opens. Its title bar shows the log folder.
- A second, borderless black window (the output) opens on the LED wall.

**If a "SimpleWall could not start VLC" dialog appears**, that's the app's own
error handling. Screenshot it, grab `simple-wall.log`, and **STOP** — that is a
complete, valid (if unwelcome) result.

- [ ] **Acceptance: launches on Win7 SP1 x64 with no missing-runtime error.**

## 6. Put the output window on the LED strip

Go to the **Settings** tab. Under **Output window**, the fields should already
hold the wall geometry (**X=1920, Y=0, W=1964, H=256** is the measured-good
setting — W deliberately exceeds the 1920 panel so the wall is pixel-1:1 and
sharp; do **not** clamp it to 1920). Adjust X/Y/W/H until the output window
covers the physical strip exactly, with no visible window chrome. Changes apply
live. **Reset output window** puts it back on the wall at the default if it ever
ends up lost on the desktop.

- [ ] **Acceptance: output window lands exactly on the LED strip; no chrome.**

## 7. Add the clips

On the **Clips** tab, drag the wall's `.mp4` files in (they live at
`V:\VIZRT\INSIDE_WALL\`), or press **+**. Assign them to slots to match the
Stream Deck buttons. A clip whose file is missing shows a red box — that's the
app telling you the path is wrong, not a bug.

## 8. Walk the acceptance checklist

Everything below is on the **LED panel**, not the desktop preview. Record each
in `acceptance.md`.

- [ ] A 1964×256 clip **loops seamlessly** — no stutter, no tear, no seam at the
  loop point.
- [ ] **Clip switching** is acceptable; the black frame at the cut matches the
  spike's ~290ms measurement (it is not zero — nothing holds the wall during a
  reload; expect up to one frame of black at the cut).
- [ ] **Brightness/contrast** apply live and look right *on the LED panel* —
  drag each slider, confirm the wall follows with no stutter, then Reset.
- [ ] **Missing clip** ⇒ red box, wall unaffected (remove a file or point a slot
  at a missing path).
- [ ] **Stream Deck triggers clips over OSC**; a press-and-release fires exactly
  once (not twice, not zero times).
- [ ] **Grid highlight follows Stream Deck triggers** — the on-screen grid lights
  the slot the Stream Deck selected, proving the UI reads the engine, not its own
  clicks.
- [ ] **A scheduled task fires on time** — add one a couple of minutes out on the
  Schedule tab and watch it fire.
- [ ] **Master disable stops the scheduler firing** — untick "Run the schedule",
  confirm a due task does not fire.
- [ ] **Restart restores state** — kill the app mid-playback, relaunch; layout,
  geometry, brightness and schedule all come back.

## 9. Autostart

On the **Settings** tab, tick **Start SimpleWall when Windows starts**. The
status line under it should turn green and say it will start *this* copy at
logon. (If it warns that autostart points at a different copy, untick and
re-tick — that means an older copy is registered.)

- [ ] **Acceptance: autostart survives a real reboot** — reboot the wall PC,
  confirm SimpleWall comes back on its own with the wall playing.

## 10. Two things only time can prove

- [ ] **Leave it running overnight** ⇒ still playing in the morning, log clean,
  memory not climbing. This is the only test for slow leaks, and this machine is
  expected to run for months.
- The 22-day `EndReached` restart is not seamless (nothing holds the wall while
  it reloads). Expect ~290ms of black once every ~22 days. Nothing to do — just
  know it's expected, not a fault.

## 11. Bring back

- `simple-wall.log` (from the folder named in the title bar)
- the filled-in `acceptance.md`
- a photo of the wall running, if you can

Then close nothing — leave it running. It's live now.
