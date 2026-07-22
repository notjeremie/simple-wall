# Per-clip crop / fit (stretch) mode — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Give each clip a per-clip Fit mode — `Crop` (today's cover-fit, default) or `Stretch` (distort-to-fill) — settable from a toggle in the transport adjustment bar, next to Brightness/Contrast.

**Architecture:** `Fit` is a new enum field on `ClipEntry`, a sibling of `Brightness`/`Contrast`. The engine's crop application becomes mode-aware: `Crop` sets `MediaPlayer.CropGeometry`, `Stretch` sets `MediaPlayer.AspectRatio` to the same output ratio — always clearing the opposite property, because the A/B players are double-buffered and reused across clips. It flows through the engine's single entry point (`Execute` + a new `CommandKind.Fit`), so mouse is the only trigger — no OSC, no scheduler. Persistence rides the existing debounced `SaveSoon()` path; no migration is needed because the enum default (`Crop` = 0) is exactly what an old config's missing field deserializes to.

**Tech Stack:** C# / .NET Framework 4.8, WinForms, LibVLCSharp, Newtonsoft.Json, xUnit. Build & test run on `wallvm` over SSH (the Mac has no toolchain).

**Build/test on the VM (from the worktree):**
```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall\.worktrees\crop-fit && dotnet test -c Release -clp:ErrorsOnly"'
```
A single test class:
```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall\.worktrees\crop-fit && dotnet test -c Release --filter FullyQualifiedName~FitGeometryTests -clp:ErrorsOnly"'
```
Baseline before starting: **238 passing, échec : 0** (`Réussi!` = pass, `Échec` = fail on the French VM).

**Working directory for every step:** `/Users/notjeremie/Documents/Coding/simple-wall/.worktrees/crop-fit` on branch `feature/crop-fit-mode`. Commit there.

---

## Task 1: `FitMode` enum + `ClipEntry.Fit` field

**Files:**
- Modify: `src/SimpleWall/Model/ClipEntry.cs`
- Test: `tests/SimpleWall.Tests/ClipLibraryTests.cs` (new test near the existing `ANewClipEntryHasANeutralLook`, ~line 321)

**Step 1: Write the failing test**

In `ClipLibraryTests.cs`, add:

```csharp
[Fact]
public void ANewClipEntryDefaultsToCrop()
{
    var clip = new ClipEntry();
    Assert.Equal(FitMode.Crop, clip.Fit);
}
```

**Step 2: Run it, expect FAIL** — `FitMode` does not exist (compile error).

Run: `ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall\.worktrees\crop-fit && dotnet test -c Release --filter FullyQualifiedName~ANewClipEntryDefaultsToCrop -clp:ErrorsOnly"'`

**Step 3: Implement**

In `src/SimpleWall/Model/ClipEntry.cs`, add the enum and field (keep it beside `Brightness`/`Contrast`):

```csharp
namespace SimpleWall.Model
{
    /// <summary>
    /// How a clip fills the wall when its aspect ratio does not match. Crop is cover-fit --
    /// scale to fill, centre-crop the overflow, no distortion (the historical behaviour, and the
    /// default so old configs and fresh clips are unchanged). Stretch distorts the picture to
    /// fill the wall exactly -- no bars, nothing cropped, aspect not preserved.
    /// Crop is 0 so a config written before this field deserializes to it.
    /// </summary>
    public enum FitMode { Crop, Stretch }

    public class ClipEntry
    {
        // ... existing NeutralLook, Slot, Path, Brightness, Contrast ...

        /// <summary>
        /// Per-clip fill mode, applied to the wall whenever this clip plays. Like the look, it
        /// belongs to the clip, not the wall. Defaults to <see cref="FitMode.Crop"/> so existing
        /// clips are unchanged and old configs (no such field) deserialize to cover-fit.
        /// </summary>
        public FitMode Fit { get; set; } = FitMode.Crop;
    }
}
```

**Step 4: Run it, expect PASS.**

