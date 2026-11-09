#!/usr/bin/env python3
"""Draw the four Steam Workshop preview images (512x512) for this repository.

The artwork here is drawn from scratch with rectangles on a 32x32 pixel grid and
scaled up with nearest-neighbour, so it matches the game's pixel-art look without
copying any of the game's own sprites.

Palette is sampled from the game's item icons: near-black navy outlines, muted
greys, warm beige highlights, plus one accent colour per mod.

Usage:
    python tools/make-previews.py            # writes <mod>/preview.png for all four
    python tools/make-previews.py auto_uv_lamp
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SIZE = 512
GRID = 32  # logical pixel grid for the icons

# ---------------------------------------------------------------- palette
BG        = (0x12, 0x14, 0x1A)
PANEL     = (0x19, 0x1D, 0x26)
BORDER    = (0x33, 0x3A, 0x4A)
INK       = (0x18, 0x1B, 0x24)   # outline
GREY_D    = (0x3A, 0x3E, 0x4C)
GREY_M    = (0x6E, 0x72, 0x85)
GREY_L    = (0x9A, 0x9F, 0xB0)
BEIGE     = (0xC9, 0xC3, 0xA8)
BEIGE_L   = (0xE4, 0xDF, 0xC9)
WHITE     = (0xED, 0xEA, 0xE1)
TEXT_SUB  = (0xA9, 0xB1, 0xC4)
TEXT_DIM  = (0x6C, 0x74, 0x88)
SHADOW    = (0x0A, 0x0C, 0x10)

def _pick(candidates):
    """First font that actually exists, so the script runs outside this machine too."""
    for p in candidates:
        if Path(p).exists():
            return p
    raise SystemExit("no usable CJK font found; edit FONT_BOLD / FONT_REG")


# msyhbd.ttc is deliberately avoided: Pillow mis-parses its cmap here and renders
# common characters (灯/水/标 ...) as .notdef garbage. DengXian Bold and SimHei are fine.
FONT_BOLD = _pick([r"C:\Windows\Fonts\Dengb.ttf", r"C:\Windows\Fonts\simhei.ttf",
                   r"C:\Windows\Fonts\msyh.ttc"])
FONT_REG = _pick([r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\Deng.ttf",
                  r"C:\Windows\Fonts\simhei.ttf"])


# ---------------------------------------------------------------- pixel helpers
class Icon:
    """A 32x32 logical-pixel canvas that upscales to a chunky sprite."""

    def __init__(self):
        self.im = Image.new("RGBA", (GRID, GRID), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)

    def r(self, x, y, w, h, c):
        self.d.rectangle([x, y, x + w - 1, y + h - 1], fill=c + (255,) if len(c) == 3 else c)

    def frame(self, x, y, w, h, fill, edge=INK):
        """Filled box with a 1px outline, the way the game's icons are built."""
        self.r(x, y, w, h, edge)
        self.r(x + 1, y + 1, w - 2, h - 2, fill)

    def scale(self, k):
        return self.im.resize((GRID * k, GRID * k), Image.NEAREST)

    def render(self, k):
        return self.im.resize((GRID * k, GRID * k), Image.NEAREST)


def draw_uv_lamp():
    """Violet UV tube in a wall fixture, with a water droplet underneath."""
    i = Icon()
    # mounting plate + bracket
    i.r(8, 4, 16, 3, GREY_D)
    i.r(8, 4, 16, 1, GREY_M)
    i.r(15, 7, 2, 2, GREY_M)
    # metal caps
    i.frame(11, 8, 10, 3, GREY_M)
    i.frame(11, 19, 10, 3, GREY_M)
    # glass tube
    i.r(12, 11, 8, 8, INK)
    i.r(13, 11, 6, 8, (0x5B, 0x3F, 0x9C))
    i.r(14, 11, 4, 8, (0x8B, 0x5C, 0xF0))
    i.r(15, 12, 1, 6, (0xC9, 0xA6, 0xFF))
    # rays
    for y in (12, 15, 18):
        i.r(9, y, 1, 2, (0x6B, 0x4C, 0xB4))
        i.r(22, y, 1, 2, (0x6B, 0x4C, 0xB4))
    # droplet: point on top, round bottom
    i.r(15, 23, 2, 1, (0x3E, 0x8F, 0xC4))
    i.r(14, 24, 4, 2, (0x4F, 0xA8, 0xE0))
    i.r(14, 26, 4, 1, (0x4F, 0xA8, 0xE0))
    i.r(15, 27, 2, 1, (0x2E, 0x74, 0xA6))
    i.r(14, 25, 1, 1, (0x9A, 0xDA, 0xF8))
    return i


