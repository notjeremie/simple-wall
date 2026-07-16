# RenderShot — look at a window without a desktop

Renders a WinForms `Form` to a PNG and prints its layout tree, over SSH, with no
interactive session. It exists because the spike's control window shipped with every
GroupBox collapsed to a 16-pixel sliver: it was built and reviewed entirely over SSH,
three rounds of review scrutinised VLC, and **nobody could look at the screen**. The
user found it in ten seconds by glancing at the monitor.

## Use it

```bash
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && dotnet build tools\RenderShot\RenderShot.csproj"'
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && tools\RenderShot\bin\Debug\net48\RenderShot.exe SimpleWall.Ui.ControlForm artifacts\render\control.png"'
```

`artifacts/` is on the Mac share and gitignored, so the PNG lands where you can open it
directly — no copying back.

Exit codes: `0` fine, `1` threw, `2` bad arguments, `3` **a control collapsed to nothing**.

## Then actually look at the PNG

The exit code only catches total collapse. It cannot tell you the window is ugly,
mislabelled, or unusable. Open the image.

## The one rule this imposes: build the UI in the constructor

**Anything a form adds to its control tree in `Load` is invisible to RenderShot, and RenderShot
cannot tell.** It would print a clean tree and a healthy-looking PNG of a window missing half its
controls — false confidence, which is worse than no check. `Load` is for hardware (VLC), never for
layout. This is a good rule anyway: it's what makes a window checkable without a video card.

## How it works, and why that way

It never shows the form and never fires `Load`. Reading `Handle` creates the HWND but
skips `OnCreateControl`, which is what would raise `Form.OnLoad`. That is deliberate:
`Load` is where the real windows start VLC, and a layout check that needs a video card
is a layout check nobody will run. Children are then created explicitly, overriding the
`Visible == false` they inherit from a parent that is never shown.

The layout tree is printed next to the picture because a control missing from the PNG
has two very different explanations, and only the numbers separate them: laid out at
zero size (a bug) versus laid out correctly and never painted (fine).

## Known fidelity limits — not bugs, don't chase them

- **Child 3D borders don't paint.** `DrawToBitmap` sends `WM_PRINT` with client+children
  but not non-client, and a TextBox's sunken border is non-client. Text fields appear as
  bare white rectangles.
- **An empty `ReadOnly` TextBox renders as nothing.** Its background is `SystemColors.Control`,
  the same grey as the form, and per the above it has no visible border. The spike's log
  pane looks absent in `spike.png`; the layout dump proves it is 902x193 and healthy.
- **No title bar** — non-client again.
- **`FormClosing`/`FormClosed` never fire**, because the form is disposed rather than closed. That's
  symmetric with never firing `Load`: nothing was started, so nothing needs tearing down.
- **An `AutoSize` control with no text and no children is not reported as collapsed**, because
  measuring zero is correct for it. A check that cries wolf gets ignored on the day it's right.

## It is validated against the real bug

Don't trust a harness that has only ever rendered working windows. This one is checked
against the known-bad input:

```bash
git show cea172f^:src/SimpleWall/Spike/SpikeForm.cs > src/SimpleWall/Spike/SpikeForm.cs   # pre-fix
# build + render  -> "9 control(s) collapsed to nothing", EXIT=3, slivers plainly visible
git checkout src/SimpleWall/Spike/SpikeForm.cs                                            # restore
```

`cea172f` is "Fix spike control window collapsing to unusable slivers". Its parent is the
version the user caught. If you change how RenderShot realises a form, re-run that and
confirm it still fails — a check that cannot fail is worthless.
