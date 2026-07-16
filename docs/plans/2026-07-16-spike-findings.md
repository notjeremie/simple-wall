# Spike findings — VLC on the real Win7 wall

**Date:** 2026-07-16
**Machine:** Windows 7 Home Premium SP1, x64 (`Microsoft Windows NT 6.1.7601`)
**Run by:** the operator, over VNC from the work computer
**Verdict: the approach is viable. Build it. One design decision reversed — see Q5.**

## The five questions

**1. Does VLC 3.x initialize at all on Win7 SP1? — YES.**
```
Core.Initialize() succeeded.
LibVLC version: 3.0.21 Vetinari
Process bitness: x64
.NET runtime version: 4.0.30319.42000
```
No workarounds needed. `vout=default`, `softwareDecode=False` — the default hardware path works. None of the Win7 fallbacks we built (software decode, `direct3d9`, `directdraw`) were needed.

**2. Does it decode and loop a real 1964x256 mp4 without stutter? — YES.**
Clean loop, no stutter, no visible seam at the loop point. `:input-repeat=65535` is sufficient; looping needs no further work. Media resolution confirmed read correctly: `1964:256`.

**3. Does a borderless always-on-top window land pixel-accurately on the strip? — YES, but the geometry was not what the design assumed. See below.**

**4. Do brightness/contrast apply live, with no restart? — YES.**
Both respond live on the LED panel. VLC's adjust filter is sufficient; no shader work needed.

**5. How bad is the black frame between clips? — BAD ENOUGH TO CHANGE THE DESIGN.**
```
GAP A->B: 112 ms          (click -> MediaPlayer "Playing" state)
FIRST PICTURE: 286 ms     (click -> a vout exists)
```
~290ms of black on every clip change. The operator perceived it as "half a second" — black on a bright wall reads longer than it measures. Either way it is plainly visible and not acceptable on a live wall.

This **reverses the design's accepted trade-off**. The design cut layers and crossfades and accepted "a brief black frame", flagging that the two-layer mixer was the first thing to reconsider if it proved visible. It did.

## Geometry — the design's assumption was wrong

```
\\.\DISPLAY1 primary=False {X=1920,Y=0,Width=1920,Height=1080}   <- the LED wall
\\.\DISPLAY2 primary=True  {X=0,Y=0,Width=1920,Height=1080}      <- the desktop
```

The LED is an **extended second display at X=1920**, not a mirror of the primary's top strip. The original brief ("recopies only the upper part of the screen") read as mirroring; it is not. Defaulting the output to 0,0 painted onto the desktop, not the wall.

Note the trap: the LED enumerates as `DISPLAY1` while the *primary* is `DISPLAY2`. Device names are not an ordering — always read `Bounds` and `Primary`.

**Working geometry: X=1920, Y=0, W=1964, H=256.**

**W=1964, not 1920** — and this is deliberate. The clip is 1964 wide; the panel is 1920. At W=1920 VLC downscales the clip to fit and the wall looks softer. At W=1964 the window is 44px wider than the display, the overhang is simply cropped, and the visible area is pixel-for-pixel 1:1 — visibly sharper on the panel. Sharpness beats the lost 44px.

The real app must not hardcode any of this: it should default the output onto the non-primary display's bounds rather than 0,0, and let W exceed the screen width without "helpfully" clamping it.

## Consequences for the build

1. **Task 9 changes: two layers, hard cut** (chosen by the user over accepting the black, and over adding crossfades). Two stacked VideoViews in the output window. On trigger, load the new clip into the hidden layer; only once it has a picture, swap z-order and stop the outgoing one. The outgoing clip stays on the wall throughout, so the wall never goes black. Cost: ~290ms between trigger and change — accepted, and far better than a dropout.
   Still one clip at a time. No mixer, no crossfade slider, no UI change, no change to the OSC contract.
2. **Default output geometry** = the non-primary display's bounds, not 0,0.
3. **Don't clamp output width to the screen.** Wider-than-screen is a legitimate, deliberate setting.
4. **Drop the Win7 fallbacks from the real engine.** Software decode and the `direct3d9`/`directdraw` vout options were built as insurance and proved unnecessary on this machine. No `avcodec-hw` or `--vout` options — keep the default path that works. (They live in git history if a future machine needs them.)
5. **Prerequisites are a non-issue on this machine.** .NET 4.8 present (`Release = 0x80eb1` = 528049), `d3dcompiler_47.dll` present, VC++ redist not needed (libvlc is MinGW-built, imports only `msvcrt.dll`).

## What the spike cost, and what it bought

Three review rounds. Two of them caught defects that would have wasted the trip entirely: VLC 2.x logging options that made `libvlc_new` return NULL (the app would have opened a window and done nothing, forever), and a runbook pointing at files that weren't in the package.

The bug that actually hit was neither. Every GroupBox was built with both `Width=880` and `AutoSize=true`; inside a FlowLayoutPanel, AutoSize wins and computes width from children, and a GroupBox's preferred size ignores `Dock`ed children — so every control group collapsed to a sliver and the window was unusable.

It shipped because **nobody ever rendered it**. It was built and reviewed entirely over SSH, where there is no GUI. Three rounds of paranoid review all aimed at VLC; none looked at the thing the operator touches first. The operator found it in ten seconds by looking at the screen.

**Task 10 (the real UI) must not repeat this.** Before any UI ships to that machine, there must be a way to render it and look at it here — screenshot in the VM, or the operator as the render check with a fast loop. A 32KB EXE swap turned out to be a two-minute iteration; the 45MB package was never the bottleneck.