def draw_label_crate(has_label=True):
    """Storage crate with (or without) a paper tag on its front."""
    i = Icon()
    i.frame(4, 8, 24, 20, GREY_D)
    # lid
    i.r(5, 9, 22, 3, GREY_M)
    i.r(5, 9, 22, 1, GREY_L)
    if has_label:
        # tag
        i.r(9, 14, 14, 9, INK)
        i.r(10, 15, 12, 7, BEIGE)
        i.r(11, 16, 6, 1, GREY_M)
        i.r(11, 18, 9, 1, GREY_M)
        i.r(11, 20, 5, 1, GREY_M)
        # tag string to the lid
        i.r(15, 12, 1, 2, BEIGE_L)
    else:
        # bare front panel with a recessed handle
        i.r(9, 15, 14, 8, (0x32, 0x36, 0x42))
        i.r(13, 18, 6, 2, INK)
    # feet + side handle
    i.r(6, 28, 4, 2, GREY_M)
    i.r(22, 28, 4, 2, GREY_M)
    i.r(2, 15, 2, 5, GREY_M)
    i.r(28, 15, 2, 5, GREY_M)
    return i


def draw_power_array():
    """Battery block with a bolt and four cell studs (kept inside the frame)."""
    i = Icon()
    i.frame(5, 6, 22, 22, (0x2B, 0x2F, 0x3C))
    # inner recess
    i.r(8, 9, 16, 16, (0x22, 0x26, 0x30))
    # cell studs, inside the frame
    for x, y in ((9, 10), (21, 10), (9, 22), (21, 22)):
        i.r(x, y, 2, 2, (0x8E, 0x4A, 0x3C))
    # bolt
    for x, y, w, h in ((15, 10, 3, 2), (14, 12, 4, 2), (13, 14, 5, 2),
                       (14, 16, 3, 2), (13, 18, 3, 2), (12, 20, 3, 2)):
        i.r(x, y, w, h, (0xF0, 0xB4, 0x55) if y < 16 else (0xD8, 0x98, 0x3C))
    # terminals
    i.r(8, 3, 3, 3, GREY_M)
    i.r(21, 3, 3, 3, GREY_M)
    i.r(9, 28, 4, 2, GREY_M)
    i.r(19, 28, 4, 2, GREY_M)
    return i


def draw_grid_card():
    """4x3 layout grid with a few covered cells: the 'optimised placement' motif."""
    i = Icon()
    i.frame(4, 6, 24, 20, (0x23, 0x28, 0x38))
    covered = {(0, 0), (1, 0), (0, 1), (3, 2)}
    for cx in range(4):
        for cy in range(3):
            x = 6 + cx * 5
            y = 8 + cy * 5
            i.r(x, y, 4, 4, (0xF0, 0xB4, 0x55) if (cx, cy) in covered else (0x39, 0x3F, 0x50))
    return i


def draw_programmer():
    """Self-learning module programmer: screen band, module slot, chip inserted."""
    i = Icon()
    i.frame(5, 4, 22, 26, (0x26, 0x2A, 0x36))
    # screen
    i.r(7, 7, 18, 6, INK)
    i.r(8, 8, 16, 4, (0x2F, 0x8C, 0x7E))
    i.r(9, 9, 5, 2, (0x6D, 0xE3, 0xCC))
    i.r(15, 9, 3, 2, (0x45, 0xA8, 0x98))
    # slot
    i.r(7, 16, 18, 7, INK)
    i.r(8, 17, 16, 5, (0x3A, 0x3E, 0x4C))
    # module chip in the slot
    i.r(12, 18, 8, 4, (0xE0, 0xA2, 0x4C))
    i.r(13, 19, 6, 2, (0xF3, 0xC0, 0x72))
    # button row
    i.r(8, 25, 4, 2, GREY_M)
    i.r(14, 25, 4, 2, (0x6D, 0xE3, 0xCC))
    i.r(20, 25, 3, 2, GREY_M)
    return i


