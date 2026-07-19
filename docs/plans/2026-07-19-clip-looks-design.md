# Clip looks — per-clip brightness & contrast

**Date:** 2026-07-19 (session 5)
**Status:** design agreed, implementing into the v1.0 candidate build

## The idea

Brightness and contrast become a property **of the clip**, not a global wall
setting. Whenever a clip plays — from the Stream Deck, the mouse, the scheduler,
or the boot default — it comes up at its own saved look. This is what the
operator asked for: *"when I change clip with the Stream Deck, the brightness and
contrast settings are already applied."*

A happy consequence: the original request (a scheduled "play clip" event that
carries brightness/contrast) **needs no new scheduler fields**. A scheduled
`play clip 3` automatically shows clip 3 at clip 3's look.

## The model

- `ClipEntry` gains `Brightness` and `Contrast`, both defaulting to `1.0`
  (neutral — existing clips look identical).
- **Setting a look is just adjusting it while it plays.** The transport sliders,
  the Stream Deck faders, and any scheduled brightness/contrast command all edit
  the **currently-playing clip's** look and persist it (debounced). *What I see
  is what's saved.*
- There is no longer a global wall brightness. The value that used to be global
  is now per-clip.

## Where each piece changes

### `Model/ClipEntry.cs`
Add `public float Brightness { get; set; } = 1.0f;` and `Contrast` likewise.

### `ClipLibrary.Replace` — reset the look on a file swap
Replacing a clip's video file is a *new* clip in the same slot, so it must not
inherit the old video's dimming. `Replace(slot, newPath)` resets that clip's
`Brightness`/`Contrast` back to neutral. If the replaced clip is the one on the
wall, the existing re-play (Stop→play through `StartLoad`) applies the now-neutral
look and the sliders refresh to neutral automatically.

### `Engine/VlcWallEngine.cs`
- `ApplyAdjust(player)` → `ApplyAdjust(player, ClipEntry clip)`: reads the clip's
  look (neutral when `clip == null`). Constructor calls it with `null` (no clip
  loaded yet).
- `StartLoad(slot, path)` applies the **incoming** clip's look to the back layer
  before the swap (same after-Play timing as today's `ApplyAdjust`/crop) — so the
  cut is clean, no flash at the wrong value. `EndReached` restart inherits this
  for free (it calls `StartLoad`).
- `SetBrightness`/`SetContrast` now edit `_library.BySlot(CurrentSlot).Brightness`
  (through `AdjustValue.Clamp`), apply to the **front** player only, and
  `RaiseStateChanged`. When nothing is playing, no-op + log (you can't set a look
  on nothing). They no longer write `_config.Brightness/Contrast`.

### `UI/MainForm.cs`
- Sliders reflect the **current clip's** look on `StateChanged`, via the library,
  not `_config`. When no clip is loaded, the sliders disable (like Play/Stop) and
  read neutral.
- Save-change detection becomes **slot-aware**: persist only when the *same*
  clip's look changed (an OSC/scheduler edit), never on a mere clip switch — so a
  switch doesn't spuriously rewrite config.json.

### Migration — `Program.cs` (composition root, after `store.Load()`)
One-time, pure, testable seed: if the old global `config.Brightness`/`Contrast`
is non-neutral, copy it into every clip's look, then reset the global to `1.0`.
On first upgrade every clip is still at default `1.0`, so this exactly preserves
the wall's current appearance; on every later load the global is `1.0` and the
step is a no-op. `WallConfig.Brightness/Contrast` stay as the migration seed
(vestigial thereafter — documented as such, not removed, so old configs migrate).

### Scheduler
`play clip` events are unchanged and now carry the look automatically. The
existing standalone `Brightness`/`Contrast` scheduled commands still work and
behave consistently (they edit the current clip's look when they fire). Left in
for now; can be hidden from the editor later if they prove confusing.

## Tests
- `ClipEntry` defaults to neutral (pure).
- Migration seeds clips from a non-neutral global and no-ops on a neutral one (pure).
- Engine (VM/libvlc): playing a clip applies its look; `Execute(Brightness)`
  writes the current clip's look and is ignored when nothing plays.
- MainForm slot-aware save: a clip switch does not trigger a save; an edit does.

## Deliberately NOT doing
- No per-trigger override layer, no global master dimmer on top of clip looks
  (YAGNI — the operator wanted looks to live on the clip, full stop).
- No new scheduler fields.

## Review outcome (two changes the review forced)
A code review found two real issues before commit — both in the *composition*, as ever:

1. **The OSC reply feedback (`OscReplySender`) still read the now-frozen global**
   `_config.Brightness/Contrast`. It fires on every `StateChanged`, so the Stream
   Deck fader feedback would have reported a neutral wall while the actual clip
   was dimmed — permanently, after the migration reset the global. Fixed by adding
   `CurrentBrightness`/`CurrentContrast` to `IWallEngine` (the clip's look, clamped,
   neutral when nothing plays) and reading those. The reply still clamps at the
   network boundary. Pinned by `ReplyReportsTheClipLookFromTheEngineNotTheConfigGlobal`.
2. **A failed config save followed by a clip switch dropped the pending edit.** The
   old global design retried on the next state change; the slot-aware baseline
   would have discarded the divergence on the first switch. Fixed with a
   `_configDirty` flag (orthogonal to the slot baseline): an edit dirties it, only a
   successful save clears it, and every state change re-arms the debounce while
   dirty. The decision is the pure `MainForm.PendingSaveAfter`, tested for the
   failed-save-then-switch retry.
</content>
