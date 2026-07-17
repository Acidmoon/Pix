# Generates launcher/Assets/pix.ico from the floating-ball design language.
# Usage: python launcher/tools/generate-icon.py
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SHELL_TOP = (43, 48, 56, 255)
SHELL_BOTTOM = (16, 19, 24, 255)
SIGNAL = (74, 224, 158, 255)
LETTER = (230, 233, 237, 255)

OUT_DIR = Path(__file__).resolve().parent.parent / "Assets"
BAHNSCHRIFT = "C:/Windows/Fonts/bahnschrift.ttf"


def lerp(a, b, t):
    return tuple(int(round(x + (y - x) * t)) for x, y in zip(a, b))


def shell_gradient(size):
    grad = Image.new("RGBA", (1, size))
    px = grad.load()
    for y in range(size):
        px[0, y] = lerp(SHELL_TOP, SHELL_BOTTOM, y / max(1, size - 1))
    return grad.resize((size, size))


def letter_font(px, weight=600):
    font = ImageFont.truetype(BAHNSCHRIFT, px)
    try:
        font.set_variation_by_axes([weight])
    except Exception:
        pass  # Older Pillow or non-variable font: keep default weight.
    return font


def render(size):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    margin = max(1, round(size * 0.055))
    outer = (margin, margin, size - margin - 1, size - margin - 1)

    # Gradient shell clipped to the disc.
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse(outer, fill=255)
    img.paste(shell_gradient(size), (0, 0), mask)

    draw = ImageDraw.Draw(img)
    detailed = size >= 32

    if detailed:
        # Specular sweep across the top.
        spec = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        inset = max(1, round(size * 0.036))
        spec_rect = (outer[0] + inset, outer[1] + inset, outer[2] - inset, outer[3] - inset)
        ImageDraw.Draw(spec).arc(spec_rect, 200, 340, fill=(255, 255, 255, 34), width=max(1, round(size * 0.021)))
        img.alpha_composite(spec)

        # Hairline track ring.
        ring_inset = max(2, round(size * 0.071))
        ring_rect = (outer[0] + ring_inset, outer[1] + ring_inset, outer[2] - ring_inset, outer[3] - ring_inset)
        draw.arc(ring_rect, 0, 360, fill=(255, 255, 255, 22), width=max(1, round(size * 0.018)))
        arc_rect = ring_rect
    else:
        arc_rect = outer

    # Emerald status arc with rounded caps.
    arc_w = max(1.5, size * 0.045)
    start, sweep = -58.0, 126.0
    draw.arc(arc_rect, start, start + sweep, fill=SIGNAL, width=max(1, round(arc_w)))
    cx = (arc_rect[0] + arc_rect[2]) / 2
    cy = (arc_rect[1] + arc_rect[3]) / 2
    rx = (arc_rect[2] - arc_rect[0]) / 2
    ry = (arc_rect[3] - arc_rect[1]) / 2
    for angle in (start, start + sweep):
        rad = math.radians(angle)
        ex, ey = cx + rx * math.cos(rad), cy + ry * math.sin(rad)
        r = max(0.5, arc_w / 2)
        draw.ellipse((ex - r, ey - r, ex + r, ey + r), fill=SIGNAL)

    # Letter mark.
    font_px = round(size * (0.36 if detailed else 0.42))
    font = letter_font(font_px)
    bbox = draw.textbbox((0, 0), "P", font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx = (size - tw) / 2 - bbox[0]
    ty = (outer[1] + outer[3]) / 2 - th / 2 - bbox[1]
    draw.text((tx, ty), "P", font=font, fill=LETTER)
    return img


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [render(s) for s in sizes]
    images[-1].save(OUT_DIR / "pix.ico", format="ICO", append_images=images[:-1])
    images[3].save(OUT_DIR / "pix-64.png")
    images[-1].resize((64, 64), Image.LANCZOS).save(OUT_DIR / "pix-preview.png")
    print(f"wrote {OUT_DIR / 'pix.ico'} with sizes {sizes}")


if __name__ == "__main__":
    main()
