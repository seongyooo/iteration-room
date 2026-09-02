"""itch.io cover art, 630x500, from the title screen's own picture and type.

Nothing here invents a composition. The source frame is MenuBackground.png, which SceneBuilder
renders every build and which is the one composition in this project that has been looked at and
kept; the type is the title screen's, at its own weight, colour and letter-spacing, scaled by the
same factor as the picture. So the cover and the first screen of the game agree by construction.
"""
import io
import os
from PIL import Image, ImageDraw, ImageFont

REPO = r"C:\Users\seonl\Desktop\c\2026\summer\Iteration"
OUT = os.path.join(REPO, "Build")

# itch.io's cover spec. Displayed at half this in browse grids, so anything on it has to
# survive 315x250.
W, H = 630, 500

src = Image.open(os.path.join(REPO, "Assets", "Textures", "MenuBackground.png")).convert("RGB")
sw, sh = src.size                                   # 1920 x 1080

# Crop to the cover's aspect, full height, centred on the doorway (which is centred in the
# source). 1080 * 630/500 = 1360.
cw = int(round(sh * W / H))
left = (sw - cw) // 2
crop = src.crop((left, 0, left + cw, sh))
scale = W / cw                                      # 0.463

pic = crop.resize((W, H), Image.LANCZOS)

# The title screen's own values, scaled by the same factor as the picture.
INK = (28, 28, 33)                                  # MenuInk (0.11, 0.11, 0.13)
# THE MENU'S OWN SIZES, SCALED BY THE SAME FACTOR AS THE PICTURE. Nothing here is chosen.
#
# A heavier, larger setting was tried first, because a cover is shown at 315x250 in a browse grid
# and at that size Light 24px ROOM drops out entirely. It was reverted (2026-09-02, by request):
# ROOM disappearing at grid size is acceptable, and a plain untitled picture is acceptable too -
# so the reason for the cover to have its own type scale went away, and with it the special case.
# `cover_plain.png` is written beside the titled one for exactly that reason.
title_px = max(1, int(round(86 * scale)))           # 40
room_px = max(1, int(round(52 * scale)))            # 24

font_dir = os.path.join(REPO, "Assets", "Fonts", "JetBrains_Mono", "static")
title_font = ImageFont.truetype(os.path.join(font_dir, "JetBrainsMono-Light.ttf"), title_px)
room_font = ImageFont.truetype(os.path.join(font_dir, "JetBrainsMono-Light.ttf"), room_px)

lettered = Image.new("RGB", (W, H))
lettered.paste(pic, (0, 0))
d = ImageDraw.Draw(lettered)

# LEFT-HUNG, like the menu: the place is the subject and the words sit beside it. The margin is
# the cover's own, not the menu's - a 1920 screen's 120px gutter is a quarter of this image.
margin = 34
y = 40
d.text((margin, y), "I T E R A T I O N", font=title_font, fill=INK)
y += int(title_px * 1.35)
d.text((margin, y), "R  O  O  M", font=room_font, fill=INK)

lettered.save(os.path.join(OUT, "cover_titled.png"))
pic.save(os.path.join(OUT, "cover_plain.png"))

# What a browse grid actually shows.
half = lettered.resize((W // 2, H // 2), Image.LANCZOS)
half.save(os.path.join(OUT, "cover_titled_half.png"))

sheet = Image.new("RGB", (W + W // 2 + 30, H + 20), (255, 255, 255))
sheet.paste(lettered, (10, 10))
sheet.paste(half, (W + 20, 10))
sheet.save(os.path.join(OUT, "cover_sheet.png"))

print("crop", cw, "x", sh, "-> scale", round(scale, 3))
print("title", title_px, "px   room", room_px, "px")
print("written cover_titled.png, cover_plain.png")
