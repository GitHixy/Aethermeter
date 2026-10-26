"""
Generates Aethermeter's icon (icon.png, 512x512) from one set of constants.

It speaks the plugin's own panel language from src/UI/Theme.cs: smoked obsidian glass with an
aether-teal frame. The subject is the meter itself: four ranked bars, the leader white-hot at its
leading edge, rising out of a crystal glow, with the ember "recording" dot in the corner.

Drawn at 4x and downsampled, so every edge is anti-aliased.

Run from the repository root:  python tools/make_icon.py
"""
import math

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# ---------------------------------------------------------------- palette ------
INK_TOP = (0x1B, 0x21, 0x2B)
INK_BOTTOM = (0x07, 0x08, 0x0B)
AETHER = (0x5E, 0xD9, 0xC7)
AETHER_BRIGHT = (0x9C, 0xF0, 0xE2)
AETHER_DEEP = (0x1F, 0x6E, 0x66)
EMBER = (0xF0, 0x7D, 0x4F)
WHITE_HOT = (0xF4, 0xFF, 0xFC)

# ---------------------------------------------------------------- geometry -----
SIZE = 512
SCALE = 4
S = SIZE * SCALE
CORNER = 108
FRAME_INSET = 30
FRAME_RADIUS = 80

# Ranked bars: (length as a share of the track, colour at the head, opacity).
BARS = [
    (1.00, AETHER_BRIGHT, 1.00),
    (0.78, AETHER, 1.00),
    (0.60, (0x49, 0xAE, 0xA1), 1.00),
    (0.41, AETHER_DEEP, 1.00),
]
TRACK_LEFT = 104
TRACK_RIGHT = 412
BAR_TOP = 150
BAR_HEIGHT = 46
BAR_GAP = 22
BAR_RADIUS = 12


def px(v):
    return int(round(v * SCALE))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def rounded_mask(box, radius):
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([px(c) for c in box], radius=px(radius), fill=255)
    return mask


def vertical_gradient(top, bottom):
    grad = Image.new("RGB", (1, S))
    for y in range(S):
        grad.putpixel((0, y), lerp(top, bottom, y / (S - 1)))
    return grad.resize((S, S))


def horizontal_gradient(left, right, width, height):
    grad = Image.new("RGB", (width, 1))
    for x in range(width):
        t = x / max(width - 1, 1)
        grad.putpixel((x, 0), lerp(left, right, t ** 1.6))
    return grad.resize((width, height))


def glow(layer, radius):
    return layer.filter(ImageFilter.GaussianBlur(px(radius)))


