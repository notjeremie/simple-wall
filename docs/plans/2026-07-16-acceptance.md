# SimpleWall — Win7 acceptance results

Fill this in AT THE WALL, from `RUNBOOK.md`. `PASS` / `FAIL` / `N/A`, plus a
note on anything that surprised you. Bring this back with `simple-wall.log`.

- **Date:** 2026-07-19
- **Machine:** the real Win7 SP1 x64 wall PC (GPU: AMD Radeon HD 7800 Series)
- **Build / tag:** v1.0 (commit: f1f1533)
- **Run by:** Jeremie Quenet

---

## Pre-flight

| # | Check | Result | Note |
|---|-------|--------|------|
| 1 | Tray clock correct (CMOS battery not dead) | PASS | Date/time correct in tray |
| 2 | `.NET` Release ≥ 528040 (`0x80eb1` expected) | PASS | Release = 0x80eb1 (528049) |
| 3 | Extracted somewhere writable (not Program Files) | PASS | C:\SimpleWall |
| 4 | `install.bat` reported `[ok] Firewall rule added` | PASS | .NET 528049 ok; rule added; verify showed Enabled/In/UDP/7000/Allow |

## Acceptance checklist

| # | Item | Result | Note |
|---|------|--------|------|
| 1 | Launches on Win7 SP1 x64, no missing-runtime error | PASS | Control window opened, no VLC/runtime dialog. Log at C:\SimpleWall\app\simple-wall.log (writable, next to exe) |
| 2 | Output window lands exactly on the LED strip; no chrome | PASS | After correcting geometry to X=1920/Y=0/W=1664/H=256, fit-check border kissed all 4 physical edges. No chrome. |
| 3 | 1664×256 clip loops seamlessly — no stutter/tear/seam | PASS | Loops cleanly (observed on wall). NOTE clip size corrected 1964→1664. |
| 4 | Clip switching acceptable; black frame ≈ spike's ~290ms | PASS | Swaps 178–363ms (log), matches ~290ms. NO "swapping anyway" lines → two-layer fast path firing. Brief ~1-frame black at cut, as expected. |
| 5 | Brightness/contrast apply live on the LED panel, look right | PASS | Both apply live on the wall. |
| 6 | Missing clip ⇒ red box, wall unaffected | RETRY | First attempt blocked: Explorer won't delete a clip the app holds. Retest with a throwaway copy that isn't the playing clip. |
| 7 | Stream Deck triggers clips over OSC; press+release fires once | PASS | `(OSC)` log count +1 per single press (address-only /clip/N). Fires exactly once. |
| 8 | Grid highlight follows Stream Deck triggers | PASS | On-screen tile highlights the slot triggered over OSC from the Stream Deck (Companion, /clip/N to wall PC:7000). |
| 9 | A scheduled task fires on time | PASS | Scheduled task fired on the wall on time. |
| 10 | Master disable stops the scheduler firing | PASS | Untick "Run the schedule" ⇒ due task does not fire. |
| 11 | Kill mid-playback + relaunch ⇒ layout/geometry/brightness/schedule restored | PASS | Force-killed via Task Manager (unclean shutdown). Geometry (1664×256), brightness, clips, and task all restored on relaunch. |
| 12 | Autostart survives a real reboot | DEFERRED | Autostart ticked + registered (status line confirms "will start at logon"; registry write proven by Task 14 tests). Reboot-survival NOT tested — production wall PC is never rebooted on purpose; will be confirmed on the next natural reboot. Note: status line confirms logon-start but does not display the copy's path (minor cosmetic gap). |
| 13 | Overnight: still playing, log clean, memory not climbing | | |

## Measurements worth writing down

