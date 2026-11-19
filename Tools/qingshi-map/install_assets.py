"""青石镇四张来源的可复跑接入。默认只准备；--install 仅安装3张RGBA；地表由独立显式入口绑定。
所有处理本地完成，不生图、不拉伸，保留原JPG和已有.meta。
"""
import argparse, hashlib, json, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'GameClient/TwelveJade/Assets/ArtSource/incoming-20261104'
OUT=ROOT/'GameClient/TwelveJade/Assets/ArtSource/qingshi-map-20261105'
EVIDENCE=ROOT/'Tools/logs/qingshi-map-20261105'
PROPS=ROOT/'GameClient/TwelveJade/Assets/Resources/Art/Props'
MATERIAL=ROOT/'GameClient/TwelveJade/Assets/ArtSource/world/materials'
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def cutout(path,size):
    rgb=np.array(Image.open(path).convert('RGB'),dtype=np.float32)
    low=rgb.min(2);spread=rgb.max(2)-low
    white=(low>=237)&(spread<25)
    seed=np.zeros(white.shape,bool);seed[0]=white[0];seed[-1]=white[-1];seed[:,0]|=white[:,0];seed[:,-1]|=white[:,-1]
    bg=ndimage.binary_propagation(seed,mask=white)
    # Fully enclosed white holes are background only when bright and spatially isolated;
    # cream plaster remains foreground. This removes gaps between roof braces/table legs.
    labels,n=ndimage.label((low>=249)&(spread<7))
    for i in range(1,n+1):
        component=labels==i
        if component.sum()>=5:bg|=component
    alpha=(~bg).astype(np.float32)
    edge=ndimage.binary_dilation(bg,iterations=1)&~bg
    alpha[edge]=np.clip((255-low[edge])/40,.15,1)
    # Decontaminate the white matte only on the outer antialias band.
    clean=rgb.copy(); a=alpha[edge,None]
    clean[edge]=np.clip((rgb[edge]-255*(1-a))/np.maximum(a,.15),0,255)
    if path.name=='town-gate-south-candidate-01.jpg':
        doors=Image.new('L',(rgb.shape[1],rgb.shape[0]));draw=ImageDraw.Draw(doors)
        draw.polygon([(263,391),(281,379),(310,374),(310,576),(268,576)],fill=255)
        draw.polygon([(398,401),(472,384),(473,574),(389,574)],fill=255)
        draw.polygon([(270,382),(290,369),(318,363),(341,364),(365,375),(386,394),(399,416),(396,573),(270,576)],fill=255)
        alpha[np.asarray(doors)>0]=0
    rgba=np.dstack([clean,alpha*255]).astype(np.uint8)
    im=Image.fromarray(rgba);box=im.getbbox()
    if not box:raise ValueError('empty cutout: '+str(path))
    im=im.crop(box); target=Image.new('RGBA',size)
    margin=max(8,int(size[0]*.012)+1);scale=min((size[0]-2*margin)/im.width,(size[1]-2*margin)/im.height)
    im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
    target.alpha_composite(im,((size[0]-im.width)//2,size[1]-margin-im.height))
    return target

def periodic_tile(path):
    im=Image.open(path).convert('RGB');side=min(im.size)
    im=im.crop(((im.width-side)//2,(im.height-side)//2,(im.width+side)//2,(im.height+side)//2))
    a=np.asarray(im,dtype=np.float64)
    # Periodic-plus-smooth decomposition removes opposing boundary discontinuity.
    v=np.zeros_like(a);v[0]=a[-1]-a[0];v[-1]=-v[0]
    v[:,0]+=a[:,-1]-a[:,0];v[:,-1]-=a[:,-1]-a[:,0]
    yy,xx=np.indices((side,side));den=2*np.cos(2*np.pi*xx/side)+2*np.cos(2*np.pi*yy/side)-4
    den[0,0]=1
    smooth=np.fft.fft2(v,axes=(0,1))/den[:,:,None];smooth[0,0]=0
    a-=np.fft.ifft2(smooth,axes=(0,1)).real
    lum=a@np.array([.2126,.7152,.0722]);a=(a-lum.mean())*.62+140
    # Exact cyclic edge values with a narrow mirrored seam taper (not a whole-image blur).
    for k in range(5):
        weight=(1-k/5)*.5
        left=a[:,k].copy();right=a[:,-1-k].copy()
        a[:,k]=left*(1-weight)+right*weight;a[:,-1-k]=right*(1-weight)+left*weight
        top=a[k].copy();bottom=a[-1-k].copy()
        a[k]=top*(1-weight)+bottom*weight;a[-1-k]=bottom*(1-weight)+top*weight
    return Image.fromarray(np.clip(a,0,255).astype(np.uint8)).resize((1024,1024),Image.Resampling.LANCZOS)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--install',action='store_true');parser.add_argument('--prepare-provisional-ground',action='store_true');args=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);EVIDENCE.mkdir(parents=True,exist_ok=True)
    specs=[('wall-courtyard-v1.jpg','qingshi-courtyard-wall.png',(960,480)),('town-gate-south-candidate-01.jpg','qingshi-south-gate.png',(1320,1140)),('props-street-vignette-01.jpg','qingshi-market-vignette.png',(720,720))]
    manifest=[]
    for source,name,size in specs:
        im=cutout(SOURCE/source,size);dest=OUT/name;im.save(dest)
        alpha=np.array(im.getchannel('A'));bbox=im.getchannel('A').point(lambda x:255 if x>128 else 0).getbbox()
        assert alpha[0].max()==alpha[-1].max()==alpha[:,0].max()==alpha[:,-1].max()==0
        manifest.append(dict(source=source,source_sha256=sha(SOURCE/source),output=name,door_leaves_removed=(source=='town-gate-south-candidate-01.jpg'),sha256=sha(dest),size=size,alpha_bbox=bbox,alpha_fraction=float((alpha>128).mean())))
        if args.install:shutil.copy2(dest,PROPS/name)
    ground=None
    if args.prepare_provisional_ground:
        ground=periodic_tile(SOURCE/'ground-qingshi-flagstone-raw.jpg');ground.save(OUT/'qingshi-flagstone-tile.png')
        a=np.asarray(ground,dtype=float);manifest.append(dict(source='ground-qingshi-flagstone-raw.jpg',source_sha256=sha(SOURCE/'ground-qingshi-flagstone-raw.jpg'),output='qingshi-flagstone-tile.png',sha256=sha(OUT/'qingshi-flagstone-tile.png'),mean=float((a@np.array([.2126,.7152,.0722])).mean()),edge_lr=float(np.abs(a[:,0]-a[:,-1]).mean()),edge_tb=float(np.abs(a[0]-a[-1]).mean())))
    preview=Image.new('RGB',(1500,1000),'#d7d2c5');d=ImageDraw.Draw(preview)
    for i,(_,name,_) in enumerate(specs):
        im=Image.open(OUT/name);im.thumbnail((470,450))
        for row,bg in enumerate(['#182c29','#e8dcc0']):
            x=i*500;y=row*500;d.rectangle((x,y,x+499,y+499),fill=bg)
            preview.paste(im,(x+(500-im.width)//2,y+30),im)
            d.text((x+8,y+475),name,fill='#8b8b80')
    preview.save(EVIDENCE/'assets-dual-background.jpg',quality=92)
    if ground is not None:
        tile=Image.new('RGB',(1024,1024));thumb=ground.resize((512,512))
        for y in [0,512]:
            for x in [0,512]:tile.paste(thumb,(x,y))
        tile.save(EVIDENCE/'flagstone-tile-2x2.jpg',quality=94)
    (EVIDENCE/'asset-install-manifest.json').write_text(json.dumps(dict(installed=args.install,assets=manifest),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Prepared 3 RGBA sprites; installed='+str(args.install)+'; stone-road unchanged')
if __name__=='__main__':main()
