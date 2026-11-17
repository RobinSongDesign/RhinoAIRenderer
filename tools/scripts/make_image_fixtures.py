"""Regenerates the small image fixtures used by Chameleon.Core.Tests.

Usage: python3 tools/scripts/make_image_fixtures.py tests/Chameleon.Core.Tests/Fixtures/images
Requires Pillow with JPEG and WebP support.
"""
import sys
from pathlib import Path

from PIL import Image


def gradient(width: int, height: int) -> Image.Image:
    """Builds a deterministic RGB gradient so files stay tiny and reproducible."""
    img = Image.new("RGB", (width, height))
    img.putdata([(x * 255 // width, y * 255 // height, 96) for y in range(height) for x in range(width)])
    return img


def main(out_dir: str) -> None:
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)

    gradient(64, 48).save(out / "png_64x48.png")
    gradient(80, 60).save(out / "jpeg_baseline_80x60.jpg", quality=80)
    gradient(72, 54).save(out / "jpeg_progressive_72x54.jpg", quality=80, progressive=True)

    # EXIF puts an APP1 segment (with an embedded thumbnail-sized payload) before the frame header.
    exif = Image.Exif()
    exif[0x010E] = "Chameleon fixture " + "x" * 600
    gradient(90, 40).save(out / "jpeg_exif_90x40.jpg", quality=80, exif=exif)

    gradient(50, 70).save(out / "webp_lossy_50x70.webp", quality=80)
    gradient(33, 21).save(out / "webp_lossless_33x21.webp", lossless=True)

    # An alpha channel on a lossy file forces the extended (VP8X) container.
    rgba = gradient(44, 28).convert("RGBA")
    rgba.putalpha(128)
    rgba.save(out / "webp_extended_44x28.webp", quality=80)


if __name__ == "__main__":
    main(sys.argv[1])
