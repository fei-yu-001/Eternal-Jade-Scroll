"""Crop one reviewed four-shot source into four independent Resources images.
Sources and receipts remain under ignored Tools/logs. No network call is made here.
"""
import argparse, hashlib, json, re, uuid
from pathlib import Path
from PIL import Image
from importlib.util import module_from_spec, spec_from_file_location
ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "GameClient/TwelveJade/Assets/Resources/Art/Story/Qingshi"
def main():
    spec = spec_from_file_location("storyboards", ROOT / "Tools/generate-qingshi-storyboards.py")
    source_module = module_from_spec(spec); spec.loader.exec_module(source_module)
    parser = argparse.ArgumentParser(); parser.add_argument("scene", choices=source_module.SHOTS)
    args = parser.parse_args()
    folder = ROOT / "Tools/logs/qingshi-storyboards-20261123" / args.scene
    image = Image.open(folder / "source.png").convert("RGB")
    receipt = json.loads((folder / "source.json").read_text(encoding="utf-8"))
    if hashlib.sha256((folder / "source.png").read_bytes()).hexdigest() != receipt["sha256"]:
        raise ValueError("Source differs from the generation receipt")
    if image.width % 2 or image.height % 2: raise ValueError("Expected equal 2x2 source")
    template = (ART / "home.png.meta").read_text(encoding="utf-8")
    records = []
    for index, (art, _) in enumerate(source_module.SHOTS[args.scene]):
        if art in {"departure-ruins", "tomb-box"}: continue # Superseded after visual review.
        x, y = (index % 2) * image.width // 2, (index // 2) * image.height // 2
        box = (x+3, y+3, x+image.width//2-3, y+image.height//2-3)
        cropped = image.crop(box)
        target = ART / (art + ".png"); cropped.save(target, optimize=True)
        meta = target.with_suffix(".png.meta")
        if not meta.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, "eternal-jade/qingshi-story/" + art).hex
            meta.write_text(re.sub(r"(?m)^guid: [a-f0-9]+$", "guid: " + guid, template), encoding="utf-8")
        records.append(dict(art=art, sourceRect=box, size=cropped.size,
                            sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    (folder / "packed.json").write_text(json.dumps(records, indent=2)+"\n", encoding="utf-8")
    print(args.scene, "installed", len(records), "independent shots", cropped.size)
if __name__ == "__main__": main()
