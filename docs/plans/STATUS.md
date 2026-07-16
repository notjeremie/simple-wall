# simple-wall — where things stand

**Last updated:** 2026-07-17, session 2
**Tests:** 78 passing, 0 failing
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
| 9 | Real VLC engine + output window | blocked on nothing; **spec changed by the spike, see below** |
| 10 | Clip grid UI | pending |
| 11 | Transport + image adjustment UI | pending |
| 12 | OSC listener + reply | pending |
| 13 | Scheduler UI + one-second tick | pending |
| 14 | Settings, autostart, logging | pending |
| 15 | Packaging + Win7 acceptance | pending |

**Next action: Task 9 (real VLC engine + output window).** The biggest remaining task, and the spike rewrote its spec — read "The spike changed the design" below before writing a line of it. It also deletes `src/SimpleWall/Spike/`.

Task 7 notes: the trap was real and the plan's own sample code fell into it — `IsButtonRelease` ran before the address switch, swallowing `/brightness 0`. The fix is structural: the release guard now lives in `Trigger()`, which wraps only the valueless addresses; `/brightness` and `/contrast` never see it, because `0` is data there. Both structures were run against the tests to prove the guard bites.

## How to build and test (nothing builds on the Mac)

```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && dotnet test"'
```

- **`pushd`, never `cd /d`** — cmd refuses UNC paths as a working directory.
- The VM is **French**: `échec : 0` = ZERO failures (good), `réussite : 54` = 54 passed, `La génération a réussi` = build ok.
- The `wallvm` SSH alias is configured (key `~/.ssh/simple-wall-vm`). SDK is per-user at `C:\Users\notjeremie\.dotnet`.
- Known trap: the SMB share occasionally serves a stale build. If a fix "isn't taking effect", `dotnet build --no-incremental`.
- **If ssh times out, the VM is simply off.** `prlctl start` is Parallels Pro-only, so the user has to start it from the Parallels GUI. `prlctl list -a` reads status fine without Pro.
- ssh occasionally answers `Permission denied (publickey)` or the share briefly vanishes (`Le chemin d'accès spécifié est introuvable`) for one call, then works again. It's flaky, not broken — retry once before investigating.
- Reading an exit code over ssh: use `cmd /v:on /c "... & echo EXIT=!ERRORLEVEL!"`. Plain `%ERRORLEVEL%` is expanded when the line is *parsed*, so it reports the previous command's code and will happily tell you a failing command passed. Don't pipe to `findstr` either — you'd read findstr's code instead.

## The spike changed the design. Task 9 must honour all of this

Measured on the real Win7 SP1 x64 wall PC, 2026-07-16. **Not guesses — three of these contradict what the design originally assumed.**

1. **The LED is an EXTENDED display at X=1920**, not a mirror of the primary's top strip. Working geometry: **X=1920, Y=0, W=1964, H=256**.
2. **Width 1964 deliberately exceeds the 1920 panel.** The clip is 1964 wide; at 1920 VLC downscales and the wall looks soft, at 1964 the overhang crops and the visible area is pixel-1:1 and sharper. **Never clamp width.**
3. **Two layers, hard cut.** The black frame measured ~290ms (`GAP A->B: 112 ms`, `FIRST PICTURE: 286 ms`) and is plainly visible. Load the incoming clip into a hidden second layer and swap only once it has a picture, so the outgoing clip holds the wall. Still one clip at a time — layers are an implementation detail, not a feature. **The design originally cut layers on the assumption the black was brief. It isn't.**
4. **Restart on `EndReached`.** `:input-repeat=65535` is a COUNTDOWN, not "forever", and 65535 is the option's max — a 30s clip stops after ~22 days. This app runs unattended for months. No observation would ever have caught this.
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

## Deliberately cut — do not re-add without asking

Crossfades/mixer (the hard cut solved the actual problem), saturation/gamma/hue, audio, clip trimming/speed/effects/BPM sync, cron expressions, scheduled playlists/sequences, catch-up on missed tasks, `ConfigVersion` schema field, config value range validation.

## Open threads

- A **cross-process config race** is possible (autostart + a manual launch = two instances). `ConfigStore`'s `_gate` is now `static`, which handles threads but not processes. The fix is a single-instance mutex at app startup — **Task 14**, not in `ConfigStore`.
- `dist/simple-wall-spike.zip` (45MB) and `dist/prereq/` (169MB of .NET 4.8 + KB4474419) are gitignored, still on disk. The prereqs turned out unnecessary for this machine and can be deleted.
- The spike still lives in `src/SimpleWall/Spike/`. **Task 9 deletes it.**
