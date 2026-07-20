# simple-wall — where things stand

**Last updated:** 2026-07-19, session 5
**Tests:** 229 passing, 0 failing (+21 this session — per-clip look defaults, Replace-resets-look, `ConfigMigration` seeding, slot-aware `ClassifyLookChange`, `PendingSaveAfter` retry, and the OSC-reply-reads-clip-look regression test)
**Branch:** `master` (user explicitly consented to committing straight to master)

## Read these first, in this order

1. `2026-07-16-simple-wall-design.md` — what we're building and **why things were cut**
2. `2026-07-16-spike-findings.md` — **what the real machine proved**, including three things that changed the design
3. `2026-07-16-simple-wall-implementation.md` — the 15-task plan, with the verified toolchain table at the top

## Task status

| # | Task | State |
|---|---|---|
| 0 | Dev environment | ✅ done |
| 1 | Solution scaffold | ✅ done |
| 2 | RISK SPIKE — VLC on the real Win7 wall | ✅ done, **approach proven viable** |
| 3 | Config model + persistence | ✅ done |
| 4 | Clip library (stable slots) | ✅ done |
| 5 | Command path (`IWallEngine`) | ✅ done |
| 6 | Scheduler due-calculation | ✅ done |
| 7 | OSC message parsing | ✅ done |
| 8 | Output geometry validation | ✅ done |
| 9 | Real VLC engine + output window | ✅ done |
| 10 | Clip grid UI | ✅ done — **the spike is gone; the app now runs the real engine** |
| 11 | Transport + image adjustment UI | ✅ done |
| 12 | OSC listener + reply | ✅ done — **proven end-to-end from the Mac over real UDP** |
| 13 | Scheduler UI + one-second tick | ✅ done — **watched a real task fire on the VM** |
| 14 | Settings, autostart, logging | ✅ done |
| 15 | Packaging + Win7 acceptance | 🟢 **wall trip run 2026-07-19; every checklist item passed; sign-off pending the overnight soak, now re-running on the session-5 clip-looks build** — results in `docs/plans/2026-07-16-acceptance.md` |

**Next action: the v1.0 CANDIDATE build (`9f46e59`, clip-looks) is DEPLOYED to the wall and partially verified live (see Session 5). The wall is in production use until 2026-07-20, so the overnight soak + the remaining live checks (replace-reset, idle-grey, calib fold lines) are deferred to when it frees up. Once the soak runs clean on THIS build, merge `master` into `main` and tag `v1.0`.** `master` is at `9f46e59`, six commits ahead of the packaging commit `f1f1533` (`23be9bf` DXVA2 fix, `08a8ce2` default clip, `bcef66e` cover-fit, `115454f` replace-in-place, `9f46e59` clip-looks). The staged exe at `dist/hotfix/SimpleWall.exe` (101,376 bytes) is this build. **Do not tag yet** — the soak (checklist item 13) needs a clean run against `9f46e59` and is the last open item.

**Task 15 packaging — done and committed:**
- **`packaging/build-release-package.sh`** builds `dist/simple-wall.zip` (~45MB, 486 files, self-contained). It builds Release on the VM, stages `SimpleWall/{app,install.bat,RUNBOOK.md,acceptance.md}`, strips the x86 natives, and **refuses to zip** if a `config.json`, a log, or the x86 folder leaked in (verified: none do). `dist/` is gitignored, so the SCRIPT lives in `packaging/`, not `dist/` — the spike's script was lost to the ignore, this one isn't.
- **`packaging/install.bat`** self-elevates and adds the one thing the app can't do for itself: the inbound **UDP firewall rule** (silent if missing — see wall checklist item 0). Idempotent (deletes any stale rule first), takes an optional port arg for when Settings changes it, and does a non-blocking .NET 4.8 pre-flight. Autostart is deliberately NOT here — it's the in-app HKCU checkbox, which needs no admin and confirms on screen.
- **`docs/RUNBOOK.md`** — the deploy runbook for whoever is at the wall (clock check, runtime check, extract-somewhere-writable, firewall, launch, geometry, clips, the checklist, autostart+reboot, overnight). Modeled on the spike's runbook, which worked.
- **`docs/plans/2026-07-16-acceptance.md`** — the results skeleton, shipped in the package as `acceptance.md`, to fill in AT the wall.

**Verified headless (what the VM can prove):** Release builds clean; the packaged app is self-contained with its x64 natives; libvlc loads and plays on the VM (`LibVlcContractTests`, part of the 195); `install.bat`'s .NET parse and netsh syntax are valid. The self-elevation, the actual `netsh add`, and every item on the acceptance checklist need the real machine.

