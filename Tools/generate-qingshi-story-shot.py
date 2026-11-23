"""Generate one replacement narrative shot with the authorized image-studio skill.
Does not overwrite production artwork; review the small preview before installing it.
"""
import argparse, contextlib, hashlib, importlib.util, json
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
PROMPTS={
"tomb-box": "A young man Shitou, EIGHTEEN YEAR OLD adult, clean-shaven youthful face, NO moustache NO beard, lean build, messy black topknot, patched clay-brown SHORT hemp tunic with sleeves rolled to elbows, dark trousers. He crouches in ancient Chinese tomb chamber picking up an abandoned small battered BLACK WOODEN BOX from mud, putting old cloth and ONE FIST-SIZED ROUGH GREY STONE into it. Behind him one angry black-robed adult walks away having discarded the unremarkable box; distant thieves quarrel over other objects. Young Shitou has no magic, no jewelry, no sword and no extravagant robes. The box and SMALL stone are clear in foreground. He is NOT old, not stocky, not muscular, not bearded. No double image of him, no lettering, no corpse. Muted ochre lamplight, intimate viewpoint."
}
def main():
    parser=argparse.ArgumentParser();parser.add_argument("shot",choices=PROMPTS);args=parser.parse_args()
    folder=ROOT/"Tools/logs/qingshi-storyboards-20261123"/(args.shot+"-replacement")
    folder.mkdir(parents=True,exist_ok=True);target=folder/"source.png"
    if target.exists():raise SystemExit("Existing source preserved")
    prompt="Single continuous cinematic narrative illustration, NOT a collage, no panels, no captions, no bubbles, NO TEXT. Fine Chinese ink drawing, delicate muted watercolor, worn cloth texture, realistic expressive anatomy, poor rural wartime world. Full scene with breathing room and clear faces in middle third. "+PROMPTS[args.shot]
    spec=importlib.util.spec_from_file_location("image_studio", "C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py")
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    with (folder/"generation.log").open("w",encoding="utf-8") as log,contextlib.redirect_stdout(log),contextlib.redirect_stderr(log):
        result=module.generate_image(prompt,channel_id="chuanapi",output=str(target),size="1536x1024")
    if not result or not target.exists():raise SystemExit("No image; inspect private local log")
    (folder/"source.json").write_text(json.dumps(dict(art=args.shot,prompt=prompt,skill="image-studio",sha256=hashlib.sha256(target.read_bytes()).hexdigest()),ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    im=Image.open(target).convert("RGB");im.thumbnail((1440,1000));im.save(folder/"review.jpg",quality=76,optimize=True)
    print("GENERATED",args.shot)
if __name__=="__main__":main()
