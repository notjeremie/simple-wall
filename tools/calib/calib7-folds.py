#!/usr/bin/env python3
"""
Final fold calibration / authoring reference for the corner LED wall.
1664x256. Three physical faces meeting at two 90-degree creases the operator
read off the wall: LEFT fold at x=191, RIGHT fold at x=1440
(calib6 pass at 190/1445 was 1px off left, 5px off right).

This is the CLEAN authoring reference (not the read-the-crease dense-ruler
version): labelled faces, red "keep clear" bands over each fold, and a 128px
tile ruler so content authors can place logos/faces away from the corners.
"""
from PIL import Image, ImageDraw, ImageFont

W, H = 1664, 256
LEFT_FOLD = 191
RIGHT_FOLD = 1440
TILE = 128            # physical LED tile = 128px; wall is 13x2 tiles
BAND = 36             # +/- px "keep clear" zone painted over each fold

BG      = (10, 10, 12)
FRONT   = (16, 20, 28)      # subtle fill so FRONT face reads as the safe zone
RETURN  = (26, 16, 16)      # side returns tinted so they read as "edges"
ORANGE  = (255, 140, 0)
RED     = (220, 40, 40)
GREY    = (90, 96, 104)
WHITE   = (235, 238, 242)

AB = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
A  = "/System/Library/Fonts/Supplemental/Arial.ttf"
f_face  = ImageFont.truetype(AB, 30)
f_num   = ImageFont.truetype(AB, 26)
f_small = ImageFont.truetype(A, 16)
f_tick  = ImageFont.truetype(A, 13)

img = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(img, "RGBA")

# --- face fills (safe front vs side returns) ---
d.rectangle([0, 0, LEFT_FOLD, H], fill=RETURN)
d.rectangle([LEFT_FOLD, 0, RIGHT_FOLD, H], fill=FRONT)
d.rectangle([RIGHT_FOLD, 0, W, H], fill=RETURN)

# --- 128px tile grid (seams) ---
for x in range(TILE, W, TILE):
    d.line([(x, 0), (x, H)], fill=(*GREY, 70), width=1)
d.line([(0, H // 2), (W, H // 2)], fill=(*GREY, 70), width=1)  # the one horizontal seam

# --- "keep clear" red bands over each fold ---
for fold in (LEFT_FOLD, RIGHT_FOLD):
    d.rectangle([fold - BAND, 0, fold + BAND, H], fill=(*RED, 55))

# --- fold lines + exact numbers ---
def centered(text, cx, y, font, fill):
    l, t, r, b = d.textbbox((0, 0), text, font=font)
    d.text((cx - (r - l) / 2, y), text, font=font, fill=fill)

# 2px lines, trimmed toward the front face: LEFT covers [191,192],
# RIGHT covers [1439,1440] (calib7 v1 was a symmetric 3px line).
for fold, x0 in ((LEFT_FOLD, LEFT_FOLD), (RIGHT_FOLD, RIGHT_FOLD - 1)):
    d.rectangle([x0, 0, x0 + 1, H - 1], fill=ORANGE)
    centered(f"{fold}", fold, 6, f_num, ORANGE)
    centered("FOLD", fold, 34, f_small, ORANGE)
    centered("KEEP LOGOS", fold, H - 44, f_small, RED)
    centered("CLEAR", fold, H - 26, f_small, RED)

# --- face labels ---
centered("LEFT",   LEFT_FOLD / 2,               H // 2 - 30, f_small, WHITE)
centered("RETURN", LEFT_FOLD / 2,               H // 2 - 8,  f_small, WHITE)
centered(f"{LEFT_FOLD}px", LEFT_FOLD / 2,        H // 2 + 16, f_tick,  GREY)

centered("FRONT FACE", (LEFT_FOLD + RIGHT_FOLD) / 2, H // 2 - 18, f_face, WHITE)
centered(f"{RIGHT_FOLD - LEFT_FOLD}px wide  •  safe zone",
         (LEFT_FOLD + RIGHT_FOLD) / 2, H // 2 + 20, f_small, GREY)

centered("RIGHT",  (RIGHT_FOLD + W) / 2,         H // 2 - 30, f_small, WHITE)
centered("RETURN", (RIGHT_FOLD + W) / 2,         H // 2 - 8,  f_small, WHITE)
centered(f"{W - RIGHT_FOLD}px", (RIGHT_FOLD + W) / 2, H // 2 + 16, f_tick, GREY)

# --- ruler labels every 128 along the very top edge of the front area ---
for x in range(0, W + 1, TILE):
    if abs(x - LEFT_FOLD) > 50 and abs(x - RIGHT_FOLD) > 50:
        centered(str(x), min(max(x, 12), W - 12), H - 20, f_tick, GREY)

# --- outer border so the panel edges are visible ---
d.rectangle([0, 0, W - 1, H - 1], outline=(*GREY, 160), width=2)

png = "calib7-folds-1664x256.png"
img.save(png)
print("wrote", png, img.size, "folds", LEFT_FOLD, RIGHT_FOLD)