**To rebuild the package:** `./packaging/build-release-package.sh` (needs the `wallvm` alias up).

## Session 4 (2026-07-19) — the wall trip

Everything below was found or verified ON the real Win7 wall PC unless noted otherwise — this is the trip Task 15 was waiting on.

- **DXVA2 thumbnail crash — found and fixed (`23be9bf`).** Adding any clip crashed the app ~0.5s later: a native `0xc0000005` access violation inside `libdxva2_plugin.dll`, silent because a corrupted-state exception never reaches the managed crash handler. Root cause: `ThumbnailCache` built its LibVLC with `--vout=dummy` but nothing forced software decode, so on the real AMD Radeon libvlc took DXVA2 hardware decode and handed GPU surfaces to a dummy vout plus a CPU scene filter. Fixed by forcing `avcodec-hw=none` on the thumbnail instance. The GPU-less build VM software-decodes everything, so it could never have caught this — real-GPU paths are only provable at the wall. Confirmed via Windows Event Log / WER.

- **REAL wall dimensions: 1664x256 — it's a wrapped CORNER wall.** The spike's "X=1920/W=1964/H=256" (session 1) was a mismeasurement: eyeballed "looks sharp", never checked with a pixel ruler, and it turns out to have been cropping the corner the whole time. Remeasured 2026-07-19 with pixel-ruler calibration clips: Windows reports the LED as an extended display `{X=1920, Y=0, 1920x1080}`, but the physical LED only lights the top-left 1664x256 of that canvas. A magenta-border fit-check kissed all four physical edges at 1664x256; cyan 128px lines matched the tile seams (13x2 tiles of 128px). Correct geometry is **X=1920, Y=0, W=1664, H=256** — set and persisted on the machine. The wall wrapping a corner is irrelevant to the app (still one 1664x256 canvas) but matters for whoever authors content.

- **Two features added at the operator's request, both verified on the wall:**
  - **Default clip (`08a8ce2`).** The app boots black by design (deliberately no catch-up in the scheduler), so an autostarted wall stayed dark after a power cut until the next scheduled task. Added a per-install default clip: grid right-click "Make this clip default" (gold star badge), `WallConfig.DefaultSlot`, played once from `MainForm.OnShown`. A stale default pointing at a removed/missing clip is a dark boot, not a silent fallback, and is logged. With autostart, the wall now recovers content after a power cut.
  - **Live cover-fit (`bcef66e`).** Clips are authored at 1964x256 but the wall is 1664x256, so they letterboxed (black bars top/bottom). The engine now sets `MediaPlayer.CropGeometry` to the output's reduced aspect ratio ("13:2"), which crops the source centred and fills the panel — cover, not letterbox, no distortion. Any clip of any size now fills the wall live; no content re-authoring needed.

- **Fixed a latent test-suite flake this surfaced:** `LibVlcContractTests` and `ThumbnailCacheTests` each initialize native libvlc and ran in parallel xunit collections; concurrent `libvlc_new` access-violates on the ARM64-emulated build VM. Both now share one non-parallel `LibVlc` collection.

- **Acceptance results:** every interactive checklist item PASSED — launch, geometry/fit at 1664x256, loop, clip-switching (~178-363ms, the two-layer fast path firing), brightness/contrast live, OSC fires-once with grid highlight, scheduler fires plus master-disable, restart-restores-state after a force-kill. Item 6 (missing-clip red box) is optional and wasn't retested. Item 12 (autostart survives a reboot) is deferred to the next natural reboot — this is a production wall PC that's never rebooted on purpose, and autostart is registered and already test-proven. Item 13 (overnight soak) is running tonight, to be checked tomorrow.

