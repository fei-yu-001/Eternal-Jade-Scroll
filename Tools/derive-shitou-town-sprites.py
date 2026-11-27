"""Assemble Shitou's empty-handed town sprites from approved Image Studio art.

Keeps the original painted coat hem and four leg poses; only the upper body
is replaced. This corrects the premature box prop, not the gait animation.
Inputs are explicit so rerunning never reads already-overwritten game assets.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


def load(path, size):
    image = Image.open(path).convert("RGBA")
    if image.size != size:
        raise ValueError(f"Unexpected dimensions for {path}: {image.size}")
    return image


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upper", type=Path, required=True)
    parser.add_argument("--original-stand", type=Path, required=True)
    parser.add_argument("--original-walk", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    inputs = [args.upper, args.original_stand, args.original_walk]
    destinations = [args.output / "stand.png", args.output / "walk.png"]
    if any(src.resolve() == dst.resolve() for src in inputs for dst in destinations):
        raise ValueError("Output must not overwrite a source image")
    upper = load(args.upper, (384, 512))
    stand = load(args.original_stand, (384, 512))
    walk = load(args.original_walk, (1536, 512))
    args.output.mkdir(parents=True, exist_ok=True)
    yy, xx = np.indices((512, 384))
    keep_hand = ((xx < 169) & (yy < 305)) | ((xx > 234) & (yy < 301))
    blend = np.clip((yy - 242) / 20, 0, 1)
    frames = []
    checks = []
    originals = [stand] + [walk.crop((i * 384, 0, (i + 1) * 384, 512)) for i in range(4)]
    for original in originals:
        top = np.asarray(upper).astype(float).copy()
        lower = np.asarray(original).astype(float).copy()
        top[:, :, 3] *= 1 - blend
        top[:, :, 3] = np.where(keep_hand, np.asarray(upper)[:, :, 3], top[:, :, 3])
        lower[:, :, 3] *= np.clip((yy - 238) / 16, 0, 1)
        frame = Image.alpha_composite(
            Image.fromarray(lower.clip(0, 255).astype("uint8")),
            Image.fromarray(top.clip(0, 255).astype("uint8")))
        # Preserve every original lower-body pixel, including foot contacts.
        if not np.array_equal(np.asarray(frame)[305:], np.asarray(original)[305:]):
            raise ValueError("Original lower-body pose changed")
        checks.append({"alphaBounds": frame.getchannel("A").getbbox(),
                       "originalLowerBodyUnchangedFromY": 305})
        frames.append(frame)
    frames[0].save(destinations[0], optimize=True)
    sheet = Image.new("RGBA", (1536, 512))
    for index, frame in enumerate(frames[1:]):
        sheet.paste(frame, (index * 384, 0))
    sheet.save(destinations[1], optimize=True)
    preview = Image.new("RGB", (1920, 512), "#d8d0bc")
    for index, frame in enumerate(frames):
        preview.paste(frame, (index * 384, 0), frame)
    preview.resize((1440, 384), Image.Resampling.LANCZOS).save(
        args.output / "review.jpg", quality=85, optimize=True)
    receipt = {"inputs": [{"name": p.name, "sha256": digest(p)} for p in inputs],
               "outputs": [{"name": p.name, "sha256": digest(p)} for p in destinations],
               "frames": checks,
               "scope": "Empty hands; original coat hem and leg poses; no gait redesign"}
    (args.output / "derivation.json").write_text(
        json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Derived stand and four-frame walk; all original lower-body pixels preserved.")


if __name__ == "__main__":
    main()