def draw_chip_icon():
    """Small module chip used as decoration."""
    i = Icon()
    i.frame(8, 10, 16, 12, (0x2B, 0x2F, 0x3C))
    for y in (12, 15, 18):
        i.r(5, y, 3, 1, GREY_M)
        i.r(24, y, 3, 1, GREY_M)
    i.r(11, 13, 10, 6, (0xE0, 0xA2, 0x4C))
    i.r(13, 15, 6, 2, (0xF3, 0xC0, 0x72))
    return i


# ---------------------------------------------------------------- before / after pairs
# Each pair is drawn on the same grid as the hero icon, then rendered small.
def mini_tap(stage):
    """stage 0: plain tap with grey water. stage 1: UV tube fitted, violet water."""
    i = Icon()
    # side-on wall tap: vertical pipe, arm, spout, handle
    i.r(5, 7, 4, 20, GREY_M)
    i.r(9, 7, 11, 4, GREY_M)
    i.r(16, 11, 4, 5, GREY_D)
    i.r(7, 4, 6, 3, GREY_L)
    if stage:
        # UV tube clipped above the body
        i.r(5, 1, 15, 2, (0x5B, 0x3F, 0x9C))
        i.r(6, 1, 12, 1, (0xC9, 0xA6, 0xFF))
        i.r(16, 16, 4, 12, (0x8B, 0x5C, 0xF0))
        i.r(17, 17, 1, 10, (0xD9, 0xC2, 0xFF))
    else:
        i.r(16, 16, 4, 12, (0x59, 0x6A, 0x78))
        i.r(17, 17, 1, 10, (0x7E, 0x91, 0xA0))
    return i


def mini_crate(stage):
    return draw_label_crate(has_label=bool(stage))


def mini_layout(stage):
    """stage 0: cells scattered. stage 1: cells packed together and lit."""
    i = Icon()
    cells = [(7, 9), (15, 9), (7, 17), (15, 17)] if stage else [(5, 7), (21, 7), (5, 21), (21, 23)]
    for x, y in cells:
        i.frame(x, y, 9, 9, (0x2B, 0x2F, 0x3C))
        i.r(x + 2, y + 2, 5, 5, (0xF0, 0xB4, 0x55) if stage else (0x5B, 0x60, 0x70))
    if stage:
        i.r(16, 12, 2, 2, (0xF0, 0xB4, 0x55))
        i.r(16, 20, 2, 2, (0xF0, 0xB4, 0x55))
    return i


def mini_module(stage):
    """stage 0: blank module. stage 1: self-learning bar filled."""
    i = Icon()
    i.frame(6, 9, 20, 14, (0x2B, 0x2F, 0x3C))
    i.r(8, 12, 16, 3, INK)
    i.r(9, 13, 3 if stage else 2, 1, (0x6D, 0xE3, 0xCC) if stage else (0x45, 0xA8, 0x98))
    i.r(8, 17, 16, 4, (0x3A, 0x3F, 0x50))
    if stage:
        i.r(9, 18, 13, 2, (0xE0, 0xA2, 0x4C))
        i.r(9, 18, 7, 2, (0xF3, 0xC0, 0x72))
    return i


