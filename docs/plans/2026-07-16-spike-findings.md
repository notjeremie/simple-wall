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

**2. Does it decode and loop a real 1964x256 mp4 without stutter? — YES, visually. But looping is NOT solved — see finding 1 below.**
Clean loop, no stutter, no visible seam at the loop point. Media resolution read correctly: `1964:256`. **However**, the log revealed `:input-repeat=65535` is a finite counter that expires after ~22 days — invisible to any observation, fatal for a months-long unattended wall. Looping needs real work after all.

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

## Three findings from `vlc-log-1-default.txt` that the wall could not show

The operator's eyes answered the five questions. VLC's own log answered three we didn't know to ask. **This is why the spike collected the native log rather than trusting observation.**

### 1. `:input-repeat=65535` is a COUNTER, not "forever" — the wall stops after ~22 days

```
repeating the same input (65535)
repeating the same input (65534)
repeating the same input (65533)
```

It counts **down**, and 65535 is the option's maximum (`change_integer_range(0, 65535)` in libvlc-module.c). At 30s per clip that's ~22.7 days of looping, then playback simply ends.

This app is specified to run **unattended for months**. Nothing on a bench, and nothing in a day at the wall, would ever have caught this — the clip would just stop, some Tuesday, three weeks in.

**Task 9 must not rely on the repeat counter.** Handle `EndReached` and restart the media. Keep `:input-repeat` as a belt-and-braces backstop if you like, but the restart is the mechanism.

### 2. Audio is being decoded and played — never intended

```
creating audio output ... using audio output module "mmdevice"
looking for aout stream module matching "any" ... using aout stream module "wasapi"
codec (aac) started ... using audio decoder module "avcodec"
```

The clips carry AAC tracks and VLC decodes and routes them to the sound card on every play. The design says audio is out of scope ("it's an LED wall") — but nothing ever told *VLC* that. Wasted CPU per clip, plus the day someone connects speakers the wall starts talking.

**Task 9: add `:no-audio`.**

### 3. Most of the 290ms is a failed filter negotiation, and is probably avoidable

VLC picks a **D3D11** hardware decoder while the display is **Direct3D9**, then tries to insert the `adjust` filter (our brightness/contrast) into that mismatched chain and fails — exhaustively:

```
A filter to adapt decoder DX11 to display NV12 is needed
Trying to use chroma I422 as middle man ... Too high level of recursion (3)
Failed to create video converter
[repeated for I420/I422/I0AL/I0AB/I0FL/RV32/RV24/BGRA, hundreds of lines]
Failed to compensate for the format changes, removing all filters
trying format dxva2_vld
using hw decoder module "dxva2"            <- what it should have used from the start
Using DXVA2 (AMD Radeon HD 7800 Series) for hardware decoding
Received first picture
```

Every Play runs that futile negotiation, tears down the vout, rebuilds it on DXVA2, and only then shows a picture. That is very likely the bulk of the ~290ms.

**Task 9: try `:avcodec-hw=dxva2`** to skip the D3D11 attempt entirely. Not load-bearing — the two-layer swap hides the gap either way — but it should make the cut faster and cheaper. Measure it; don't assume.

Environment noted from the same log: GPU is an **AMD Radeon HD 7800 Series**; `Direct3D shaders initialization failed` / `cannot load Direct3D9 Shader Library` appear but are harmless (HLSL disabled, playback fine).

## Consequences for the build

1. **Task 9 changes: two layers, hard cut** (chosen by the user over accepting the black, and over adding crossfades). Two stacked VideoViews in the output window. On trigger, load the new clip into the hidden layer; only once it has a picture, swap z-order and stop the outgoing one. The outgoing clip stays on the wall throughout, so the wall never goes black. Cost: ~290ms between trigger and change — accepted, and far better than a dropout.
   Still one clip at a time. No mixer, no crossfade slider, no UI change, no change to the OSC contract.
2. **Default output geometry** = the non-primary display's bounds, not 0,0.
3. **Don't clamp output width to the screen.** Wider-than-screen is a legitimate, deliberate setting.
4. **Restart on `EndReached`** — do not trust `:input-repeat` (finding 1).
5. **`:no-audio`** on every media (finding 2).
6. **Try `:avcodec-hw=dxva2`** and measure the gap with and without (finding 3).
7. **Drop the Win7 *fallbacks*.** Software decode and the `direct3d9`/`directdraw` vout switches were built as insurance and proved unnecessary — the default vout works. Note this is *not* in tension with 6: forcing the DXVA2 **decoder** is a measured optimisation, while `--vout=` and `:avcodec-hw=none` were guesses at a problem that didn't materialise. (The fallbacks live in git history if a future machine needs them.)
8. **Prerequisites are a non-issue on this machine.** .NET 4.8 present (`Release = 0x80eb1` = 528049), `d3dcompiler_47.dll` present, VC++ redist not needed (libvlc is MinGW-built, imports only `msvcrt.dll`).

## What the spike cost, and what it bought

Three review rounds. Two of them caught defects that would have wasted the trip entirely: VLC 2.x logging options that made `libvlc_new` return NULL (the app would have opened a window and done nothing, forever), and a runbook pointing at files that weren't in the package.

The bug that actually hit was neither. Every GroupBox was built with both `Width=880` and `AutoSize=true`; inside a FlowLayoutPanel, AutoSize wins and computes width from children, and a GroupBox's preferred size ignores `Dock`ed children — so every control group collapsed to a sliver and the window was unusable.

It shipped because **nobody ever rendered it**. It was built and reviewed entirely over SSH, where there is no GUI. Three rounds of paranoid review all aimed at VLC; none looked at the thing the operator touches first. The operator found it in ten seconds by looking at the screen.

**Task 10 (the real UI) must not repeat this.** Before any UI ships to that machine, there must be a way to render it and look at it here — screenshot in the VM, or the operator as the render check with a fast loop. A 32KB EXE swap turned out to be a two-minute iteration; the 45MB package was never the bottleneck.
