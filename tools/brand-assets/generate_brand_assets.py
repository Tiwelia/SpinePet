from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw


CANVAS_SIZE = 1024
BADGE_SIZE = 944
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Create deterministic SpinePet brand PNG and ICO exports."
    )
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    return parser.parse_args()


def make_avatar(source: Image.Image) -> Image.Image:
    source = source.convert("RGB")
    if source.width != source.height:
        raise ValueError("The brand source must be square.")

    crop_inset = max(2, round(source.width * 0.004))
    cropped = source.crop(
        (crop_inset, crop_inset, source.width - crop_inset, source.height - crop_inset)
    )
    badge = cropped.resize((BADGE_SIZE, BADGE_SIZE), Image.Resampling.LANCZOS)

    alpha = Image.new("L", badge.size, 0)
    ImageDraw.Draw(alpha).ellipse((0, 0, BADGE_SIZE - 1, BADGE_SIZE - 1), fill=255)

    badge_rgba = badge.convert("RGBA")
    badge_rgba.putalpha(alpha)

    avatar = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
    offset = (CANVAS_SIZE - BADGE_SIZE) // 2
    avatar.alpha_composite(badge_rgba, (offset, offset))
    return avatar


def save_ico(images: list[Image.Image], destination: Path) -> None:
    encoded: list[bytes] = []
    for image in images:
        from io import BytesIO

        stream = BytesIO()
        image.save(stream, format="PNG", optimize=True)
        encoded.append(stream.getvalue())

    header_size = 6 + (16 * len(images))
    offset = header_size
    with destination.open("wb") as icon_file:
        icon_file.write(struct.pack("<HHH", 0, 1, len(images)))
        for image, payload in zip(images, encoded, strict=True):
            width = 0 if image.width == 256 else image.width
            height = 0 if image.height == 256 else image.height
            icon_file.write(
                struct.pack(
                    "<BBBBHHII",
                    width,
                    height,
                    0,
                    0,
                    1,
                    32,
                    len(payload),
                    offset,
                )
            )
            offset += len(payload)

        for payload in encoded:
            icon_file.write(payload)


def validate_alpha(image: Image.Image) -> None:
    alpha = image.getchannel("A")
    if any(alpha.getpixel(point) != 0 for point in ((0, 0), (1023, 0), (0, 1023), (1023, 1023))):
        raise ValueError("Avatar corners must be transparent.")

    bounds = ImageChops.difference(alpha, Image.new("L", alpha.size, 0)).getbbox()
    if bounds is None:
        raise ValueError("Avatar contains no visible pixels.")

    if bounds != (40, 40, 984, 984):
        raise ValueError(f"Unexpected avatar bounds: {bounds}")


def main() -> None:
    args = parse_arguments()
    args.output_directory.mkdir(parents=True, exist_ok=True)

    with Image.open(args.source) as source:
        avatar = make_avatar(source)

    validate_alpha(avatar)

    master_path = args.output_directory / "spinepet-avatar.png"
    avatar.save(master_path, format="PNG", optimize=True)

    exports: dict[str, object] = {
        "source": args.source.name,
        "sourceSha256": hashlib.sha256(args.source.read_bytes()).hexdigest(),
        "generator": "tools/brand-assets/generate_brand_assets.py",
        "master": {"file": master_path.name, "size": [CANVAS_SIZE, CANVAS_SIZE]},
        "pngExports": [],
        "ico": {"file": "SpinePet.ico", "sizes": list(ICO_SIZES)},
        "safeAreaPixels": (CANVAS_SIZE - BADGE_SIZE) // 2,
        "palette": {
            "midnightNavy": "#121826",
            "lavender": "#8B7CF6",
            "indigo": "#7468E8",
            "cyan": "#63D8FF",
            "pearlWhite": "#F2F5FC",
        },
    }

    for size in (512, 256):
        export_path = args.output_directory / f"spinepet-avatar-{size}.png"
        avatar.resize((size, size), Image.Resampling.LANCZOS).save(
            export_path, format="PNG", optimize=True
        )
        exports["pngExports"].append({"file": export_path.name, "size": [size, size]})

    icon_images = [
        avatar.resize((size, size), Image.Resampling.LANCZOS) for size in ICO_SIZES
    ]
    save_ico(icon_images, args.output_directory / "SpinePet.ico")

    manifest_path = args.output_directory / "brand-assets.json"
    manifest_path.write_text(
        json.dumps(exports, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