**Task 14 done — what shipped:**
- `Logging/Log.cs`: the append log moved out of `Program` and grew a ~5MB roll (two files, ~10MB cap). A blocked roll (someone tailing over VNC) still writes the line — an oversized log is recoverable, a missing line is the evidence. Serialized so a crash landing mid-roll can't write into a file being renamed. 12 tests, including concurrent-writers-across-a-roll.
- `Infrastructure/Autostart.cs`: HKCU\Run, no admin. **Deviated from the plan's sketch** — `CreateSubKey` not `OpenSubKey(writable)` (the sketch's null-guard was a silent no-op on the one case worth reporting), path quoted (unquoted splits on the space in "Program Files"), and `RegisteredPath`/`PointsAt` so the tab can warn when autostart points at a **different copy** — the one state a tick box can't express. 12 tests against the **real registry** (ran on the VM, so this IS the round-trip verification).
- `UI/SettingsTab.cs`: OSC port/reply with an **honest** status — it names the port ACTUALLY bound and says "restart to apply" when the box no longer agrees (the socket can't rebind live). Machine IPs from `NetworkInterface`, **never DNS** (a bare hostname was measured at ~10s on the UI thread in Task 12). Geometry with debounced live apply, Reset-to-wall, autostart checkbox with the stale-path warning. Zeros route to the wall via `Resolve`, never applied as 0,0.
- Single-instance **mutex** (`Local\`, GUID-named): a message box then exit on the second instance — a human double-clicked, so there's someone to read it (unlike the crash handler's deliberate silence).
- The **OSC-driven save debt**: `MainForm` debounces a save when the engine changed brightness/contrast in the in-memory config (it deliberately never saves itself). Uses `.Equals`, not `!=`, so a `NaN` in config compares equal to itself and doesn't fire an atomic write on every clip trigger forever.
- **Two RenderShot fixtures** — healthy and warning — and both PNGs were looked at. The warning one renders the interesting branch (long "restart to apply" and "points at a different copy" sentences), the exact thing Task 13's fixture failed to do. Render tall (`... 1000 940`) or the Startup section sits below the MainForm fold.
- Removed the dead `WallConfig.Autostart` bool. The registry is the only source of truth; a second one would disagree the first time anyone touched msconfig.

**A review changed the code (as ever):**
- `Application.ThreadException` was logging and then SWALLOWING — a registered handler resumes the message loop, so an exception recurring at message-loop rate would roll the log forever with the wall visibly broken and no restart. Now honours the spec's "then let it die": log, then `Environment.Exit(1)`.
- Clip triggers were logged without their source. Now mouse ("(mouse)"), OSC ("(OSC)", **PlayClip only** — a fader sweep is ~100 pkt/s and must not storm the log) and scheduler are all attributed. At 3am the one question is who moved the wall.
- The settings boxes clamp what they show; the config could still hold a value the tab wasn't showing (a hand-edited port of 70000 → box 65535, config 70000). Now reconciled into the config once at construction, and the "restart to apply" baseline is snapshotted AFTER that reconcile so a normalised port doesn't read as a pending change.

**Still owed by Task 14, and can ONLY be done on a live desktop/VNC session (the app needs a window station and the LED output window — it will not run headless over SSH):**
1. Tick autostart in the running app, reboot the wall PC (or VM), confirm it comes back; untick, confirm the value is gone. The registry write itself is proven by the tests; the reboot-survival is not.
2. Launch the app twice and confirm the "already running" message box. The mutex logic is simple and reviewed but has not been exercised as two real processes.

Both are also on the Task 15 acceptance checklist, so they get done there if not before.

**The OSC firewall rule** stays in Task 15 (item 0 of the wall checklist) — it's a `netsh` packaging action, not code, and the Task 14 spec doesn't place it here.

Task 7 notes: the trap was real and the plan's own sample code fell into it — `IsButtonRelease` ran before the address switch, swallowing `/brightness 0`. The fix is structural: the release guard now lives in `Trigger()`, which wraps only the valueless addresses; `/brightness` and `/contrast` never see it, because `0` is data there. Both structures were run against the tests to prove the guard bites.

Task 9 notes: brightness/contrast are written to the **in-memory** `WallConfig` but never saved — one atomic file write per OSC packet, at ~100 packets/sec on a fader sweep, is not a thing to do. **Task 14 owes the actual persistence** (debounced, or at exit).

## Session 5 (2026-07-19) — clip-looks, replacing the global

- **Brightness/contrast moved from the wall to the clip (`9f46e59`).** There is no more global wall brightness — a look is now a property of the CLIP. Any trigger that plays a clip (Stream Deck, mouse, scheduler, boot default) brings it up at its own saved look, applied to the incoming layer **before** the swap, so there's no flash of the wrong look on the outgoing frame. Setting a look is just adjusting the fader while that clip is playing — "what you see is what's saved" (debounced, same pattern as the old global save). The sliders and Reset now disable when nothing is on the wall, since there's no clip to hold a look. Replacing a clip's file resets its look — a new file gets a fresh default, not the old file's tuning. A one-time migration, `ConfigMigration.SeedClipLooks`, seeds every existing clip from the old global value on first launch of this build, so the wall looks identical before and after — the global field itself is now vestigial. Design doc: `2026-07-19-clip-looks-design.md`.
- **This also answered the operator's scheduler question for free.** They'd asked about a "play clip" scheduled event that carries its own brightness/contrast. Once look is a clip property, a scheduled play-clip already carries the clip's look — no new scheduler fields needed, no design owed.
- **A review caught two real composition bugs** (the theme holds: every review finds one).
  - `OscReplySender` was still reporting the now-frozen global `_config.Brightness/Contrast` on every `StateChanged` — so after the migration, Stream Deck fader feedback would show a neutral wall while the actual clip sat dimmed, permanently, on every wall the migration ran on. Fixed by adding `IWallEngine.CurrentBrightness/Contrast` (the on-screen clip's own look) and reading those instead.
  - A failed config save followed immediately by a clip switch dropped the pending look edit: the slot-aware save baseline was reset to the new clip's state, so the still-unsaved divergence from the previous clip silently vanished instead of retrying. Fixed with a `_configDirty` flag that restores the event-driven retry; the decision of *when* to retry is delegated to the pure, testable `MainForm.PendingSaveAfter`, so the interesting logic is unit-tested even though the orchestration around it isn't (see Open threads).
- **Fold calibration finalized AND confirmed on the wall.** The corner-wall creases were measured on the physical LED, played back, photographed, and corrected against the ruler over three passes: 175/1475 → 190/1445 (calib6) → **LEFT fold = 191px, RIGHT fold = 1440px** (calib7, confirmed 2026-07-20) on the 1664-wide canvas. Three faces: LEFT RETURN 0–191 = 191px, FRONT FACE 191–1440 = **1249px safe zone**, RIGHT RETURN 1440–1664 = 224px. The orange fold markers are 2px wide, trimmed toward the front face — LEFT covers columns 191–192, RIGHT covers 1439–1440 (an earlier symmetric 3px line straddled the crease unevenly).
- **`dist/hotfix/calib8-gfx-1664x256.mp4` (+ `.png`) is the GFX department handoff sheet.** It is the artifact to send artists: magenta 1px fit-check border touching all 4 edges (calib3 style), ruler every 200px labelled top and bottom, the three faces with exact pixel ranges, and red keep-clear bands over each fold. Content authors keep logos/faces out of the red fold zones. This is a content-authoring aid, not app code — it does not touch the soak. (Superseded: `calib5` at 175/1475, `calib6` at 190/1445.)
- **Deployed and partially verified LIVE on the wall (2026-07-19).** The `9f46e59` build was deployed to the real Win7 wall. Two things confirmed on the hardware — the highest-risk paths: (1) the migration left the wall looking **identical** to before (each clip inherited the old global), and (2) **per-clip fader persistence works with real clip switching** — set a look, switch away, switch back, the look returns. The remaining two were confirmed on the hardware 2026-07-20 (see below), so every live spot-check is now closed.
- **Overnight soak PASSED, confirmed against the log (2026-07-20).** `simple-wall.log` was pulled off the wall PC and read. **18h29m of continuous uptime** — the last `SimpleWall starting` is the post-migration launch at 2026-07-19 17:10:11 and there is no restart line through 2026-07-20 11:39:26. In that window: **24 swaps, 5/5 scheduler events on time** (17:58, 18:58, 19:58, 21:58, and 06:00 after an 8-hour idle gap — the timer surviving the quiet stretch was the point of the exercise), and **zero** error/exception/timeout lines. Swap latency across the whole log ranges 165–471 ms with no upward trend; the final swaps are the fastest in the file (165/169 ms), which is where a handle or memory leak would have shown first. The only failure-shaped events are three `Slot 2 unavailable (no clip assigned)` OSC triggers, each logged, surfaced to the UI, and leaving the wall unchanged — graceful handling. **Caveat: brightness/contrast changes are not logged**, so the soak says nothing about clip-looks persistence; that rests on the unit tests plus the live spot-checks. The fold lines were also re-checked on the hardware that morning, producing the calib7 correction above.
- **Observed: re-triggering the clip already on the wall is a silent no-op.** Four times in the log a trigger produced no load/swap (17:11:48 clip 7; 21:06:35/36/40 clip 3, three presses in five seconds; 11:21:25 clip 1) — in every case that clip was already on the wall. The 21:58 scheduler firing did the same, because the operator had manually played clip 13 seven seconds earlier. Behavior is consistent and almost certainly correct for a video wall (you don't want a visible restart jump), so it was left alone. But the 21:06 triple-press reads like an operator expecting a response and not getting one — worth confirming they know it's intentional, and a candidate for a UI acknowledgement later.
- **The last two live spot-checks PASSED on the wall (2026-07-20).** (1) **Replace resets the look** — a clip tuned to brightness 1.40, then replaced via right-click → "Replace clip N…", came back at 1.00/1.00 on the new file, and the Stream Deck button for that slot still fired the new file (slot + mapping preserved, which is the whole point of replace-in-place). (2) **Sliders grey when idle** — "Stop" disabled both faders, both per-row Reset buttons, and Play/Pause together; playing a clip re-enabled them at that clip's saved look. Pause correctly leaves the faders live, since a paused clip is still loaded. Nothing on the v1.0 checklist is now outstanding.

## How to build and test (nothing builds on the Mac)

```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && dotnet test"'
```

- **`pushd`, never `cd /d`** — cmd refuses UNC paths as a working directory.
- The VM is **French**: `échec : 0` = ZERO failures (good), `réussite : 54` = 54 passed, `La génération a réussi` = build ok.
- The `wallvm` SSH alias is configured (key `~/.ssh/simple-wall-vm`). SDK is per-user at `C:\Users\notjeremie\.dotnet`.
- Known trap: the SMB share occasionally serves a stale build. If a fix "isn't taking effect", `dotnet build --no-incremental`.

**libvlc RUNS ON THE VM. The engine is not as untestable as the plan assumed.** The VM is ARM64 Windows 11 running our x64 binaries under emulation, and libvlc 3.0.21 loads and plays there. No video card is needed: `--vout=dummy` plus `vlc://pause:1` (from the bundled `libidummy` plugin) gives real playback with real events and a real duration. `LibVlcContractTests` uses this to prove things that were previously only reasoned about. Reach for this before declaring anything about libvlc unverifiable — it took ten minutes to set up and it immediately disproved one of my own claims. Note `xunit.runner.json` sets `shadowCopy: false`, without which `Core.Initialize()` cannot find the natives.

Two limits worth knowing: `--vout=dummy` never increments `VoutCount` (so the swap's poll is not exercised by any test), and libvlc **silently ignores** unknown *media* options — only unknown *instance* options are fatal. So a typo in `:avcodec-hw=dxva2` costs the optimisation with no signal anywhere.
- **If ssh times out, the VM is simply off.** `prlctl start` is Parallels Pro-only, so the user has to start it from the Parallels GUI. `prlctl list -a` reads status fine without Pro.
- ssh occasionally answers `Permission denied (publickey)` or the share briefly vanishes (`Le chemin d'accès spécifié est introuvable`) for one call, then works again. It's flaky, not broken — retry once before investigating.
- Reading an exit code over ssh: use `cmd /v:on /c "... & echo EXIT=!ERRORLEVEL!"`. Plain `%ERRORLEVEL%` is expanded when the line is *parsed*, so it reports the previous command's code and will happily tell you a failing command passed. Don't pipe to `findstr` either — you'd read findstr's code instead.

## The spike changed the design. Task 9 must honour all of this

Measured on the real Win7 SP1 x64 wall PC, 2026-07-16. **Not guesses — three of these contradict what the design originally assumed.**

1. **The LED is an EXTENDED display at X=1920**, not a mirror of the primary's top strip. Working geometry: **X=1920, Y=0, W=1964, H=256**.
2. **Width 1964 deliberately exceeds the 1920 panel.** The clip is 1964 wide; at 1920 VLC downscales and the wall looks soft, at 1964 the overhang crops and the visible area is pixel-1:1 and sharper. **Never clamp width.**
3. **Two layers, hard cut.** The black frame measured ~290ms (`GAP A->B: 112 ms`, `FIRST PICTURE: 286 ms`) and is plainly visible. Load the incoming clip into a hidden second layer and swap only once it has a picture, so the outgoing clip holds the wall. Still one clip at a time — layers are an implementation detail, not a feature. **The design originally cut layers on the assumption the black was brief. It isn't.**
4. **Restart on `EndReached`.** `:input-repeat=65535` is a COUNTDOWN, not "forever" — a 30s clip stops after ~22 days, and this app runs unattended for months. **Now measured, not reasoned**: `LibVlcContractTests` drives real libvlc and proves plays == repeat + 1 (`:input-repeat=0/1/3` → EndReached at 1039/2068/4100 ms). The claim that "65535 is the option's max" was **wrong** and has been removed: libvlc accepts 65536, 70000 and 999999 without complaint. Whether it honours them or wraps them to something *shorter* is unproven, so 65535 stays and the EndReached restart makes the question moot. This one was originally caught by reading, not observing — it is observable in about a second, and now is.
5. **`:no-audio`.** The clips carry AAC and VLC was decoding and routing it to the sound card. Nothing ever told VLC this is a video wall.
6. **Try `:avcodec-hw=dxva2` and measure.** VLC picks a D3D11 decoder against a Direct3D9 display, fails to insert the brightness filter across every chroma combination, tears the vout down, rebuilds on DXVA2, then works — on every play. Likely most of the 290ms.
7. **Drop the Win7 fallbacks** (software decode, `--vout=direct3d9`/`directdraw`). Built as insurance, proven unnecessary — the default path works. Not in tension with 6: forcing the DXVA2 *decoder* is a measured optimisation; the others were guesses at a problem that didn't materialise.
8. **Control window is NOT always-on-top** (user asked, 2026-07-16). The spike's TopMost was a workaround for its own wrong geometry default. The **output** window keeps always-on-top.

Environment: GPU is an **AMD Radeon HD 7800 Series**. .NET 4.8 present (`Release = 0x80eb1` = 528049), `d3dcompiler_47.dll` present, VC++ redist **not needed** (libvlc is MinGW-built, imports only `msvcrt.dll`). Clips live at `V:\VIZRT\INSIDE_WALL\`.

## How the work gets done

Every task: **implementer → spec-compliance review → code-quality review**, fix rounds until clean. This is not ceremony — **every quality review this session found a real defect**, and the most serious ones were mine:

- I specified VLC **2.x** logging options (`--file-logging`) that make `libvlc_new` return NULL. The app would have opened a window and done nothing, forever, on arrival at the wall. A reviewer proved it with a harness.
- I designed a config save that couldn't survive the power cut it was written for: `WriteAllText` doesn't flush, so the likely artifact is a **zero-length** file — precisely the input that skipped quarantine and then got overwritten.
- I wrote a scheduler that skips any task whose moment falls on the other side of a midnight tick. An implementer proved it by running my own code against a test it wrote.
- Task 13: `Describe` indexed the 7-name day array with whatever was in `Days`, and Json.NET casts a JSON integer to an enum **without range-checking it** — so a hand-edited `"Days": [9]` threw from the `MainForm` constructor, before `Application.Run` existed to catch it. A reviewer **reproduced it as a live crash on the VM**: the app died with no window and no dialog, the exact outcome `CreateEngine` was written to prevent and `ConfigStore` refuses to cause.
- Task 13: my `TaskEditFixture` claimed "the editor ADAPTS… that is a claim about what is on screen, so it gets looked at" — **and only ever rendered the branch where both adaptive controls are hidden.** In the branch it never showed, `Value:` floated 54px below its own spinner next to nothing (no `RowStyles`, so the last row ate all the slack). Nothing collapsed, so RenderShot exited 0 and the layout dump was happy. **A fixture that doesn't render the interesting state is a fixture that proves nothing.**
- Task 13: `Scheduler.DueBetween` honoured `Spent` for **every** task — while `ScheduledTask.Spent`'s own documentation says it is "meaningless for recurring tasks". A reviewer traced the path: the scheduler sets `Spent` on a one-off, the operator later edits it into a weekly, and the stale flag rides along. The row then looks perfectly normal — ticked, not red, a sensible sentence — and **never fires again, forever, with nothing logged**. That is the exact silent lie the whole tab exists to prevent. Fixed in both places: the scheduler now honours `Spent` only for one-offs, and any save from the editor clears it.
- Task 12: `OscReplySender` used `UdpClient.Send(bytes, len, HOSTNAME, port)`, which **resolves DNS on every call** — from `StateChanged`, on the UI thread. A reviewer measured a bare hostname like `streamdeck-pc` (i.e. exactly what someone types into `OscReplyHost`) at **~10 seconds per call**, uncached, every time. At ~100 fader packets a second the UI thread never catches up: the wall wedges permanently. My test gave false comfort — it used `no-such-host.invalid`, the one unreachable name that's fast, and asserted only "does not throw", which was never the risk. The rewritten test measures duration and fails at **25,506ms** against the old code. Resolution now happens once, off the UI thread.
- Task 12: I marshalled with `if (InvokeRequired) BeginInvoke else call()`. **`InvokeRequired` returns false when the handle doesn't exist**, not just when you're on the right thread — and `Raise` only ever runs on the receive thread, so that `else` was never "we're on the UI thread", it was "there's no window yet", twice per run (before `Application.Run`, and after the form closes). It would have called `Execute` — native libvlc — on the receive thread. `MainForm.BeginInvokeSafely` already had the right pattern and even predicted this in a comment; I didn't reuse it.
- Task 11: I saved brightness on mouse-release, so the **mouse wheel** — which raises `Scroll` but never `MouseUp` — changed the wall and never persisted it. Worse, `WM_MOUSEWHEEL` goes to the *focused* control, so once an operator touched a slider, scrolling the clip grid would drift wall brightness 0.03 a notch and silently revert on restart. A reviewer measured it. The wheel is now ignored outright (`WallTrackBar`), and saving is debounced so every input route is covered.
- Task 11: my slider clamp re-derived `Math.Round(v * 100)` instead of reusing the engine's, so a config holding `1e40` (which overflows float to infinity, and config.json is deliberately not range-validated) put the **slider at 0 while the wall ran at 2.0** — the readout saying the exact opposite of the truth. **The NaN/clamp trap has now caught three separate components**; there is one `AdjustValue.Clamp` now, and everything routes through it.
- Task 10: I disposed `ThumbnailCache` without waiting for an in-flight extraction, so `libvlc_release` ran against a live player. A reviewer **measured a 0xC0000005 access violation, 3/3 runs** — and since .NET 4.0 a corrupted-state exception is not delivered to `AppDomain.UnhandledException`, so the crash handler writes **nothing**. Closing the window during the ~100s of extraction on a fresh launch would have killed the wall PC leaving no evidence at all.
- Task 10: every error message the UI can produce was written and then **erased on the next line** — the error paths set the status text, then called `BuildGrid`, which repainted over it. "Slot 3: file not found" became "3 clip(s). Wall: slot 2." instantly. The clip-unavailable event, the engine's only way to tell an operator their button did nothing, was discarded at the last step. Measured by a reviewer, not reasoned.
- I wrote Task 9's `ApplyGeometry` to call `GeometryValidator.Validate`, so on a **first run** the config's zeros passed validation (0,0 really does overlap the primary screen) and the output window opened on the operator's desktop while the wall stayed dark — reproducing spike finding 1, the exact thing `DefaultGeometry` was built in Task 8 to prevent. `DefaultGeometry` was dead code, called by nothing but its own tests. A reviewer traced it with the real machine's screen numbers. **Unit tests all passed: the bug lived in the composition, not the parts.**
- I wrote that adding layer B first would leave layer A in front. **Backwards** — `Controls.Add` appends and child index 0 is the front. A reviewer measured the real HWND z-order and proved the engine's `_frontIsA = true` disagreed with reality at startup. It self-healed on the first swap so it never bit, but one refactor away it stops the wrong player and shows a layer that never played anything: a black wall, no exception, no log.
- I documented that "65535 is `:input-repeat`'s maximum". **False, and I put it in three files including this one.** A reviewer measured 65536/70000/999999 all accepted and still looping. The countdown itself is real and now proven; the ceiling was invented.
- I clamped OSC brightness with `Math.Max(0f, Math.Min(2f, value))`, which **does not clamp `NaN`** — both methods propagate it. `/brightness NaN` off the network would have escaped the range and landed on a native VLC filter parameter, on an unattended machine, for months. A reviewer proved it on the VM's own net48 runtime rather than asserting it from memory. **Two of the three defects in this task were in the plan's sample code, not the implementation** — the plan is a draft, not a spec to type in.

Implementers are explicitly told to push back rather than implement something wrong. Several did, correctly. **Keep doing this.**

**Any task that touches a window: render it with `tools/RenderShot` and actually look at the PNG before calling it done.** Reviews aimed at the hard part (VLC) sailed past a window that was visibly broken. There is no longer an excuse for shipping a UI nobody has seen.

## The render gap is closed — use `tools/RenderShot`

**We can now look at a window over SSH, with no desktop session.** `tools/RenderShot` renders any
`Form` to a PNG on the Mac share and prints its layout tree. Read `tools/RenderShot/README.md`
before Task 10; the short version:

```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && tools\RenderShot\bin\Debug\net48\RenderShot.exe SimpleWall.Spike.SpikeForm artifacts\render\spike.png"'
```

It exits `3` if any control collapsed. **The exit code is not the point — open the PNG.** It cannot
tell you a window is ugly or unusable, only that something reached zero size.

It never shows the form and never fires `Load`, deliberately: `Load` is where the real windows start
VLC, and a layout check that needs a video card is a layout check nobody runs. Known fidelity limits
(child 3D borders, title bar, and empty read-only TextBoxes render as nothing) are in the README —
they're `WM_PRINT` semantics, not bugs. Don't chase them.

**It is validated against the real bug, not just against working windows.** Rendering `cea172f^`
(the version the user caught) reproduces it exactly: `GroupBox 16x180`, nine collapsed controls,
EXIT=3, four useless slivers plainly visible in the PNG. If you change how it realises a form,
re-run that and confirm it still fails. The README has the commands.

What didn't work, so nobody re-tries it: `prlctl exec`/`prlctl start` are Parallels **Pro-only**;
`schtasks /it` + PowerShell `CopyFromScreen` fires but never produces output (window station).
Neither is needed now — rendering never touches a desktop.

## Take this list to the wall (Task 15)

-1. **Check the wall PC's clock and CMOS battery.** `TickGuard` now refuses any tick window that goes backwards or exceeds 5 minutes, because a Win7 box that boots believing it is 2019 and then gets corrected by w32time would otherwise walk ~2,750 calendar dates in one tick: every weekly task fires, and every one-off in seven years fires and is burned. The guard makes that survivable; a working clock makes it moot.


0. **THE WALL PC NEEDS A FIREWALL RULE OR THE STREAM DECK CANNOT REACH IT.** Found the hard way, and it is completely silent: with the firewall on (default, all profiles) and no rule for the app, OSC packets from another machine are dropped with no error, no log line, and nothing on screen — the app cheerfully reports "OSC listening on port 7000" the whole time. Loopback still works, so it looks fine from the machine itself. The rule (admin):

   ```
   netsh advfirewall firewall add rule name="SimpleWall OSC" dir=in action=allow protocol=UDP localport=7000
   ```

   Windows normally prompts on first bind, but only in an interactive session and only if someone is there to click Allow — on an autostarted wall PC nobody is. **Packaging must add this rule, not rely on the prompt.** Proven on the VM: before the rule, packets from the Mac vanished; after it, every one arrived.

   **Now handled by `packaging/install.bat`** — run it once as admin during deployment (it self-elevates). It runs exactly this rule, idempotently, and takes an optional port arg if Settings later changes the OSC port. This checklist item becomes "confirm `install.bat` reported `[ok] Firewall rule added`", not a hand-typed `netsh`.

Things Task 9 could not settle away from the hardware. Each has a named symptom — don't just "check it works".

1. **Does the back layer's vout come up while it is occluded?** Still unproven — z-order *is* enough to hide the layer (measured: both VideoViews have `WS_CLIPSIBLINGS`), but whether libvlc builds a D3D9 vout against a window with an empty visible region is unprovable off the real hardware (`--vout=dummy` never increments `VoutCount`, and the VM has no GPU). **This is no longer architecture-threatening, and no longer worth a special trip.** `SwapPolicy` treats "playing but no picture yet" as *swap anyway after 1s*, not as failure: if the vout was merely waiting to be seen, it starts the moment the layer comes to the front, and the worst case is the ~290ms of visible black we'd have had with no layers at all. These are looped background clips — nothing is frame-critical and starting mid-loop costs nothing (user, 2026-07-17). **What to watch for:** if the log says "swapping anyway" on every clip change, the fast path never fires and the cut is visible — that's this assumption being wrong, and it's a tuning problem, not a redesign. If it never says that, the invisible cut is working.

2. **Expect up to one frame (~40ms) of black at the cut**, not zero: the incoming layer's region was clipped until `BringToFront`, so its next Present is the first one that lands. Still far better than 290ms. Look for it rather than assume.
3. **Measure `:avcodec-hw=dxva2`.** It is in on the theory that naming DXVA2 up front skips the failed D3D11 attempt the spike measured. If it misbehaves, deleting that one line in `VlcOptions.Media()` restores the proven default path. A typo there would be silent (see above) — confirm from the VLC log that it took.
4. **The 22-day `EndReached` restart is not seamless** — nothing holds the wall while it reloads. Expect ~290ms of black once every ~22 days.

## Deliberately cut — do not re-add without asking

Crossfades/mixer (the hard cut solved the actual problem), saturation/gamma/hue, audio, clip trimming/speed/effects/BPM sync, cron expressions, scheduled playlists/sequences, catch-up on missed tasks, `ConfigVersion` schema field, config value range validation.

## Open threads

- ~~A **cross-process config race** is possible (autostart + a manual launch = two instances).~~ **Closed in Task 14:** a `Local\`-scoped single-instance mutex in `Program.Main` means the second instance shows "already running" and exits before it opens anything. `ConfigStore`'s `static _gate` still handles the in-process thread case.
- `dist/simple-wall-spike.zip` (45MB) and `dist/prereq/` (169MB of .NET 4.8 + KB4474419) are gitignored, still on disk. The prereqs turned out unnecessary for this machine and can be deleted.
- The spike still lives in `src/SimpleWall/Spike/`. **Task 9 deletes it.**
- Two low-priority follow-ups from the session 4 wall trip: the title bar doesn't actually show the log-folder path — `MainForm.cs:111` hardcodes `"SimpleWall"`. And `RUNBOOK.md` still quotes the old (wrong) 1964 geometry; it should say "measure and set the real visible size" rather than any fixed number.
- Session 5: the `_configDirty` retry orchestration inside `MainForm` (as opposed to the pure `PendingSaveAfter` it delegates to) has no instance-level test — the message-loop-coupled debounce can't be unit-tested in this harness. A reviewer flagged this as optional lock-in, not a blocker.
