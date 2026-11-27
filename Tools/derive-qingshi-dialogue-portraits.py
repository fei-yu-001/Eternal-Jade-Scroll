"""Derive matching dialogue portraits from the approved transparent full figures.
Only resamples/crops existing artwork; no new painted face is invented.
"""
import hashlib,json
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'GameClient/TwelveJade/Assets/Resources/Art/Characters/Qingshi'
OUT=ROOT/'Tools/logs/qingshi-direction-20261127'
# Hand-reviewed head/shoulder windows, in source pixels. Padding preserves all hair.
WINDOWS={'male':(-18,-24,410,404),'female':(-12,-26,509,495)}
def main():
 OUT.mkdir(parents=True,exist_ok=True)
 receipts=[]
 for gender,window in WINDOWS.items():
  source=ART/f'qingshi-{gender}-figure.png'
  im=Image.open(source).convert('RGBA').crop(window).resize((512,512),Image.Resampling.LANCZOS)
  dest=ART/f'qingshi-{gender}-portrait.png'
  im.save(dest,optimize=True)
  bg=Image.new('RGBA',im.size,(232,223,200,255));bg.alpha_composite(im)
  bg.convert('RGB').save(OUT/f'{gender}-portrait-after.jpg',quality=83,optimize=True)
  alpha=im.getchannel('A')
  receipts.append(dict(gender=gender,source=str(source.relative_to(ROOT)),sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),window=window,
   destination=str(dest.relative_to(ROOT)),sha256=hashlib.sha256(dest.read_bytes()).hexdigest(),size=im.size,alphaExtrema=alpha.getextrema()))
 (OUT/'portrait-derivations.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('Derived two 512px transparent portraits from approved figures; original GUIDs kept.')
if __name__=='__main__':main()
