# SimpleWall spike -- FINDINGS

Fill this in at the wall while following `RUNBOOK.md`. Short answers and
ticks are fine -- this isn't a report, it's evidence. Bring it back along
with `spike-log.txt`.

Date/time of this run: ______________________

Machine (name / specs if known): ______________________

---

## The five questions

**1. Does VLC 3.x initialize at all on this Win7 SP1 box?**

- [ ] Yes, no problems
- [ ] Yes, but only after installing .NET 4.8 / VC++ redist from `prereq\`
- [ ] No -- app failed to start (see step 3 of RUNBOOK -- `spike-log.txt` +
      screenshot should already be attached)

Notes: ______________________________________________________________

---

**2. Does it decode and loop a real clip without stutter?**

- [ ] Smooth, no stutter, no tearing, clean loop point
- [ ] Some stutter / tearing
- [ ] Visible seam or glitch at the loop point
- [ ] Wouldn't play at all (black window, no error)

Notes: ______________________________________________________________

---

**3. Does the output window land pixel-accurately on the LED strip?**

- [ ] Yes -- could line it up exactly with X/Y/W/H
- [ ] Close but not exact (describe what's off): __________________
- [ ] No -- couldn't get it to line up at all

**Working geometry (write down the numbers that worked):**

X = ______  Y = ______  W = ______  H = ______

---

**4. Do brightness/contrast apply live, with no restart?**

- [ ] Yes, both respond live and look right on the LED panel
- [ ] Responds live but looks wrong (describe): __________________
- [ ] Delayed / requires a restart to take effect
- [ ] No visible effect on the panel

Notes: ______________________________________________________________

---

**5. How bad is the black frame when switching clips?**

- [ ] Not noticeable
- [ ] A brief, acceptable flash
- [ ] Long enough to be distracting
- [ ] Something worse than a plain black flash happened (describe):
      __________________________________________________________

(The exact gap in milliseconds is in `spike-log.txt` -- look for the
timestamps around "Play" / "Stopped" / "Playing" lines.)

---

## Working decode / vout combination

What ended up looking best in step 9 of the runbook (leave blank if step 6-8
already looked fine with defaults):

- [ ] Default settings were fine -- no changes needed
- [ ] Software decode: ON, vout: ______________
- [ ] Software decode: OFF, vout: ______________
- [ ] Nothing worked well -- describe what you tried and what happened:
      __________________________________________________________

---

## Anything surprising

Free-form -- anything odd, unexpected, or worth knowing that doesn't fit
above:

______________________________________________________________________

______________________________________________________________________

______________________________________________________________________

---

## Files to bring back

- [ ] `spike-log.txt`
- [ ] This file, filled in