def main():
    icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    tile = rounded_mask((0, 0, SIZE, SIZE), CORNER)

    # Obsidian body.
    body = vertical_gradient(INK_TOP, INK_BOTTOM).convert("RGBA")
    icon.paste(body, (0, 0), tile)

    # Aether pooling up from below the bars, like light through crystal.
    pool = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(pool).ellipse([px(40), px(250), px(470), px(560)], fill=AETHER + (80,))
    pool = glow(pool, 70)
    icon = Image.alpha_composite(icon, Image.composite(pool, Image.new("RGBA", (S, S)), tile))

    # Bars: a dim track, then the fill with a hot leading edge and its own glow.
    bars = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    track_w = TRACK_RIGHT - TRACK_LEFT
    for i, (share, head, alpha) in enumerate(BARS):
        top = BAR_TOP + i * (BAR_HEIGHT + BAR_GAP)
        box = (TRACK_LEFT, top, TRACK_RIGHT, top + BAR_HEIGHT)
        ImageDraw.Draw(bars).rounded_rectangle([px(c) for c in box], radius=px(BAR_RADIUS), fill=(255, 255, 255, 14))

        right = TRACK_LEFT + track_w * share
        fill_box = (TRACK_LEFT, top, right, top + BAR_HEIGHT)
        w, h = px(right - TRACK_LEFT), px(BAR_HEIGHT)
        tail = lerp(head, (0x0A, 0x30, 0x2C), 0.78)
        grad = horizontal_gradient(tail, head, w, h).convert("RGBA")
        mask = Image.new("L", (w, h), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius=px(BAR_RADIUS), fill=int(255 * alpha))
        bars.paste(grad, (px(TRACK_LEFT), px(top)), mask)

        # Light falling on the upper half, as on the meter's Glass bars.
        sheen = Image.new("RGBA", (w, h // 2), (255, 255, 255, 0))
        for y in range(h // 2):
            ImageDraw.Draw(sheen).line([(0, y), (w, y)], fill=(255, 255, 255, int(46 * (1 - y / (h / 2)))))
        sheen_mask = Image.new("L", (w, h // 2), 0)
        ImageDraw.Draw(sheen_mask).rounded_rectangle([0, 0, w - 1, h], radius=px(BAR_RADIUS), fill=255)
        bars.alpha_composite(Image.composite(sheen, Image.new("RGBA", sheen.size), sheen_mask), (px(TRACK_LEFT), px(top)))

        # The leader burns white at its edge.
        if i == 0:
            ImageDraw.Draw(bars).rounded_rectangle(
                [px(right - 16), px(top), px(right), px(top + BAR_HEIGHT)], radius=px(BAR_RADIUS),
                fill=WHITE_HOT + (255,))
            ImageDraw.Draw(halo).ellipse([px(right - 46), px(top - 30), px(right + 30), px(top + BAR_HEIGHT + 30)],
                                         fill=AETHER_BRIGHT + (200,))
        ImageDraw.Draw(halo).rounded_rectangle([px(c) for c in fill_box], radius=px(BAR_RADIUS),
                                               fill=head + (int((90 if i == 0 else 35) * alpha),))

    icon = Image.alpha_composite(icon, Image.composite(glow(halo, 16), Image.new("RGBA", (S, S)), tile))
    icon = Image.alpha_composite(icon, bars)

    # Rank pips to the left of each bar.
    pips = ImageDraw.Draw(icon)
    for i in range(len(BARS)):
        cy = BAR_TOP + i * (BAR_HEIGHT + BAR_GAP) + BAR_HEIGHT / 2
        r = 7 if i == 0 else 5
        colour = AETHER_BRIGHT if i == 0 else lerp(AETHER, INK_BOTTOM, 0.35 + i * 0.12)
        pips.ellipse([px(76 - r), px(cy - r), px(76 + r), px(cy + r)], fill=colour + (255,))

    # Recording dot with a soft ember bloom, top right.
    bloom = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(bloom).ellipse([px(380), px(70), px(440), px(130)], fill=EMBER + (170,))
    icon = Image.alpha_composite(icon, glow(bloom, 14))
    ImageDraw.Draw(icon).ellipse([px(398), px(88), px(422), px(112)], fill=EMBER + (255,))
    ImageDraw.Draw(icon).ellipse([px(404), px(92), px(412), px(100)], fill=(255, 214, 190, 255))

    # Header rule above the bars, fading out at both ends.
    rule = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rule)
    for x in range(px(TRACK_LEFT - 30), px(TRACK_RIGHT - 60)):
        t = (x - px(TRACK_LEFT - 30)) / (px(TRACK_RIGHT - 60) - px(TRACK_LEFT - 30))
        a = int(150 * math.sin(math.pi * t))
        rd.line([(x, px(108)), (x, px(110))], fill=AETHER + (a,))
    icon = Image.alpha_composite(icon, rule)

    # Frame: a teal hairline inset from the edge, brightest along the top.
    frame = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(
        [px(FRAME_INSET), px(FRAME_INSET), px(SIZE - FRAME_INSET), px(SIZE - FRAME_INSET)],
        radius=px(FRAME_RADIUS), outline=AETHER + (255,), width=px(3))
    fade = vertical_gradient((255, 255, 255), (90, 90, 90)).convert("L")
    frame.putalpha(ImageChops.multiply(frame.getchannel("A"), fade))
    icon = Image.alpha_composite(icon, glow(frame, 3))
    icon = Image.alpha_composite(icon, frame)

    # Outer edge highlight so the tile reads on light backgrounds too.
    edge = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle([0, 0, S - 1, S - 1], radius=px(CORNER), outline=(255, 255, 255, 34), width=px(2))
    icon = Image.alpha_composite(icon, edge)

    icon.putalpha(ImageChops.multiply(icon.getchannel("A"), tile))
    icon.resize((SIZE, SIZE), Image.LANCZOS).save("icon.png")
    print("Wrote icon.png")


if __name__ == "__main__":
    main()
