"""Review-only south gate kitbash. Crops and uniform scaling; never writes Resources."""
import hashlib,json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from scipy import ndimage
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Tools/logs/qingshi-map-20261105/gate-candidate-wide-lintel'
SOURCE=ROOT/'GameClient/TwelveJade/Assets/ArtSource/incoming-20261104/town-gate-south-candidate-01.jpg'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    OUT.mkdir(parents=True,exist_ok=True)
    rgb=np.array(Image.open(SOURCE).convert('RGB'));white=(rgb.min(2)>237)&(np.ptp(rgb.astype(int),axis=2)<25)
    seed=np.zeros_like(white);seed[0]=white[0];seed[-1]=white[-1];seed[:,0]=white[:,0];seed[:,-1]=white[:,-1]
    bg=ndimage.binary_propagation(seed,mask=white);rgba=np.dstack((rgb,(~bg).astype('uint8')*255));src=Image.fromarray(rgba)
    out=Image.new('RGBA',(960,595));modules=[]
    def module(name,box,scale,xy):
        part=src.crop(box);size=(round(part.width*scale),round(part.height*scale));part=part.resize(size,Image.Resampling.LANCZOS)
        out.alpha_composite(part,xy);modules.append({'name':name,'sourceCrop':box,'uniformScale':scale,'destination':[xy[0],xy[1],*size]})
    # Replace the structurally narrow arch with a straight lintel; no elongated arch stones.
    module('upper watch-house with original roof',(0,20,640,307),1.4,(32,0))
    module('left stone pier',(140,320,255,575),.75,(84,398))
    module('right stone pier',(480,320,595,575),.75,(790,398))
    for i,x in enumerate((84,448)):
        module('lintel timber segment '+str(i+1),(179,275,543,303),1,(x,389))
    module('lintel timber end',(179,275,243,303),1,(812,389))
    out.save(OUT/'wide-gate-candidate.png')
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',20)
    board=Image.new('RGB',(1040,780),'#d6d3c3');board.paste(out,(40,48),out);draw=ImageDraw.Draw(board)
    draw.rectangle((210,465,830,637),outline='#186f44',width=3)
    draw.line((210,660,830,660),fill='#186f44',width=3);draw.text((340,668),'净宽 620 · 内容高 <= 595',font=font,fill='#12482a')
    draw.text((42,8),'宽门洞施工候选：平梁式重构 / 待架构师目视',font=font,fill='#292522')
    draw.text((40,714),'原图构件裁切 + 等比缩放；拱圈改平梁，非原门楼等比放大',font=font,fill='#292522')
    board.save(OUT/'candidate-review.png')
    x0,y0=3480,6425
    collision=[{'x0':-396,'y0':417,'x1':-310,'y1':589},{'x0':310,'y0':417,'x1':396,'y1':589}]
    overlay=board.copy();d=ImageDraw.Draw(overlay,'RGBA')
    for r in collision:d.rectangle((40+480+r['x0'],48+r['y0'],40+480+r['x1'],48+r['y1']),fill=(220,30,30,110),outline=(185,0,0,255),width=3)
    road=[]
    for y in range(417,590):
        cx=4096+140*math.sin((y0+y)/1500)-x0
        road.append((40+cx-250,48+y))
    for y in range(589,416,-1):
        cx=4096+140*math.sin((y0+y)/1500)-x0
        road.append((40+cx+250,48+y))
    d.polygon(road,fill=(30,135,230,80));d.text((42,744),'红=真实门墩阻挡   蓝=原500宽主街   绿框=620净空',font=font,fill=(30,35,42,255));overlay.save(OUT/'collision-review.png')
    current=ROOT/'GameClient/TwelveJade/Assets/Resources/Art/Props/qingshi-south-gate.png'
    alpha=Image.open(current).getchannel('A');bbox=alpha.point(lambda x:255 if x>128 else 0).getbbox();visual=(bbox[3]-bbox[1])/alpha.height*570
    report={'status':'CANDIDATE_REQUIRES_VISUAL_REVIEW','installed':False,'source':str(SOURCE.relative_to(ROOT)),'sourceSha256':sha(SOURCE),'currentRuntimeSha256':sha(current),'current':{'plotSize':[660,570],'alphaBbox':bbox,'opaqueContentHeight':visual,'opening':[-62,162],'clearWidth':224,'scaleNeededFor600':600/224,'resultingContentHeightFor600':visual*600/224,'maxClearWidthAt595':224*595/visual,'G-E3OverlapArea':79123},'candidate':{'path':str((OUT/'wide-gate-candidate.png').relative_to(ROOT)),'sha256':sha(OUT/'wide-gate-candidate.png'),'canvas':[960,595],'contentBbox':out.getbbox(),'clearWidth':620,'clearBandY':[417,589],'plotSuggestion':{'footX':3960,'footY':6425,'width':960,'height':595,'collisionRects':collision},'modules':modules,'limitations':['Review-only kitbash; arch architecture changed to lintel architecture.','Junction paint and timber continuity need visual review.','No runtime resource, plot, road, blocker or art baseline changed.','Upper lintel is rendered as overhead structure; only piers block ground movement.']}}
    a=np.array(out.getchannel('A'));report['candidate']['clearBandMaxAlpha']=int(a[417:589,170:790].max())
    assert report['candidate']['clearBandMaxAlpha']==0
    assert out.getbbox()[3]-out.getbbox()[1]<=595
    (OUT/'construction.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Review-only candidate: 620 clear width, original pixel aspect ratios, 2 real piers; not installed.')
if __name__=='__main__':main()
