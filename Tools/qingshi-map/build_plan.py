"""Deterministic constrained layout installer; never changes gate thresholds or asset identities.
Run after install_assets.py + geometry generation/sync. --install writes runtime JSON.
Input snapshot is preserved in this folder; every proposed placement is audited before installation.
"""
import argparse,copy,hashlib,importlib.util,json,math,sys
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools'))
CFG=ROOT/'GameClient/TwelveJade/Assets/Resources/Config';RES=CFG.parent
LOG=ROOT/'Tools/logs/qingshi-map-20261105';HERE=Path(__file__).resolve().parent
sp=importlib.util.spec_from_file_location('overlap',ROOT/'Tools/check-town-overlap.py');gate=importlib.util.module_from_spec(sp);sp.loader.exec_module(gate)

def read(p):return json.loads(p.read_text('utf-8-sig'))
def write(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def bounds(p):return gate.rect_of(p)
def intersects(a,b,pad=0):return a[0]<b[2]+pad and a[2]>b[0]-pad and a[1]<b[3]+pad and a[3]>b[1]-pad

CLASSES={'Art/Buildings/town-gate-south-wide':'two-storey','Art/Props/teahouse-v2':'two-storey','Art/Props/stone-house-v2':'two-storey','Art/Props/residence':'courtyard','Art/Props/paifang':'gateway','Art/Props/temple-gate':'gateway','Art/Props/shop-front':'single-storey','Art/Props/shop-front-v2':'single-storey'}
def apply_footprint(plot,res,resize=True):
 p=dict(plot);im=Image.open(res/(p['art']+'.png')).convert('RGBA');a=np.asarray(im)[:,:,3]
 bbox=Image.fromarray((a>128).astype('uint8')*255).getbbox();w,h=im.size;ch=bbox[3]-bbox[1]
 oldfoot=p['footY']+p['height']
 if p['kind']=='building':
  cls=CLASSES[p['art']];p['buildingClass']=cls
  if resize and cls=='two-storey':
   # Keep the physical foot fixed while the actual upper storey grows to four traveler heights.
   scale=680/ch;p['width']=round(w*scale,4);p['height']=round(h*scale,4);p['footY']=round(oldfoot-p['height'],4)
  elif resize:
   # Remove any accidental aspect distortion without changing the existing visual height.
   p['width']=round(p['height']*w/h,4)
 sx,sy=p['width']/w,p['height']/h
 fraction=.15 if p['kind']=='building' else (.12 if p['kind']=='tree' else .18)
 y0=max(bbox[1],math.floor(bbox[3]-ch*fraction));y1=bbox[3]
 selected=a[y0:y1]>128;xs=np.where(selected.any(axis=0))[0];x0=int(xs.min());x1=int(xs.max())+1
 top=round(y0*sy,4);bottom=round(y1*sy,4)
 rects=[]
 if p['art']=='Art/Buildings/town-gate-south-wide':
  # Per-row true zero-alpha interval; two nonempty piers follow actual opening, never a blanket hole.
  runs=[]
  for y in range(y0,y1):
   left=w//2;right=left+1
   while left>0 and a[y,left-1]==0:left-=1
   while right<w and a[y,right]==0:right+=1
   if left==w//2:raise ValueError('Door center is opaque in required foot band')
   solid=np.flatnonzero(a[y]>128)
   row=(int(solid[0]),left,right,int(solid[-1])+1)
   if runs and runs[-1]['edges']==row and runs[-1]['y1']==y:runs[-1]['y1']=y+1
   else:runs.append({'y0':y,'y1':y+1,'edges':row})
  for row in runs:
   l,innerl,innerr,r=row['edges']
   for ax,bx,role in [(l,innerl,'west-pier'),(innerr,r,'east-pier')]:
    rects.append({'x0':round((ax-w/2)*sx,4),'y0':round(row['y0']*sy,4),'x1':round((bx-w/2)*sx,4),'y1':round(row['y1']*sy,4),'role':role})
 elif p['art']=='Art/Props/paifang' and p.get('collisionRects'):
  # Existing four measured solid legs are preserved horizontally; only their ground contact depth changes.
  for r in p['collisionRects']:
   rects.append({'x0':r['x0'],'y0':top,'x1':r['x1'],'y1':bottom,'role':'arch-pier'})
 else:rects=[{'x0':round((x0-w/2)*sx,4),'y0':top,'x1':round((x1-w/2)*sx,4),'y1':bottom,'role':'ground-footprint'}]
 p['collisionTop']=top;p['collisionDepth']=bottom;p['collisionRects']=rects;p['radius']=0
 if p['kind'] in ('tree','prop','stall') and p['art']!='Art/Props/qingshi-courtyard-wall':
  r=rects[0];radius=min((r['y1']-r['y0'])/2,(r['x1']-r['x0'])/2)
  p['collisionCapsule']={'x0':round(r['x0']+radius,4),'x1':round(r['x1']-radius,4),'y':round((r['y0']+r['y1'])/2,4),'radius':round(radius,4)}
 else:p.pop('collisionCapsule',None)
 p['groundFootOffset']=bottom
 p['footprint']={'version':1,'shape':'capsule' if 'collisionCapsule' in p else 'rectangles','bottomFraction':fraction,'sourceSha256':hashlib.sha256((res/(p['art']+'.png')).read_bytes()).hexdigest(),'alphaThreshold':128,'sourceBand':[y0,y1]}
 return p

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--install',action='store_true');args=ap.parse_args()
    data=read(HERE/'plots-before.json');town=read(CFG/'town-map.json');ts=town['terrainShape']
    plots={p['id']:copy.deepcopy(p) for p in data['plots']};before=copy.deepcopy(plots)
    # Key anchors chosen from the plan; all width/height changes preserve source aspect ratio.
    targets={
      'plot-ancestral-hall':(3420,1710,640),'plot-paifang':(3750,3490,660),
      'plot-blacksmith':(3650,2880,520),'shop-north-street-01':(4640,3650,420),
      'plot-temple-gate':(5250,3710,526),'plot-herbal-garden':(5250,4860,377),
      'plot-teahouse':(4630,4750,520),'plot-stone-house':(2800,3630,433),
      'plot-school':(2810,2960,390),'plot-street-square-tea':(3510,4370,340),
      'plot-street-locust-tea':(2710,4950,300),'plot-street-east-wine':(5200,5370,520),
      'plot-street-east-gate':(6220,4950,520),'plot-street-east-store':(5300,6100,470),
      'plot-street-east-home':(5500,5320,330),'plot-opera-stage':(3080,5430,400),
      'plot-street-opera-west':(3580,5430,400),'plot-grain-shop':(3220,6370,440),
      'plot-cloth-shop':(4730,6330,520),
      'plot-pine-south-01':(3162,7170,None),'plot-pine-south-02':(4790,6900,None),
      'plot-market-cart':(2780,4810,None),'plot-fortune-stall':(3470,4800,None),
      'market-stall-south-01':(2800,5130,None),'market-stall-south-02':(3140,5130,None),
      'market-stall-south-03':(3490,5110,None),'market-stall-south-04':(3520,4870,None),
      'plot-drying-rack':(5100,5270,None),'plot-forge-anvil':(3380,3320,None),
      'plot-firewood':(2110,3880,None),'plot-ancient-locust':(2750,4170,420),
      'plot-washing-stone':(5320,2770,None),'plot-wharf-reeds':(5340,2940,None),
      'plot-bridge-lion-west':(4020,2780,None),'plot-bridge-lion-east':(4480,2770,None),
      'plot-village-well':(2660,4650,None), 'plot-notice-board':(3570,4680,None),
    }
    new=[dict(id='plot-courtyard-wall-west',kind='prop',name='宗祠西侧护墙',footX=2840,footY=2090,width=480,height=240,art='Art/Props/qingshi-courtyard-wall',radius=0),
         dict(id='plot-town-gate-south',kind='building',name='青石镇南门楼',footX=3960,footY=6360,width=960,height=595,art='Art/Buildings/town-gate-south-wide',radius=0,collisionRects=[dict(x0=-396,y0=417,x1=-310,y1=589),dict(x0=310,y0=417,x1=396,y1=589)]),
         dict(id='plot-market-vignette',kind='prop',name='西市酒坛石磨柴棚',footX=3500,footY=4800,width=300,height=300,art='Art/Props/qingshi-market-vignette',radius=0)]
    plots.update({p['id']:p for p in new})
    # The old central arch gains real piers instead of its legacy blocks:false blanket bypass.
    arch=plots['plot-paifang'];arch['blocks']=True
    arch['collisionRects']=[dict(x0=a,y0=320,x1=b,y1=653) for a,b in [(-165,-135),(-100,-65),(50,85),(120,145)]]
    for pid,p in plots.items():
        if pid in targets:
            x,y,h=targets[pid];p['footX']=x;p['footY']=y
            if h is not None:
                p['width']=round(p['width']*h/p['height']);p['height']=h
        if p['kind']=='tree' and p['height']>300 and pid!='plot-ancient-locust':
            p['width']=round(p['width']*300/p['height']);p['height']=300
        p.update(apply_footprint(p,RES))
    corridors=np.array([r[:4] for r in gate.corridor_rects(ts)])
    blocked=np.zeros((1026,1026),dtype=np.int32)
    for b in town['blocked']:
        if isinstance(b,dict):x,y,w,h=[b[k] for k in ['x','y','width','height']]
        else:x,y,w,h=b
        blocked[math.floor(y/8):math.ceil((y+h)/8)+1,math.floor(x/8):math.ceil((x+w)/8)+1]=1
    integral=np.pad(blocked.cumsum(0).cumsum(1),((1,0),(1,0)))
    def blocked_rect(r):
        x0,y0=max(0,int(r[0]/8)),max(0,int(r[1]/8));x1,y1=min(1025,math.ceil(r[2]/8)+1),min(1025,math.ceil(r[3]/8)+1)
        return integral[y1,x1]-integral[y1,x0]-integral[y0,x1]+integral[y0,x0]>0
    pair={'plot-market-cart':'huolang','plot-fortune-stall':'npc-shenhuai-an','plot-firewood':'npc-shitou','plot-forge-anvil':'npc-zhao-da','plot-drying-rack':'npc-yao-lao',
          'market-stall-south-01':'npc-liu-sao','market-stall-south-02':'npc-a-xing','market-stall-south-03':'npc-he-er-niang','market-stall-south-04':'npc-hu-san'}
    npcs={n['id']:n for n in town['npcs']}
    probes=[(n['x']-60,n['y']-170,n['x']+60,n['y']+8) for n in town['npcs'] if n['id'] not in pair.values()]
    probes += [(e['x']-40,e['y']-40,e['x']+40,e['y']+40) for e in town['encounters']]
    placed=[]
    def paired_rect(p):
        x=p['footX']-110;y=p['footY']
        return (x-48,y-170,x+48,y+8)
    def valid(p):
        r=bounds(p)
        if min(r)<16 or r[2]>8176 or r[3]>8176:return False
        if blocked_rect((p['footX'],p['footY'],p['footX']+1,p['footY']+1)):return False
        if any((p['footX']-l['x'])**2+(p['footY']-l['y'])**2<60**2 for l in town['landmarks']):return False
        brs=gate.blocker_rects_of(p)
        for b in brs:
            if blocked_rect(b):return False
            intersect_area=np.maximum(0,np.minimum(b[2],corridors[:,2])-np.maximum(b[0],corridors[:,0]))*np.maximum(0,np.minimum(b[3],corridors[:,3])-np.maximum(b[1],corridors[:,1]))
            if intersect_area.sum()>0:return False
        for q in placed:
            if intersects(r,bounds(q),10):return False
            if p['kind'] in ('building','tree','stall') and q['kind'] in ('building','tree','stall'):
                for b in brs:
                    for qb in gate.blocker_rects_of(q):
                        dx=max(b[0]-qb[2],qb[0]-b[2],0);dy=max(b[1]-qb[3],qb[1]-b[3],0)
                        if dx*dx+dy*dy<60**2:return False
        if any(intersects(r,probe,8) for probe in probes):return False
        if p['id'] in pair:
            nr=paired_rect(p)
            if blocked_rect(nr):return False
            if any(intersects(nr,bounds(q),8) for q in placed):return False
            if any(intersects(nr,probe,8) for probe in probes):return False
        # Solid terraces cannot receive half-supported buildings or trees.
        for reg in town['elevation']['regions']:
            if reg['shape']=='rect' and reg['level']==2:
                t=reg['rect']
                if intersects(r,t) and not (r[0]>=t[0] and r[2]<=t[2] and r[1]>=t[1] and r[3]<=t[3]):return False
        for reg in town['elevation']['regions']:
            if reg['shape']=='ramp' and any(intersects(b,reg['rect'],12) for b in brs):return False
        return True
    priority=['plot-town-gate-south','plot-ancestral-hall','plot-courtyard-wall-west','plot-paifang','plot-blacksmith','shop-north-street-01','plot-temple-gate','plot-market-vignette','plot-teahouse','plot-opera-stage','plot-street-square-tea']
    order=priority+[pid for pid in plots if pid not in priority and plots[pid]['kind']=='building']+list(pair)
    order+= [pid for pid in plots if pid not in order]
    offsets=sorted([(x,y) for x in range(-1500,1501,20) for y in range(-1500,1501,20)],key=lambda d:(d[0]**2+d[1]**2,abs(d[1]),d))
    moves=[]
    for pid in order:
        p=plots[pid];ox,oy=p['footX'],p['footY']
        found=False
        for dx,dy in offsets:
            p['footX']=ox+dx;p['footY']=oy+dy
            if pid in ('plot-town-gate-south','plot-ancestral-hall','plot-courtyard-wall-west') and (dx or dy):continue
            if pid in ('plot-paifang','plot-blacksmith','shop-north-street-01') and (abs(dx)>80 or abs(dy)>100):continue
            if valid(p):found=True;break
        if not found:raise RuntimeError('No legal functional placement: '+pid+f' near {ox},{oy}')
        placed.append(p)
        if pid in pair:
            n=npcs[pair[pid]];n['x']=p['footX']-110;n['y']=p['footY'];probes.append(paired_rect(p))
        old=before.get(pid);moves.append(dict(id=pid,from_=[old['footX'],old['footY']] if old else None,to=[p['footX'],p['footY']],size=[p['width'],p['height']],target_delta=[p['footX']-ox,p['footY']-oy]))
    e2=gate.check_e2(placed);e3=gate.check_e3(placed,gate.corridor_rects(ts))
    if e2:raise RuntimeError(f'Gate failed E2={e2}')
    if e3:raise RuntimeError(f'Corridor conflicts {e3}')
    for npc in town['npcs']:
        art=npc.get('art','')
        if art.startswith('Art/') and '/' not in art[4:]:
            canonical='Art/Characters/Stand/'+art[4:]
            if not (RES/(canonical+'.png')).is_file():raise RuntimeError('Missing NPC stand art: '+canonical)
            npc['art']=canonical
    data['plots']=sorted(placed,key=lambda p:(p['footY'],p['id']))
    data['comment']='2026-11-05 青石镇落地。footX/Y 为画布顶边中点；地面锚点=footY+height；绘制减去落脚点高度。collisionRects 优先于 alpha 单盒；门墩实挡、门洞可通行。1身位=170=1.75米；二层3.5–4.5，其他1.5–3.5；碰撞仅底部15%，groundFootOffset为真实落脚点。来源与功能见 Tools/qingshi-map/build_plan.py。'
    write(LOG/'layout-installed.json',data);write(LOG/'plot-moves.json',moves)
    if args.install:
        write(CFG/'building-plots.json',data);write(CFG/'town-map.json',town)
        # Day schedules must follow the now-relocated shop, not pull the NPC back to an old landmark.
        schedules=read(CFG/'npc-schedules.json')
        for sch in schedules['schedules']:
            n=npcs[sch['npcId']];home=min(town['landmarks'],key=lambda l:(l['x']-n['x'])**2+(l['y']-n['y'])**2)['id']
            for w in sch['windows']:
                if not w.get('black'):w['place']=home
        write(CFG/'npc-schedules.json',schedules)
    print(f'Layout {len(placed)} plots, E2={len(e2)}, E3={len(e3)}, installed={args.install}')
    write(LOG/'layout-pending-conflicts.json',{'status':'FAIL' if e3 else 'PASS','G-E2':len(e2),'G-E3':len(e3),'conflicts':[v.text for v in e3]})
    for m in moves:
        if m['id'] in priority:print(m)
    if e3:raise SystemExit('RESULT: FAIL; actual blockers intersect the approved corridors')
if __name__=='__main__':main()
