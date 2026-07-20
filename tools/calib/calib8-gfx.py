#!/usr/bin/env python3
"""
GFX department handoff sheet for the corner LED wall. 1664x256.

Combines the calib3 fit-check (magenta border touching all 4 edges) with the
confirmed fold geometry: LEFT crease at x=191, RIGHT crease at x=1440.
Ruler every 200px. This is the spec artists design against.
"""
from PIL import Image, ImageDraw, ImageFont

W, H = 1664, 256
LEFT_FOLD = 191
RIGHT_FOLD = 1440
STEP = 200            # ruler interval
BAND = 36             # +/- px "keep clear" zone around each fold

BG      = (8, 8, 10)
FRONT   = (17, 21, 30)
RETURN  = (28, 17, 17)
MAGENTA = (255, 0, 255)
ORANGE  = (255, 140, 0)
RED     = (220, 40, 40)
GREY    = (95, 101, 110)
DIM     = (60, 65, 72)
WHITE   = (238, 241, 245)

AB = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
A  = "/System/Library/Fonts/Supplemental/Arial.ttf"
f_title = ImageFont.truetype(AB, 26)
f_face  = ImageFont.truetype(AB, 20)
f_num   = ImageFont.truetype(AB, 20)
f_small = ImageFont.truetype(A, 14)
f_tick  = ImageFont.truetype(A, 12)

img = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(img, "RGBA")


def centered(text, cx, y, font, fill):
    l, t, r, b = d.textbbox((0, 0), text, font=font)
    d.text((cx - (r - l) / 2, y), text, font=font, fill=fill)


# --- face fills ---
d.rectangle([0, 0, LEFT_FOLD, H], fill=RETURN)
d.rectangle([LEFT_FOLD, 0, RIGHT_FOLD, H], fill=FRONT)
d.rectangle([RIGHT_FOLD, 0, W, H], fill=RETURN)

# --- ruler every 200px: full-height hairline + label top and bottom ---
for x in range(STEP, W, STEP):
    d.line([(x, 22), (x, H - 22)], fill=(*DIM, 130), width=1)
    # skip the label where a fold marker already owns that column
    if min(abs(x - LEFT_FOLD), abs(x - RIGHT_FOLD)) < 55:
        continue
    centered(str(x), x, 5, f_tick, GREY)
    centered(str(x), x, H - 17, f_tick, GREY)

# --- keep-clear bands ---
for fold in (LEFT_FOLD, RIGHT_FOLD):
    d.rectangle([fold - BAND, 0, fold + BAND, H], fill=(*RED, 50))

# --- fold lines: 2px, trimmed toward the front face ---
for fold, x0 in ((LEFT_FOLD, LEFT_FOLD), (RIGHT_FOLD, RIGHT_FOLD - 1)):
    d.rectangle([x0, 0, x0 + 1, H - 1], fill=ORANGE)
    centered(f"{fold}", fold, 4, f_num, ORANGE)
    centered("FOLD", fold, 26, f_small, ORANGE)
    centered("KEEP CLEAR", fold, H - 36, f_small, RED)

# --- title block, centred on the FRONT face ---
cx = (LEFT_FOLD + RIGHT_FOLD) / 2
centered("LED WALL  1664 x 256", cx, 62, f_title, WHITE)
centered("magenta border must touch all 4 edges  •  ruler every 200px",
         cx, 96, f_small, GREY)

# --- face labels ---
centered("FRONT FACE", cx, 138, f_face, WHITE)
centered(f"{LEFT_FOLD}–{RIGHT_FOLD}   ({RIGHT_FOLD - LEFT_FOLD}px)  safe zone",
         cx, 164, f_small, GREY)

for x0, x1, name in ((0, LEFT_FOLD, "LEFT"), (RIGHT_FOLD, W, "RIGHT")):
    mid = (x0 + x1) / 2
    centered(name, mid, 112, f_small, WHITE)
    centered("RETURN", mid, 130, f_small, WHITE)
    centered(f"{x0}–{x1}", mid, 152, f_tick, GREY)
    centered(f"{x1 - x0}px", mid, 168, f_tick, GREY)

# --- magenta fit-check border, 1px on all four edges ---
d.rectangle([0, 0, W - 1, H - 1], outline=MAGENTA, width=1)

png = "calib8-gfx-1664x256.png"
img.save(png)
print("wrote", png, img.size, "folds", LEFT_FOLD, RIGHT_FOLD)