# ---------------------------------------------------------------- composition
def dither_burst(base, cx, cy, radius, colour, alpha=26, step=4):
    """Chequered halo: a soft glow that still reads as pixels, not a gradient."""
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for y in range(cy - radius, cy + radius, step):
        for x in range(cx - radius, cx + radius, step):
            if ((x // step) + (y // step)) % 2:
                continue
            dx = (x - cx) / radius
            dy = (y - cy) / radius
            if dx * dx + dy * dy > 1:
                continue
            d.rectangle([x, y, x + step - 1, y + step - 1], fill=colour + (alpha,))
    base.alpha_composite(layer)


def cut_corners(im, box, cut=8, fill=(0, 0, 0, 0)):
    """Knock the four corners off a rectangle so the frame looks hand-drawn."""
    x0, y0, x1, y1 = box
    d = ImageDraw.Draw(im)
    for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        sx = cx if cx == x0 else x1 - cut
        sy = cy if cy == y0 else y1 - cut
        d.rectangle([sx, sy, sx + cut, sy + cut], fill=fill)


def text_shadow(d, xy, s, font, fill, shadow=SHADOW, off=2):
    d.text((xy[0] + off, xy[1] + off), s, font=font, fill=shadow + (200,))
    d.text(xy, s, font=font, fill=fill + (255,))


def draw_strip(img, d, accent, before, after, labels=("改前", "改后")):
    """Two small cells with an arrow between: what the mod changes, at a glance."""
    cell_w, cell_h, y = 120, 86, 210
    gap = 36
    total = cell_w * 2 + gap
    x0 = (SIZE - total) // 2
    xs = (x0, x0 + cell_w + gap)
    f_lab = ImageFont.truetype(FONT_REG, 13)
    for idx, (x, icon_factory) in enumerate(zip(xs, (before, after))):
        live = idx == 1
        d.rectangle([x, y, x + cell_w - 1, y + cell_h - 1], fill=(0x0F, 0x11, 0x16, 255),
                    outline=(accent if live else (0x3A, 0x42, 0x56)) + (255,), width=1)
        art = icon_factory().render(2)
        img.alpha_composite(art, (x + (cell_w - art.width) // 2, y + 4))
        col = accent if live else (0x6C, 0x74, 0x88)
        w = d.textlength(labels[idx], font=f_lab)
        d.text((x + (cell_w - w) / 2, y + 66), labels[idx], font=f_lab, fill=col + (255,))
    ax0 = xs[0] + cell_w + 9
    ax1 = xs[1] - 9
    ay = y + cell_h // 2 - 4
    d.line([(ax0, ay), (ax1 - 8, ay)], fill=(0x7C, 0x84, 0x98, 255), width=3)
    d.polygon([(ax1, ay), (ax1 - 9, ay - 6), (ax1 - 9, ay + 6)],
              fill=(0x7C, 0x84, 0x98, 255))


def build(mod, out_path):
    cfg = CONFIG[mod]
    accent = cfg["accent"]
    compare = cfg.get("layout") == "compare"
    img = Image.new("RGBA", (SIZE, SIZE), BG + (255,))
    d = ImageDraw.Draw(img)

    # panel with cut corners
    d.rectangle([10, 10, SIZE - 11, SIZE - 11], fill=PANEL + (255,))
    cut_corners(img, (10, 10, SIZE - 11, SIZE - 11), fill=BG + (255,))
    d.rectangle([10, 10, SIZE - 11, SIZE - 11], outline=BORDER + (255,), width=2)

    # faint pixel grid
    grid = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grid)
    for gx in range(10, SIZE - 10, 16):
        gd.line([(gx, 12), (gx, SIZE - 12)], fill=(0x22, 0x27, 0x33, 255))
    for gy in range(12, SIZE - 10, 16):
        gd.line([(12, gy), (SIZE - 12, gy)], fill=(0x22, 0x27, 0x33, 255))
    img.alpha_composite(Image.composite(grid, Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0)),
                                        Image.new("L", (SIZE, SIZE), 30).convert("L")))

    # header
    f_small = ImageFont.truetype(FONT_REG, 13)
    f_mono = ImageFont.truetype(FONT_REG, 13)
    d.text((26, 22), "PROBABLY STOLEN  ·  MOD", font=f_small, fill=TEXT_DIM + (255,))
    d.text((SIZE - 26 - d.textlength(cfg["tag"], font=f_small), 22),
           cfg["tag"], font=f_small, fill=accent + (255,))
    d.line([(26, 46), (SIZE - 27, 46)], fill=(0x2A, 0x30, 0x3E, 255))

    # hero: smaller when the before/after strip needs the room
    sprite = cfg["sprite"]()
    if compare:
        k = max(2, min(cfg["scale"], 152 // cfg.get("grid_h", 32)))
        zone_top, zone_h = 54, 152
    else:
        k = cfg["scale"]
        zone_top, zone_h = 56, 250
    hero = sprite.render(k)
    hx = (SIZE - hero.width) // 2 + cfg.get("dx", 0)
    hy = zone_top + (zone_h - hero.height) // 2 + cfg.get("dy", 0)
    dither_burst(img, hx + hero.width // 2, hy + hero.height // 2,
                 max(hero.width, hero.height) // 2 + 6, accent, cfg.get("glow", 20), step=2)
    # contact shadow: a short, low-contrast band right under the sprite
    sw = int(hero.width * 0.54)
    sh = Image.new("RGBA", (sw, 6), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rectangle([0, 0, sw - 1, 4], fill=(6, 7, 11, 70))
    img.alpha_composite(sh, (hx + (hero.width - sw) // 2, hy + hero.height - 5))
    img.alpha_composite(hero, (hx, hy))

    if compare:
        draw_strip(img, d, accent, cfg["before"], cfg["after"])
    else:
        extras = {"chip": draw_chip_icon, "grid": draw_grid_card}
        for key, px_, py_, kk in cfg.get("extras", []):
            img.alpha_composite(extras[key]().render(kk), (px_, py_))

    # title block
    f_title = ImageFont.truetype(FONT_BOLD, 36)
    f_sub = ImageFont.truetype(FONT_REG, 19)
    f_chip = ImageFont.truetype(FONT_REG, 16)
    d.rectangle([26, 306, 26 + 44, 309], fill=accent + (255,))
    text_shadow(d, (26, 318), cfg["title"], f_title, WHITE)
    d.text((26, 366), cfg["sub"], font=f_sub, fill=TEXT_SUB + (255,))

    # chips
    cx = 26
    cy = 404
    for label in cfg["chips"]:
        w = int(d.textlength(label, font=f_chip)) + 26
        d.rectangle([cx, cy, cx + w, cy + 30], fill=(0x23, 0x28, 0x38, 255),
                    outline=(0x3A, 0x42, 0x56, 255), width=1)
        d.rectangle([cx + 1, cy + 1, cx + 3, cy + 28], fill=accent + (255,))
        d.text((cx + 12, cy + 6), label, font=f_chip, fill=(0xD5, 0xD9, 0xE3, 255))
        cx += w + 10

    # footer
    d.line([(26, 462), (SIZE - 27, 462)], fill=(0x2A, 0x30, 0x3E, 255))
    d.text((26, 472), cfg["id"], font=f_mono, fill=TEXT_DIM + (255,))
    right = f"v{cfg['ver']}  ·  0.46D"
    d.text((SIZE - 26 - d.textlength(right, font=f_mono), 472), right,
           font=f_mono, fill=TEXT_DIM + (255,))

    out_path.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(out_path, "PNG", optimize=True)
    return out_path


CONFIG = {
    "auto_uv_lamp": dict(
        sprite=draw_uv_lamp, scale=7, dx=0, dy=0, accent=(0x8B, 0x5C, 0xF0), glow=22,
        title="紫外灯水龙头", sub="装灯即提纯 · 拖出即卸下",
        chips=["98% 基础水", "顺手杀菌", "随存档保存"],
        tag="WATER", id="auto_uv_lamp", ver="2.0.0",
        layout="compare", grid_h=32,
        before=lambda: mini_tap(0), after=lambda: mini_tap(1),
    ),
    "storage_bay_label": dict(
        sprite=draw_label_crate, scale=7, dx=0, dy=0, accent=(0xE2, 0xC0, 0x76), glow=18,
        title="存储舱标签", sub="给任意容器加一行自己的文字",
        chips=["右键打标签", "悬停 + 角标", "三套外观"],
        tag="LABEL", id="storage_bay_label", ver="1.7.1",
        layout="compare", grid_h=32,
        before=lambda: mini_crate(0), after=lambda: mini_crate(1),
    ),
    "machine_power_layout": dict(
        sprite=draw_power_array, scale=7, dx=0, dy=0, accent=(0xF0, 0xB4, 0x55), glow=20,
        title="机器区供电布局", sub="一键把能量阵列搬到最优相邻位",
        chips=["相邻覆盖最大", "夜间回电", "可整体重排"],
        tag="POWER", id="machine_power_layout", ver="3.1.0",
        layout="compare", grid_h=32,
        before=lambda: mini_layout(0), after=lambda: mini_layout(1),
    ),
    "module_solo_upgrade": dict(
        sprite=draw_programmer, scale=7, dx=0, dy=0, accent=(0x6D, 0xE3, 0xCC), glow=20,
        title="模组独自升级", sub="耗电为模组植入自学习词条",
        chips=["自学习进度", "电池供电", "博士处出售"],
        tag="DEVICE", id="module_solo_upgrade", ver="3.5.0",
        layout="compare", grid_h=48,
        before=lambda: mini_module(0), after=lambda: mini_module(1),
    ),
}


def main(argv):
    root = Path(__file__).resolve().parent.parent
    targets = argv[1:] or list(CONFIG)
    for mod in targets:
        if mod not in CONFIG:
            print(f"unknown mod: {mod}")
            continue
        out = build(mod, root / mod / "preview.png")
        kb = out.stat().st_size / 1024
        flag = "OK" if kb < 1024 else "TOO BIG"
        print(f"{mod:<22} -> {out.relative_to(root)}  {kb:6.1f} KB  {flag}")


if __name__ == "__main__":
    main(sys.argv)