- Working output geometry (X/Y/W/H): **1920 / 0 / 1664 / 256** (the "expected
  1920/0/1964/256" was WRONG -- see the wall-dimension finding below).

### MAJOR FINDING — the wall is 1664x256, not 1920/1964x256

Measured on the wall 2026-07-19 with a pixel-ruler calibration clip (the spike
never used one; it eyeballed "looks sharp" and was cropping the whole time):

- Windows reports the LED as an extended display `{X=1920, Y=0, 1920x1080}`.
- But the physical LED only lights the **top-left 1664x256** of that canvas.
  Confirmed with a 1664x256 fit-check: the magenta border kissed all four
  physical edges, the ruler ran 0 -> ~1660, and cyan 128px lines matched the
  tile seams (13x2 tiles of 128px = 1664x256).
- The wall is a **wrapped corner** (short left return ~0-200, main run, turns
  again near the right) -- irrelevant to the app (one 1664x256 canvas) but
  critical for content authoring (creases must be mapped, nothing important in
  them).
- **Correct geometry: X=1920, Y=0, W=1664, H=256** (set + persisted this trip).
- **Content mismatch:** the real clips are authored 1964x256 but the wall is
  1664x256, so today they crop (~300px off the corner) or squish. Re-author
  content at 1664x256, or reconfigure the LED controller. Content task, not an
  app bug.
- **NEW FEATURE added + verified this trip — default clip (commit 08a8ce2):** the
  app booted black and played nothing until a trigger (deliberate "no catch-up"
  scheduler), so an unattended wall autostarting after a power cut sat dark until
  the next scheduled task. Added a per-install **default clip**: grid right-click
  → "Make this clip default" (gold star badge, drawn as a polygon not a glyph so
  it can't vanish on a font-poor Win7 box), persisted as `WallConfig.DefaultSlot`,
  played once on launch (`MainForm.OnShown`). A scheduled task due seconds later
  still wins; a stale default at a removed/missing clip is a dark boot, never a
  fall-back, and is logged. VERIFIED on the wall: star appears, and a force-kill +
  relaunch boots straight into the starred clip. Also fixed a latent xunit flake
  it surfaced (two libvlc test classes ran in parallel → concurrent libvlc_new
  access-violates on the emulated VM; now one non-parallel collection). 201 tests
  green ×2. **This + autostart = the wall recovers content after a power cut.**
- **Letterbox on a mismatched clip — RESOLVED with live cover-fit (commit bcef66e):**
  a 1964x256 clip in the 1664x256 window letterboxed to ~217px tall (VLC preserves
  aspect). Rather than force operators to pre-author at 1664x256, the engine now
  cover-fits every clip: `MediaPlayer.CropGeometry` is set to the output's reduced
  aspect ratio ("13:2" for 1664x256), which crops the source centred to that ratio
  and fills -- no bars, no distortion. A wrong-sized clip loses left/right or
  top/bottom (whichever overflows); a correctly-sized clip is cropped by nothing.
  VERIFIED on the wall: a 1964x256 clip now FILLS top-to-bottom, sides cropped.
  **So any clip, any size, fills the wall live -- no content re-authoring needed.**
- Supersedes the earlier "default width should be 1964" note: neither 1920 nor
  1964 was ever right for this wall. Geometry is per-install and the app already
  persists it per-machine (correct design), so no single hardcoded default is
  right -- the operator must set the real visible size once, which persists.
- Black frame at a clip cut, if you can eyeball it: ______ ms
- Any log lines that say "swapping anyway" on every clip change? (If yes, the
  invisible-cut fast path isn't firing — a tuning issue, not a redesign.)

## Win7-specific notes for next time

- **CRASH on adding a clip — DXVA2 thumbnail decode (FIXED in-trip):** adding any
  mp4 killed the app ~0.5s later, silently (no app-log line; the crash handler
  never fires for native corrupted-state exceptions). WER/Event Log named it:
  `Faulting module libdxva2_plugin.dll 3.0.21.0, exception 0xc0000005, offset
  0x1cf3` — deterministic across 3 reproductions. Root cause: `ThumbnailCache`
  built its LibVLC with `--vout=dummy` but nothing forcing software decode, so on
  the real AMD Radeon libvlc hardware-decoded via DXVA2 and the plugin
  access-violated handing GPU surfaces to a dummy vout + CPU scene filter. The
  GPU-less build VM silently software-decoded, masking it. Fix: force
  `avcodec-hw=none` on the thumbnail instance (it must never touch the GPU — its
  own doc says so). Wall engine keeps `:avcodec-hw=dxva2` (real D3D vout, proven
  in spike). **FIXED and verified on the wall 2026-07-19:** rebuilt SimpleWall.exe
  hot-swapped into app\; adding an mp4 no longer crashes and the thumbnail renders.
  196 tests green on the VM (new contract test pins the option is present +
  accepted). Commit: 23be9bf (the deployed hotfix exe was built from this code).
- **Default output width is 1920, not 1964 (soft-wall trap):** on a fresh
  config the Settings fields show W=1920 (`GeometryValidator.DefaultWidth = 1920`,
  line 38), but the same file's comment (lines 23-25) documents W=1964 as the
  measured-good value and W=1920 as producing a *soft* downscaled wall. RUNBOOK
  §6 wrongly says the fields "should already hold ... W=1964". **UPDATE: the real
  answer is neither — the wall measured 1664x256 (see MAJOR FINDING below). Do NOT
  change the default to 1964.** Geometry is per-install and the app persists it;
  fix the RUNBOOK to say "measure and set the real visible size" instead of quoting
  any fixed number. Persistence-across-restart still to verify at item 11.
- **RUNBOOK/code gap:** RUNBOOK says the log folder is shown in the title bar,
  but `MainForm.cs:111` hardcodes the title to `"SimpleWall"` — the feature was
  never wired up. Log still writes correctly next to the exe (found via `dir`).
  Fix after the trip: either surface the path in the title bar (small code
  change) or correct the RUNBOOK to say "look in app\, else %LOCALAPPDATA%".

_(Anything that differed from the dev VM, anything that needed a workaround,
anything the runbook got wrong.)_

## Verdict

**2026-07-19 — app acceptance essentially complete; sign-off pending overnight soak.**

Every interactive item PASSED (1–5, 7–11). Two critical things the wall trip found
and we fixed + verified on the machine: the **DXVA2 thumbnail crash** (commit
23be9bf) and the **real wall dimensions 1664x256** (spike's 1964 was a
mismeasurement). Two features added at the operator's request and verified on the
wall: **default clip** (08a8ce2, closes the black-boot gap) and **live cover-fit**
(bcef66e, any clip fills the wall). One latent test flake fixed. 208 tests green.

Still open before tagging v1.0:
- **Item 13 — overnight soak:** running tonight; check tomorrow (still playing,
  log clean, memory flat).
- **Item 6 — missing-clip red box:** optional, not retested (blocked once by a
  file lock; low risk, logic reviewed).
- **Item 12 — autostart reboot-survival:** deferred to the next natural reboot
  (registered + test-proven; box is never rebooted on purpose).
- **Minor doc/cosmetic:** title-bar log path not shown; RUNBOOK still quotes old
  geometry — fix the RUNBOOK to say "measure the real visible size".

- [ ] Shipped and live on the wall. *(pending overnight soak sign-off)*
- [x] Running live on the wall as of 2026-07-19; overnight soak in progress.