**Step 5: Commit**

```bash
git add src/SimpleWall/Model/ClipEntry.cs tests/SimpleWall.Tests/ClipLibraryTests.cs
git commit -m "feat: add per-clip FitMode (Crop default) to ClipEntry"
```

---

## Task 2: `Replace` resets Fit to Crop

**Files:**
- Modify: `src/SimpleWall/Model/ClipLibrary.cs` (`Replace`, ~line 136)
- Test: `tests/SimpleWall.Tests/ClipLibraryTests.cs` (beside `ReplaceResetsTheLookToNeutral`, ~line 306)

**Step 1: Write the failing test**

```csharp
[Fact]
public void ReplaceResetsFitToCrop()
{
    var library = new ClipLibrary();
    library.Add("a.mp4");
    var clip = library.BySlot(1);
    clip.Fit = FitMode.Stretch;

    Assert.True(library.Replace(1, "new.mp4"));

    Assert.Equal(FitMode.Crop, library.BySlot(1).Fit);
}
```
(Mirror the exact setup style of `ReplaceResetsTheLookToNeutral` — if `Add` there takes different args, copy that test's arrangement.)

**Step 2: Run it, expect FAIL** — Fit stays `Stretch`.

**Step 3: Implement** — in `Replace`, after the brightness/contrast reset:

```csharp
clip.Brightness = ClipEntry.NeutralLook;
clip.Contrast = ClipEntry.NeutralLook;
clip.Fit = FitMode.Crop;
```
Update the method's doc-comment to mention Fit resets too ("a new video is a new clip").

**Step 4: Run it, expect PASS.**

**Step 5: Commit**

```bash
git add src/SimpleWall/Model/ClipLibrary.cs tests/SimpleWall.Tests/ClipLibraryTests.cs
git commit -m "feat: reset Fit to Crop when a clip's file is replaced"
```

---

## Task 3: Config round-trip — Stretch persists, absent field is Crop

**Files:**
- Test only: `tests/SimpleWall.Tests/ConfigStoreTests.cs` (beside `SaveThenLoadRoundTrips` ~line 63 and `DefaultSlotRoundTripsAndIsZeroWhenAbsent` ~line 79)

No production change — this proves the "no migration needed" claim.

**Step 1: Write the tests**

Mirror the two existing patterns. For persistence, follow `SaveThenLoadRoundTrips` (it saves a `WallConfig` to a temp path via `ConfigStore` and reloads). Add a clip with `Fit = FitMode.Stretch`, round-trip, assert it survives:

```csharp
[Fact]
public void ClipFitRoundTrips()
{
    // arrange a config with one clip, following SaveThenLoadRoundTrips' setup
    // clip.Fit = FitMode.Stretch;  save; reload;
    Assert.Equal(FitMode.Stretch, loaded.Clips[0].Fit);
}
```

For the legacy default, follow `DefaultSlotRoundTripsAndIsZeroWhenAbsent`: write raw JSON for a clip that has `Path`/`Slot` but **no** `Fit` field, deserialize through `ConfigStore`, and assert:

```csharp
[Fact]
public void ClipWithNoFitFieldDeserializesToCrop()
{
    // raw JSON: { "Clips": [ { "Slot": 1, "Path": "a.mp4" } ], ... }
    Assert.Equal(FitMode.Crop, loaded.Clips[0].Fit);
}
```
(Look at how `DefaultSlotRoundTripsAndIsZeroWhenAbsent` gets its raw-JSON-without-the-field onto disk and reuse that mechanism verbatim.)

**Step 2: Run them, expect PASS immediately** (Newtonsoft serializes the enum as its int; absent → default 0 = Crop). If `ClipFitRoundTrips` fails, that's a real serialization surprise — investigate before moving on. If `ClipWithNoFitFieldDeserializesToCrop` fails, the "no migration" assumption is wrong — stop and reconsider.

**Step 3: Commit**

```bash
git add tests/SimpleWall.Tests/ConfigStoreTests.cs
git commit -m "test: Fit round-trips and a fieldless legacy config is Crop"
```

---

## Task 4: Engine — pure `FitGeometry` helper

**Files:**
- Modify: `src/SimpleWall/Engine/VlcWallEngine.cs` (near `CropRatio`, ~line 468)
- Test: `tests/SimpleWall.Tests/FitGeometryTests.cs` (new, mirror `CropRatioTests.cs`)

**Step 1: Write the failing test** — new file `tests/SimpleWall.Tests/FitGeometryTests.cs`:

```csharp
using SimpleWall.Engine;
using SimpleWall.Model;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The crop-vs-stretch decision, pure so it is tested without libvlc. Both properties are
    /// always returned -- one gets the ratio, the OTHER gets null -- because the A/B players are
    /// reused across clips and a stale crop or aspect must be cleared, never left behind.
    /// </summary>
    public class FitGeometryTests
    {
        [Fact]
        public void CropSetsCropGeometryAndClearsAspect()
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(FitMode.Crop, 1664, 256);
            Assert.Equal("13:2", crop);
            Assert.Null(aspect);
        }

        [Fact]
        public void StretchSetsAspectAndClearsCrop()
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(FitMode.Stretch, 1664, 256);
            Assert.Null(crop);
            Assert.Equal("13:2", aspect);
        }

        [Theory]
        [InlineData(FitMode.Crop)]
        [InlineData(FitMode.Stretch)]
        public void ZeroGeometryIsNullNullSoNothingBogusIsPushed(FitMode mode)
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(mode, 0, 256);
            Assert.Null(crop);
            Assert.Null(aspect);
        }
    }
}
```

**Step 2: Run it, expect FAIL** — `FitGeometry` does not exist.

**Step 3: Implement** — in `VlcWallEngine.cs`, next to `CropRatio`:

```csharp
/// <summary>
/// The (CropGeometry, AspectRatio) pair for a clip's fill mode against the output geometry.
/// Crop returns (ratio, null) -- cover-fit, centre-crop, no distortion. Stretch returns
/// (null, ratio) -- forcing AspectRatio to the window's own ratio makes libvlc stretch the
/// source to fill it, distorting. Exactly one property carries the ratio and the other is
/// null so a reused player never keeps a stale crop/stretch from the previous clip. Both null
/// for a not-yet-resolved (zero) geometry -- no bogus string reaches libvlc. Pure and static
/// so the choice is tested without libvlc, same as CropRatio.
/// </summary>
public static (string crop, string aspect) FitGeometry(FitMode mode, int width, int height)
{
    var ratio = CropRatio(width, height);   // null for zero/negative geometry
    return mode == FitMode.Stretch ? (null, ratio) : (ratio, null);
}
```
Add `using SimpleWall.Model;` to the file if not already present.

**Step 4: Run it, expect PASS.**

**Step 5: Commit**

```bash
git add src/SimpleWall/Engine/VlcWallEngine.cs tests/SimpleWall.Tests/FitGeometryTests.cs
git commit -m "feat: pure FitGeometry(mode, w, h) -> (crop, aspect) helper"
```

---

## Task 5: Engine — `ApplyFit` replaces `ApplyCropToFill`, wired per-player

**Files:**
- Modify: `src/SimpleWall/Engine/VlcWallEngine.cs` — `ApplyCropToFill` (~457), `StartLoad` (~259), `ApplyGeometry` (~185–186)

No new unit test (touches the real `MediaPlayer`); covered by `FitGeometry` above and the on-VM render/soak at the end. This is a refactor + rewire step — keep it mechanical.

**Step 1: Replace `ApplyCropToFill` with `ApplyFit`:**

```csharp
/// <summary>
/// Applies a clip's fill mode to one player. Crop (the default, and what a null clip gets)
/// sets CropGeometry to the output ratio and clears AspectRatio -- cover-fit, no distortion.
/// Stretch sets AspectRatio and clears CropGeometry -- distort-to-fill. BOTH are always
/// assigned: the A/B players are reused across clips, so the mode the previous clip left must
/// be cleared, never inherited. Assigning null is libvlc's "no override", safe even at the
/// not-yet-resolved zero geometry. Applied to the back layer before a swap (StartLoad) and
/// re-applied on ApplyGeometry so a geometry change re-fits.
/// </summary>
private void ApplyFit(MediaPlayer player, ClipEntry clip)
{
    var (crop, aspect) = FitGeometry(clip?.Fit ?? FitMode.Crop, _config.OutputWidth, _config.OutputHeight);
    player.CropGeometry = crop;
    player.AspectRatio = aspect;
}
```
Delete the old `ApplyCropToFill`. Keep `CropRatio`/`Gcd` — `FitGeometry` uses them.

**Step 2: Rewire `StartLoad`** (~line 259) — replace:
```csharp
ApplyCropToFill(back);
```
with (the incoming clip is the one being loaded onto `back`):
```csharp
ApplyFit(back, _library.BySlot(slot));
```
Update the nearby comment from "Cover-fit the incoming clip" to "Fit the incoming clip (crop or stretch, per its FitMode)".

**Step 3: Rewire `ApplyGeometry`** (~lines 185–186) — replace the blind:
```csharp
ApplyCropToFill(_playerA);
ApplyCropToFill(_playerB);
```
with per-player, driven by the clip each physical player is showing (front = current clip, back = pending clip if a load is in flight, else null → harmless Crop default):
```csharp
ApplyFit(FrontPlayer, CurrentLookClip);
ApplyFit(BackPlayer, _pendingSlot != null ? _library.BySlot(_pendingSlot.Value) : null);
```
Update the comment to "Re-fit both layers (crop or stretch per each clip's mode) so a geometry change tracks."

> Verify `_pendingSlot` and `CurrentLookClip` are the correct member names in this file before writing (they are used elsewhere in the engine — grep to confirm).

**Step 4: Build + full test run, expect PASS (238 + the new ones), échec : 0.**

Run: `ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall\.worktrees\crop-fit && dotnet test -c Release -clp:ErrorsOnly"'`

**Step 5: Commit**

```bash
git add src/SimpleWall/Engine/VlcWallEngine.cs
git commit -m "feat: mode-aware ApplyFit (crop or stretch) per player, replacing ApplyCropToFill"
```

---

## Task 6: Engine command path — `CommandKind.Fit` + `WallCommand.Fit` + `SetFit`

**Files:**
- Modify: `src/SimpleWall/Engine/WallCommand.cs` (enum + factory)
- Modify: `src/SimpleWall/Engine/VlcWallEngine.cs` (`Execute` switch ~152, new `SetFit` + `FitFromValue` near `SetContrast` ~401)
- Test: `tests/SimpleWall.Tests/WallCommandTests.cs`, and `FitGeometryTests.cs` (or a small `FitFromValueTests`) for the decode

Fit flows through the engine's single entry point exactly like Brightness/Contrast. It is deliberately **not** wired into `OscParser`, so the Stream Deck cannot reach it — app UI only. The enum is carried in `WallCommand.Value` (0 = Crop, 1 = Stretch); `FitFromValue` decodes it defensively.

**Step 1: Write the failing tests**

In `WallCommandTests.cs`:
```csharp
[Fact]
public void FitSetsKindAndEncodesModeInValue()
{
    Assert.Equal(1f, WallCommand.Fit(FitMode.Stretch).Value);
    Assert.Equal(CommandKind.Fit, WallCommand.Fit(FitMode.Stretch).Kind);
    Assert.Equal(0f, WallCommand.Fit(FitMode.Crop).Value);
}
```
(Add `using SimpleWall.Model;`.)

For the decode, in `FitGeometryTests.cs` (or a new small class):
```csharp
[Theory]
[InlineData(0f, FitMode.Crop)]
[InlineData(1f, FitMode.Stretch)]
[InlineData(0.4f, FitMode.Crop)]     // defensive: rounds to nearest valid mode
[InlineData(0.6f, FitMode.Stretch)]
public void FitFromValueDecodesTheMode(float value, FitMode expected)
{
    Assert.Equal(expected, VlcWallEngine.FitFromValue(value));
}
```

**Step 2: Run, expect FAIL** — `CommandKind.Fit`, `WallCommand.Fit`, `FitFromValue` missing.

**Step 3: Implement**

`WallCommand.cs`:
```csharp
public enum CommandKind { PlayClip, Play, Pause, Toggle, Stop, Brightness, Contrast, Fit }
```
```csharp
public static WallCommand Fit(FitMode mode) =>
    new WallCommand { Kind = CommandKind.Fit, Value = (float)mode };
```
(Add `using SimpleWall.Model;`.)

`VlcWallEngine.cs` — add to the `Execute` switch:
```csharp
case CommandKind.Fit: SetFit(command.Value); break;
```
And next to `SetContrast`:
```csharp
/// <summary>
/// Fit belongs to the CLIP, like the look: edits whatever is on the wall and re-fits the front
/// layer live (the back layer, if loading, already holds the incoming clip's own mode).
/// Nothing playing means nothing to edit -- dropped and logged, not stored against the wrong
/// clip. RaiseStateChanged so the UI reflects it; persistence is the UI's job (SaveSoon).
/// </summary>
private void SetFit(float value)
{
    var clip = CurrentLookClip;
    if (clip == null) { _log("Fit change ignored -- no clip on the wall."); return; }
    clip.Fit = FitFromValue(value);
    ApplyFit(FrontPlayer, clip);
    RaiseStateChanged();
}

/// <summary>Decodes the mode carried in WallCommand.Value; only 0/1 are ever sent, but rounds
/// defensively so a stray value can't throw or pick a garbage enum.</summary>
public static FitMode FitFromValue(float value) => value >= 0.5f ? FitMode.Stretch : FitMode.Crop;
```

**Step 4: Run the full suite, expect PASS, échec : 0.**

**Step 5: Commit**

```bash
git add src/SimpleWall/Engine/WallCommand.cs src/SimpleWall/Engine/VlcWallEngine.cs tests/SimpleWall.Tests/WallCommandTests.cs tests/SimpleWall.Tests/FitGeometryTests.cs
git commit -m "feat: CommandKind.Fit through Execute; SetFit re-fits the wall live (no OSC)"
```

---

## Task 7: UI — Crop/Stretch toggle in the adjustment bar

**Files:**
- Modify: `src/SimpleWall/UI/MainForm.cs` — the adjust-bar builder (~382–390), `SyncAdjustFromWall` (~501), fields (~76–87)

No unit test (WinForms wiring); verified by the on-VM render in Task 8. `ClassifyLookChange` is deliberately **not** touched — Fit only ever changes via this combo, and the combo persists it directly with `SaveSoon()`, the same direct-save the slider `Scroll` handler uses. Because `SelectedIndexChanged` fires for programmatic changes too, a `_syncingFit` guard prevents the sync-from-wall from echoing back as a fake user edit.

**Step 1: Add fields** (near `_brightness`/`_contrast`):
```csharp
private ComboBox _fit;
private bool _syncingFit;
```

**Step 2: Build the row** — after the Contrast row (~line 388), add a third row. A `ComboBox` (DropDownList) is the two-state control:
```csharp
_fit = new ComboBox
{
    DropDownStyle = ComboBoxStyle.DropDownList,
    Dock = DockStyle.Left,
    Width = 120,
    ForeColor = Color.FromArgb(220, 220, 226),
    BackColor = Color.FromArgb(48, 48, 54),
    FlatStyle = FlatStyle.Flat,
    Margin = new Padding(0, 6, 6, 0)
};
_fit.Items.AddRange(new object[] { "Crop", "Stretch" });   // index == (int)FitMode
_fit.SelectedIndexChanged += (s, e) =>
{
    if (_syncingFit) return;                  // programmatic sync, not a user action
    if (CurrentClip == null) return;
    Command(WallCommand.Fit((FitMode)_fit.SelectedIndex));   // engine mutates clip + re-fits live
    SaveSoon();                               // persist, like the slider Scroll handler
};

bar.Controls.Add(new Label
{
    Text = "Fit:",
    AutoSize = true,
    ForeColor = Color.FromArgb(200, 200, 206),
    Anchor = AnchorStyles.Left,
    Margin = new Padding(0, 8, 6, 0)
}, 0, 3);
bar.Controls.Add(_fit, 1, 3);
_adjustControls.Add(_fit);                    // greys out when no clip is on the wall
```
(If the `TableLayoutPanel` `bar` declares a fixed `RowCount`, bump it by one. Check the panel construction just above the transport row.)

Add `using SimpleWall.Model;` to `MainForm.cs` if `FitMode` is not already visible.

**Step 3: Sync from wall** — in `SyncAdjustFromWall` (after the two `SyncSlider` calls):
```csharp
_syncingFit = true;
_fit.SelectedIndex = (int)(CurrentClip?.Fit ?? FitMode.Crop);
_syncingFit = false;
```

**Step 4: Build, expect clean compile + full suite PASS, échec : 0.**

Run: `ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall\.worktrees\crop-fit && dotnet test -c Release -clp:ErrorsOnly"'`

**Step 5: Commit**

```bash
git add src/SimpleWall/UI/MainForm.cs
git commit -m "feat: Crop/Stretch toggle in the adjustment bar, per-clip and persisted"
```

---

## Task 8: Render the adjustment bar to confirm the toggle appears

**Files:** none (verification). Uses `tools/RenderShot` (see the STATUS note "RenderShot closes the render gap"): render the MainForm — or a fixture exercising the adjust bar with a clip loaded — to a PNG over SSH, so the new "Fit:" row is confirmed visually without a desktop session.

**Step 1:** Check `tools/RenderShot` for how existing fixtures build MainForm / the transport bar. If a fixture already renders the adjust bar, extend it; otherwise render the smallest control tree that includes the adjust bar with a clip "on the wall" (so `_adjustControls` are enabled and `_fit` shows a selection).

**Step 2:** Run RenderShot on the VM per its README; pull the PNG back and **Read it** to confirm: the "Fit:" label and a Crop/Stretch dropdown sit below Contrast, enabled, showing the clip's mode.

**Step 3:** If it renders correctly, note it in the STATUS session log. No commit unless a fixture was added — then:
```bash
git add tools/RenderShot/...
git commit -m "test: RenderShot fixture covering the Fit toggle row"
```

---

## Task 9: Update STATUS + finish the branch

**Files:**
- Modify: `docs/plans/STATUS.md` — add a short Session entry: per-clip Fit (Crop default / Stretch), app-UI-only, engine `ApplyFit` clears the opposite property for reused players, no migration (enum default = old behavior). Bump the test count.

Then use **superpowers:finishing-a-development-branch** to merge `feature/crop-fit-mode` back and clean up the worktree.

**Note — real-hardware caveat (from the STATUS "verify on hardware" lesson):** Stretch actually distorting the picture is a libvlc `AspectRatio` behavior that the GPU-less build VM cannot fully prove renders identically on the real wall GPU. Flag in STATUS that Stretch should be eyeballed on real hardware (the studio Win10 machines) before it's trusted in production — the unit tests prove the *decision*, not the *pixels*.

---

## Out of scope (YAGNI)
- No OSC / Stream Deck control (no `OscParser` change, no reply field).
- No letterbox ("contain") mode — only Crop and Stretch.
- No per-clip custom crop rectangle.
- No `ConfigMigration` change — the enum default makes old configs load as Crop with zero behavior change.
