"""Regenerates the sample images used by tools/Chameleon.DevHost.

Usage: python3 tools/scripts/make_devhost_samples.py tools/Chameleon.DevHost/samples
The "captures" imitate shaded viewport captures of a simple massing model; the "results"
imitate rendered versions of the same scenes. Requires Pillow.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

W, H = 1280, 720


def scene(draw, palette, variant):
    """Draws a ground plane and a few boxes in one-point perspective."""
    sky, ground, light, mid, dark = palette
    draw.rectangle([0, 0, W, H], fill=sky)
    horizon = 300 + variant * 25
    draw.polygon([(0, horizon), (W, horizon), (W, H), (0, H)], fill=ground)
    boxes = [
        (140 + variant * 60, horizon - 190, 250, 330),
        (470 - variant * 30, horizon - 290, 210, 470),
        (760 + variant * 20, horizon - 130, 330, 250),
    ]
    for x, y, w, h in boxes:
        depth = 70
        draw.polygon([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], fill=mid)
        draw.polygon([(x + w, y), (x + w + depth, y - depth // 2), (x + w + depth, y + h - depth // 2), (x + w, y + h)], fill=dark)
        draw.polygon([(x, y), (x + depth, y - depth // 2), (x + w + depth, y - depth // 2), (x + w, y)], fill=light)
        # Window bands give generated "results" something recognisable to keep in place.
        for row in range(y + 30, y + h - 20, 46):
            draw.rectangle([x + 18, row, x + w - 18, row + 16], fill=dark)


def capture(variant):
    img = Image.new("RGB", (W, H))
    scene(ImageDraw.Draw(img), ((236, 238, 240), (205, 207, 210), (228, 228, 228), (188, 188, 188), (150, 150, 150)), variant)
    return img


def result(variant, palette):
    img = Image.new("RGB", (W, H))
    scene(ImageDraw.Draw(img), palette, variant)
    return img.filter(ImageFilter.GaussianBlur(0.6))


def main(out_dir):
    out = Path(out_dir)
    (out / "results").mkdir(parents=True, exist_ok=True)

    names = ["active", "Exterior", "Interior", "Aerial"]
    for i, name in enumerate(names):
        capture(i).save(out / f"capture-{name}.png", optimize=True)

    palettes = [
        ((142, 190, 232), (118, 150, 96), (244, 232, 210), (214, 168, 120), (150, 104, 70)),   # day
        ((250, 170, 110), (96, 84, 80), (255, 214, 160), (196, 120, 90), (110, 62, 60)),       # dusk
        ((28, 36, 70), (30, 34, 44), (120, 140, 190), (70, 84, 130), (36, 44, 80)),            # night
    ]
    for i, palette in enumerate(palettes):
        result(i, palette).save(out / "results" / f"result-{i + 1}.jpg", quality=86)

    # A style reference the fake "open file" dialog returns.
    ref = Image.new("RGB", (640, 400))
    d = ImageDraw.Draw(ref)
    for y in range(400):
        d.line([(0, y), (640, y)], fill=(250 - y // 3, 170 - y // 5, 110 + y // 6))
    ref.save(out / "reference-style.jpg", quality=85)


if __name__ == "__main__":
    main(sys.argv[1])
