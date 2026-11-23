"""Generate a single transparent Qingshi dossier illustration through the installed image-studio skill."""
import argparse
import contextlib
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL = Path("C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py")
STYLE = """Premium Chinese historical RPG character illustration, hand-painted semi-realistic guofeng concept art, elegant expressive ink contours and soft watercolor shading, warm restrained earthy palette. An appealing young ADULT protagonist of eighteen, healthy lean proportions, expressive almond-shaped dark eyes, carefully drawn attractive face with natural warm skin, modest determined gentle expression. This is a character illustration, NOT a photograph, NOT a passport photo, NOT a 3D render. FULL BODY from all hair to soles visible, one single character, relaxed asymmetric three-quarter standing pose, head angled slightly toward viewer, natural hands, eight-head adult proportions. The silhouette is clean and fully isolated on a perfectly uniform PURE WHITE background, no scenery, no frame, no paper texture, no shadow puddle, no other objects, no letters. Leave generous space around topknot, hands and feet. Dark clothing and warm skin with no pure-white highlights, so the background can be cleanly removed. Late-imperial rural war-torn village life; coarse patched short hemp jacket, cloth sash, plain narrow dark trousers, worn cloth shoes. No silk, no brocade, no embroidery, no armor, no jewelry, no crown, no flowing immortal robe, no glowing effects, no decorative fantasy weapon. Clothing should look modest and worn yet visually well designed. """
PEOPLE = {
    "male": "A handsome Chinese young adult man, refined lively face, subtly defined cheekbones and soft clear jaw, calm bright gaze and the slightest confident smile, natural unpainted lips. Black hair loosely tied in a small practical topknot with a plain dark cloth strip, a few soft flyaway strands. Faded warm grey-brown short tunic with rolled forearms, small mended patch at elbow, dark charcoal trousers with a discreet knee patch. One hand naturally resting on the plain cloth sash, the other relaxed beside his thigh; slight weight shift, facing right while looking gently toward the viewer. No muscular bodybuilder, no beard, no large V-neck, no chest exposure.",
    "female": "A beautiful Chinese young adult woman, graceful lively face, elegant dark eyebrows, warm focused eyes, a small natural smile, subtle adult features, no heavy cosmetics. Black hair in a practical low tied bun with a simple grey cloth ribbon and a few loose strands. Faded grey-indigo short hemp jacket with a modest closed collar, rolled forearms, small repaired elbow patch, dark charcoal trousers with a discreet knee patch. One hand holding a plain woven strap at her waist, the other relaxed naturally beside her thigh; relaxed confident stance, facing right while looking gently toward the viewer. No skirt, no jewelry, no sexy pose, no exposed cleavage."
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("gender", choices=PEOPLE)
    parser.add_argument("--channel", default="chuanapi")
    args = parser.parse_args()
    folder = ROOT / "Tools/logs/qingshi-dossier-20261123"
    folder.mkdir(parents=True, exist_ok=True)
    output = folder / (args.gender + "-figure.png")
    raw = folder / (args.gender + "-original.png")
    if output.exists() or raw.exists():
        raise SystemExit("Existing artwork preserved; choose a separate review revision.")
    spec = importlib.util.spec_from_file_location("user_image_studio", SKILL)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    prompt = STYLE + PEOPLE[args.gender]
    with (folder / (args.gender + "-generation.log")).open("w", encoding="utf-8") as log, contextlib.redirect_stdout(log), contextlib.redirect_stderr(log):
        result = module.generate_image(prompt, channel_id=args.channel, output=str(raw), size="1024x1536")
        if result and raw.exists():
            module.make_transparent(str(raw), str(output))
    if not result or not output.exists():
        raise SystemExit("Generation failed; inspect local status without printing credentials.")
    metadata = {"gender": args.gender, "skill": "image-studio", "channel": args.channel,
                "prompt": prompt, "rawSha256": hashlib.sha256(raw.read_bytes()).hexdigest(),
                "transparentSha256": hashlib.sha256(output.read_bytes()).hexdigest()}
    (folder / (args.gender + "-source.json")).write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("GENERATED", args.gender, output.stat().st_size, "bytes")

if __name__ == "__main__":
    main()
