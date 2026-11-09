import os
from PIL import Image, ImageDraw

W, H = 32, 48
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

CASE = (46, 50, 62, 255)
CASE_HI = (96, 105, 124, 255)
CASE_LO = (26, 29, 36, 255)
PANEL = (21, 24, 31, 255)
SCR = (10, 40, 36, 255)
SEG = (59, 224, 176, 255)
SEG_OFF = (17, 55, 47, 255)
CHIP = (224, 176, 58, 255)
CHIP_HI = (250, 213, 112, 255)
CHIP_DK = (138, 108, 30, 255)
PIN = (198, 204, 214, 255)

d.rectangle([0, 0, W - 1, H - 1], fill=CASE)
d.rectangle([0, 0, W - 1, 1], fill=CASE_HI)
d.rectangle([0, H - 2, W - 1, H - 1], fill=CASE_LO)
d.rectangle([2, 3, W - 3, H - 4], fill=PANEL)

d.rectangle([4, 5, W - 5, 15], fill=SCR)
d.rectangle([4, 5, W - 5, 6], fill=(20, 64, 56, 255))
for i, x0 in enumerate([5, 12, 19]):
    d.rectangle([x0, 8, x0 + 5, 13], fill=(SEG if i < 2 else SEG_OFF))

d.rectangle([4, 17, W - 5, 18], fill=CASE_LO)

for y0 in (26, 30, 34):
    d.rectangle([3, y0, 6, y0 + 1], fill=PIN)
    d.rectangle([W - 7, y0, W - 4, y0 + 1], fill=PIN)

d.rectangle([7, 24, W - 8, 36], fill=CHIP)
d.rectangle([7, 24, W - 8, 25], fill=CHIP_HI)
d.rectangle([7, 35, W - 8, 36], fill=CHIP_DK)
d.rectangle([14, 28, 17, 32], fill=CHIP_DK)
d.rectangle([15, 29, 16, 31], fill=CHIP_HI)

d.rectangle([9, 39, 21, 43], fill=(58, 64, 78, 255))
d.rectangle([10, 40, 19, 42], fill=(127, 209, 255, 255))
d.rectangle([22, 40, 23, 42], fill=(58, 64, 78, 255))

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.normpath(os.path.join(here, "..", "Textures", "Items", "self_learn_programmer.png"))
img.save(out)
print("saved", out, img.size, "-> cells", W // 16, "x", H // 16)
for x, y in [(8, 8), (24, 8), (8, 24), (24, 24), (8, 40), (24, 40)]:
    print("  cell center", (x, y), img.getpixel((x, y)))
