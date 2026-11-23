"""Pack one reviewed image-studio sheet into eight walk cells and a matching planted idle."""
import argparse, contextlib, importlib.util, re
from pathlib import Path
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
def main():
 ap=argparse.ArgumentParser();ap.add_argument('gender',choices=('male','female'));ap.add_argument('view',choices=('front','side','back'));ap.add_argument('--revision',default='r1');args=ap.parse_args()
 source=ROOT/'Tools/logs/qingshi-locomotion-20261123'/f'{args.gender}-{args.view}-{args.revision}'
 raw=Image.open(source/'source.png').convert('RGBA')
 spec=importlib.util.spec_from_file_location('user_image_studio','C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py');skill=importlib.util.module_from_spec(spec);spec.loader.exec_module(skill)
 parts=[]
 for i in range(9):
  x,y=i%3,i//3
  cell=raw.crop((round(x*raw.width/3),round(y*raw.height/3),round((x+1)*raw.width/3),round((y+1)*raw.height/3)))
  path=source/f'cell-{i}.png';cell.save(path)
  cut=source/f'cut-{i}.png'
  with contextlib.redirect_stdout(None): skill.make_transparent(str(path),str(cut))
  im=Image.open(cut).convert('RGBA')
  alpha=np.asarray(im)[:,:,3]
  yy,xx=np.where(alpha>48)
  if len(xx)<2000:raise SystemExit('Missing body '+str(i))
  box=(int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1));im=im.crop(box)
  if im.height<raw.height/3*.6 or im.width>raw.width/3*.95:raise SystemExit('Clipped or invalid body '+str(i))
  parts.append(im)
 # One scale per directional sheet retains the authored weight shift. Align the pelvis and the supporting sole.
 scale=460/max(p.height for p in parts)
 frames=[]
 for i,im in enumerate(parts):
  mask=np.asarray(im)[:,:,3]>48; centers=[]
  for row in mask[int(im.height*.43):int(im.height*.51)]:
   edges=np.diff(np.pad(row.astype(int),(1,1)));starts=np.flatnonzero(edges==1);ends=np.flatnonzero(edges==-1)
   if len(starts):
    at=int(np.argmax(ends-starts));centers.append((starts[at]+ends[at])/2)
  center=float(np.median(centers)) if centers else im.width/2
  resized=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
  frame=Image.new('RGBA',(384,512)); frame.alpha_composite(resized,(round(192-center*scale),500-resized.height));frames.append(frame)
  frame.save(source/f'frame-{i}.png')
 # The generated rear views repeat one support leg. Assemble one reviewed half-stride
 # and its opposite, as a symmetric rear-view cycle, to guarantee alternating feet/arms.
 if args.view=='back':
  half_indices=(0,5,4,2) if args.gender=='male' else (0,1,2,3)
  half=[frames[i] for i in half_indices]
  frames=half+[im.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for im in half]+[frames[8]]
 sheet=Image.new('RGBA',(384*8,512))
 for i,frame in enumerate(frames[:8]):sheet.alpha_composite(frame,(384*i,0))
 target=ROOT/'GameClient/TwelveJade/Assets/Resources/Art/Characters/Qingshi';stem=f'qingshi-{args.gender}-{args.view}'
 sheet.save(target/(stem+'-walk.png'));frames[8].save(target/(stem+'.png'))
 for asset in (stem+'-walk.png',stem+'.png'):
  meta=target/(asset+'.meta');text=meta.read_text(encoding='utf-8');text=re.sub(r'maxTextureSize: \d+', 'maxTextureSize: '+('4096' if asset.endswith('-walk.png') else '2048'), text);meta.write_text(text,encoding='utf-8')
 preview=Image.new('RGBA',(384*4,512*2),(223,218,201,255))
 for i,frame in enumerate(frames[:8]):preview.alpha_composite(frame,(384*(i%4),512*(i//4)))
 preview=preview.convert('RGB');preview.thumbnail((1200,1000));preview.save(source/'packed-review.jpg',quality=80,optimize=True)
 print('PACKED',stem,sheet.size,'source scale',round(scale,4))
if __name__=='__main__':main()
