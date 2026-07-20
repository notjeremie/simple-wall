# Contributing

## Windows only — read this first

SimpleWall is .NET Framework 4.8 WinForms, x64, built against native LibVLC
binaries. It does not build or run on macOS or Linux. If you're on a Mac or a
Linux box, you'll need a Windows VM (or a spare Windows machine) before you
can do anything beyond reading the code. There's no cross-platform shim
planned — the target is a Windows 7 PC bolted to a video wall, and that's not
changing.

If you don't have Windows available, you can still contribute (see "Good
first areas" below), but expect to be limited to changes you can reason about
from the pure-logic tests without ever running the app.

## Build and test

On Windows, with .NET SDK **8.0.423** installed (pinned in `global.json`,
`rollForward: latestFeature` so a later 8.0.4xx patch is fine):

```
dotnet build
dotnet test
```

`dotnet build` builds the whole solution from the repo root. `dotnet test`
runs all 229 xunit tests, including `LibVlcContractTests`, which load real
LibVLC with `--vout=dummy` — this exercises actual playback behavior with no
GPU or video card required, so it works fine in a VM. It does mean `dotnet
test` needs the LibVLC native binaries restored (NuGet handles this) and will
be slower than a pure-logic test run.

Tests must pass on Windows before a PR is reviewed. There's no CI — the tests
get run on Windows by hand, so a PR that hasn't been through `dotnet test`
just moves that work onto someone else.

## Code style

Match the surrounding code. A few things that matter more than usual here:

- **Comments explain WHY, not WHAT.** Look at
  `src/SimpleWall/Model/WallConfig.cs` for the house style — comments there
  exist to record a decision and the failure mode it prevents (e.g. why
  `OutputWidth`/`OutputHeight` must not default to a plausible-looking
  1920×256, why `DefaultSlot` is a slot number and not a clip reference).
  If you make a non-obvious choice — especially anything driven by the
  hardware, by a bug you hit, or by a tradeoff you rejected — write down the
  reasoning next to the code, not just what the code does.
- Keep I/O and hardware-touching code separate from logic that can be
  reasoned about in isolation. See "Testing expectations" below — this
  separation is what makes the second one possible.
- Don't restructure files you're not otherwise touching. Small, reviewable
  diffs.

## Testing expectations

New logic should come with tests. Before reaching for hardware or a UI
harness, ask whether the thing you're adding can be extracted as a pure
function — something that takes inputs and returns outputs with no
`MediaPlayer`, no `Control`, no filesystem. `SwapPolicy`, `OscParser`, and
`Scheduler`'s tick logic are the existing examples: they used to be tangled
into I/O-heavy classes and got pulled out specifically so they could be unit
tested without a video card or a wall.

If what you're changing genuinely can't be pulled out (a WinForms control's
visual state, for instance), `tools/RenderShot` renders a `Form` to a PNG over
SSH with no desktop session — build the control tree in the constructor
(not `Load`) so it can be rendered non-interactively, and make sure the
fixture actually shows the interesting state, not just the default one.

## PR process

1. Branch off `main`.
2. Make the change, with tests.
3. `dotnet test` passes.
4. Open the PR and describe **what you verified and how** — "ran the unit
   tests" and "tested on the real wall" are different claims; say which one
   you're making. If you couldn't test something (no hardware access), say
   that too, plainly.

## Good first areas

Easier, hardware-independent:
- Pure logic in `Engine/SwapPolicy`, `Osc/OscParser`, `Scheduling/` — testable
  entirely with xunit, no wall needed.
- `tools/calib/` pattern generators.
- Docs, error messages, config validation edge cases.

Harder without a wall:
- Anything touching `VlcWallEngine`, the actual swap timing, or `UI/` visual
  behavior. You can write and test the logic, and use RenderShot for static
  UI states, but you can't verify swap latency, color/brightness behavior on
  real LEDs, or OSC round-trips with a real Stream Deck without physical
  access. Be upfront in the PR about what's untested if you're in this
  territory — it'll get tested on the real wall before it ships either way.
