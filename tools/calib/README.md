# Calibration pattern generators

Content-authoring aids for the corner LED wall. **Not app code** — nothing here
ships with SimpleWall or is touched by the test suite. These exist so the
patterns can be regenerated if the wall geometry is ever remeasured.

The wall is `1664x256`, three physical faces meeting at two 90-degree creases.
The crease positions were measured on the physical LED over three passes
(175/1475 → 190/1445 → **191/1440**, confirmed 2026-07-20):

| Face         | Range     | Width  |
|--------------|-----------|--------|
| LEFT RETURN  | 0–191     | 191px  |
| FRONT FACE   | 191–1440  | 1249px (safe zone) |
| RIGHT RETURN | 1440–1664 | 224px  |

The orange fold markers are 2px wide and trimmed toward the front face —
LEFT covers columns 191–192, RIGHT covers 1439–1440. A symmetric 3px line
straddles the crease unevenly and reads as misaligned on the hardware.

## The two patterns

- **`calib8-gfx.py`** — the **GFX department handoff sheet**. This is the file to
  send artists. Magenta 1px fit-check border touching all 4 edges, ruler every
  200px, exact face ranges, red keep-clear bands over each fold.
- **`calib7-folds.py`** — the **on-wall verification pattern**. Denser labelling,
  128px tile ruler. Use this to re-check that the fold lines still land on the
  physical creases after any wall or display-geometry change.

## Regenerating

Needs Python with Pillow, and ffmpeg. Fonts are macOS Arial paths.

```sh
python3 calib8-gfx.py
ffmpeg -y -loglevel error -loop 1 -i calib8-gfx-1664x256.png \
  -t 10 -r 25 -c:v libx264 -pix_fmt yuv420p -vf "scale=1664:256" \
  calib8-gfx-1664x256.mp4
```

Staged output lives in `dist/hotfix/`, which is gitignored — the generated
`.png`/`.mp4` are deliberately not tracked, only these generators are.

If you change a fold position, change it in **both** scripts: each has its own
`LEFT_FOLD` / `RIGHT_FOLD` constants at the top.
