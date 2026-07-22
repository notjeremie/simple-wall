# Per-clip crop / fit (stretch) mode — design

**Date:** 2026-07-22
**Status:** designed, ready for implementation plan

## Problem

The wall always cover-fit **crops**: `MediaPlayer.CropGeometry` is set to the output's
aspect ratio (`CropRatio(OutputWidth, OutputHeight)` → e.g. `"13:2"` for 1664×256), so a
clip whose dimensions don't match the wall fills it and the overflow (usually left/right)
is cropped off, no distortion. It's applied globally to every player from the output
geometry — there is no per-clip choice.

The operator wants a per-clip choice, in continuity with per-clip brightness/contrast:
keep **Crop** (today's cover-fit) or pick **Stretch** — distort the clip to fill the whole
wall exactly (no bars, nothing cut off, aspect not preserved; circles become ovals).

Decisions taken during brainstorming:
- "Fit" means **Stretch / squish** (distort to fill), not letterbox.
- **App UI only** — no OSC command. It's a set-once-per-clip authoring decision.
- Control lives **in the transport adjustment bar**, next to Brightness/Contrast.

## Model

`ClipEntry` (src/SimpleWall/Model/ClipEntry.cs) gains a sibling to `Brightness`/`Contrast`:

```csharp
public enum FitMode { Crop, Stretch }   // Crop = 0 = default

public FitMode Fit { get; set; } = FitMode.Crop;
```

`Crop` is the enum default (`0`), so:
- every existing clip is unchanged,
- an old config written by v1.0 (no `Fit` field) deserializes to `Crop` — exactly today's
  cover-fit behavior, zero visual change.

`ClipLibrary.Replace` resets `Fit = FitMode.Crop` alongside the existing brightness/contrast
reset (a replaced file is a fresh clip).

## Engine

Today `ApplyCropToFill(MediaPlayer player)` blindly sets `CropGeometry` to the output ratio.
Replace it with a **mode-aware** `ApplyFit(MediaPlayer player, ClipEntry clip)`:

- `Crop`    → `player.CropGeometry = ratio; player.AspectRatio = null;`  (today's behavior)
- `Stretch` → `player.AspectRatio = ratio; player.CropGeometry = null;`  (distort-to-fill,
  the mechanism the existing code comment at VlcWallEngine.cs:452 already documents)

`ratio` is the existing `CropRatio(OutputWidth, OutputHeight)`. Forcing `AspectRatio` to the
output window's own ratio makes libvlc stretch the source to fill the window.

**Both properties are always set** (one to the value, one to `null`). The A/B players are
double-buffered and reused across clips, so a stretch clip following a crop clip on the same
player must clear the stale `CropGeometry`, and vice versa. Never set one without clearing
the other.

Pure helper (unit-testable without libvlc), keeping company with `CropRatio`:

```csharp
// returns which property receives the ratio and which receives null
public static (string crop, string aspect) FitGeometry(FitMode mode, int w, int h)
{
    var ratio = CropRatio(w, h);                 // null for zero geometry
    return mode == FitMode.Stretch ? (null, ratio) : (ratio, null);
}
```

`ApplyFit` becomes a thin wrapper that calls `FitGeometry` and assigns the two properties.

Call sites:
- `StartLoad` (VlcWallEngine.cs:259) — pass the incoming clip so the cut lands in the right
  mode, same after-`Play` timing as `ApplyAdjust`.
- `ApplyGeometry` (VlcWallEngine.cs:185–186) — re-apply **per player** using the front
  player's current clip and the back player's pending clip (if any), not a blind crop of both.

## UI — adjustment bar

Add a third row under Contrast in the transport adjustment bar (MainForm.cs, `AddAdjustRow`
area): a two-state `ComboBox` (DropDownList: "Crop" / "Stretch") reflecting the clip on the
wall. Disabled when no clip is loaded.

On change:
1. write `CurrentClip.Fit`,
2. tell the engine to re-apply fit to the front player live (immediate feedback),
3. reuse the existing debounced per-clip save path.

Fold `Fit` into the same look bookkeeping brightness/contrast already use
(`SaveAdjustSoonIfChanged`, `ClassifyLookChange`, `SyncAdjustFromWall`, the `_saved*`
snapshot fields) so a `Fit` change is persist-worthy and switching clips syncs the control.

## Persistence / migration

`Fit` serializes with the rest of `ClipEntry` (JSON). `ConfigMigration` seeds
`Fit = FitMode.Crop` on any legacy entry that lacks it, matching how it seeds neutral
brightness/contrast — a v1.0 config loads with no behavior change.

## Tests (TDD)

Mirroring the existing per-clip look tests:
- `FitGeometry` returns crop-and-null for `Crop`, null-and-aspect for `Stretch`, and
  `(null, null)` for zero geometry.
- `ClipLibrary.Replace` resets `Fit` to `Crop`.
- `ConfigMigration` seeds `Fit` on legacy entries lacking it.
- `ClassifyLookChange` / debounce treats a `Fit` change as a persist-worthy look change.
- Round-trip: a clip with `Fit = Stretch` serializes and deserializes intact; an old JSON
  without the field deserializes to `Crop`.

## Out of scope (YAGNI)

- No OSC command / Stream Deck control.
- No letterbox ("contain") mode — only Crop and Stretch.
- No per-clip custom crop rectangle — the ratio is always the output's.
