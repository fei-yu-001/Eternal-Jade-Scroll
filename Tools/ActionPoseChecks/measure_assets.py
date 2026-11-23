"""Read-only alpha registration measurements; never rewrites game artwork/importers."""
import argparse
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
RESOURCES = ROOT / "GameClient/TwelveJade/Assets/Resources"


def measure(resource, count):
    path = RESOURCES / (resource + ".png")
    with Image.open(path) as source:
        alpha = source.convert("RGBA").getchannel("A")
        assert source.width % count == 0, resource
        width = source.width // count
        frames = []
        for i in range(count):
            bounds = alpha.crop((i * width, 0, (i + 1) * width, source.height)).point(
                lambda value: 255 if value >= 32 else 0).getbbox()
            assert bounds, (resource, i)
            frames.append(dict(left=bounds[0], top=bounds[1], right=bounds[2], bottom=bounds[3]))
        return dict(name=path.stem, width=source.width, height=source.height, frames=frames)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    entries = json.loads((RESOURCES / "Config/town-character-art.json").read_text(encoding="utf-8-sig"))["entries"]
    heroes = []
    for entry in entries:
        if not entry["id"].startswith("hero-"):
            continue
        gender = entry["id"].split("-")[1]
        heroes.append(dict(id=entry["id"], standing=measure(entry["stand"], 1),
                           walking=measure(entry["walk"], entry["frames"]),
                           strikes=measure("Art/Characters/Qingshi/qingshi-" + gender + "-strikes", 3)))
    assert len(heroes) == 6, "Expected both genders and front/side/back registrations"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(dict(alphaThreshold=32, heroes=heroes), indent=2), encoding="utf-8")
    walking_frames = sum(len(hero["walking"]["frames"]) for hero in heroes)
    print(f"Measured {len(heroes)} standing views, {walking_frames} walking frames, and both 3-frame attack sheets -> {args.output}")


if __name__ == "__main__":
    main()
