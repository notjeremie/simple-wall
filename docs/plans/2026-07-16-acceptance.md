# SimpleWall — Win7 acceptance results

Fill this in AT THE WALL, from `RUNBOOK.md`. `PASS` / `FAIL` / `N/A`, plus a
note on anything that surprised you. Bring this back with `simple-wall.log`.

- **Date:**
- **Machine:** the real Win7 SP1 x64 wall PC (GPU: AMD Radeon HD 7800 Series)
- **Build / tag:** v1.0 (commit: __________)
- **Run by:**

---

## Pre-flight

| # | Check | Result | Note |
|---|-------|--------|------|
| 1 | Tray clock correct (CMOS battery not dead) | | |
| 2 | `.NET` Release ≥ 528040 (`0x80eb1` expected) | | |
| 3 | Extracted somewhere writable (not Program Files) | | |
| 4 | `install.bat` reported `[ok] Firewall rule added` | | |

## Acceptance checklist

| # | Item | Result | Note |
|---|------|--------|------|
| 1 | Launches on Win7 SP1 x64, no missing-runtime error | | |
| 2 | Output window lands exactly on the LED strip; no chrome | | |
| 3 | 1964×256 clip loops seamlessly — no stutter/tear/seam | | |
| 4 | Clip switching acceptable; black frame ≈ spike's ~290ms | | |
| 5 | Brightness/contrast apply live on the LED panel, look right | | |
| 6 | Missing clip ⇒ red box, wall unaffected | | |
| 7 | Stream Deck triggers clips over OSC; press+release fires once | | |
| 8 | Grid highlight follows Stream Deck triggers | | |
| 9 | A scheduled task fires on time | | |
| 10 | Master disable stops the scheduler firing | | |
| 11 | Kill mid-playback + relaunch ⇒ layout/geometry/brightness/schedule restored | | |
| 12 | Autostart survives a real reboot | | |
| 13 | Overnight: still playing, log clean, memory not climbing | | |

## Measurements worth writing down

- Working output geometry (X/Y/W/H): ____________ (expected 1920/0/1964/256)
- Black frame at a clip cut, if you can eyeball it: ______ ms
- Any log lines that say "swapping anyway" on every clip change? (If yes, the
  invisible-cut fast path isn't firing — a tuning issue, not a redesign.)

## Win7-specific notes for next time

_(Anything that differed from the dev VM, anything that needed a workaround,
anything the runbook got wrong.)_

## Verdict

- [ ] Shipped and live on the wall.
- [ ] Issues found (list above); NOT signed off.
