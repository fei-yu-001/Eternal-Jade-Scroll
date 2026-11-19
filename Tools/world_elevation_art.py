"""Height presentation details in world coordinates; no per-chunk offsets or randomness."""
import numpy as np
from world_river_surface import dress_river

def decorate_terrain(canvas,x,y,config):
    out=canvas.copy()
    def paint(mask,color,opacity=1):
        if np.any(mask):out[mask]=out[mask]*(1-opacity)+np.array(color)*opacity
    for r in config.get('elevation',{}).get('regions',[]):
        if r['shape'] not in ('rect','ramp'):continue
        x0,y0,x1,y1=r['rect']
        if x1<x.min() or x0>x.max() or y1<y.min() or y0>y.max():continue
        inside=(x>=x0)&(x<x1)&(y>=y0)&(y<y1)
        if r['shape']=='ramp':
            axis=y-y0 if r.get('axis','y')=='y' else x-x0
            length=y1-y0 if r.get('axis','y')=='y' else x1-x0
            count=max(4,round(abs(r['to']-r['from'])*4));stride=length/count
            # The surface is physically sloped; transverse treads and edge stones make the approach legible.
            paint(inside,(146,148,136),.85)
            paint(inside & (np.mod(axis,stride)<2),(77,84,79),.85)
            paint(inside & (np.mod(axis,stride)>=2) & (np.mod(axis,stride)<4),(181,181,162),.7)
            edge=(x<x0+7)|(x>=x1-7) if r.get('axis','y')=='y' else (y<y0+7)|(y>=y1-7)
            paint(inside & edge,(96,106,99),.9)
        elif r['level']==2:
            border=inside & ((x<x0+18)|(x>=x1-18)|(y<y0+18)|(y>=y1-18))
            paint(border,(116,126,116),.85)
            joints=border & ((np.mod(x-x0,48)<2)|(np.mod(y-y0,32)<2))
            paint(joints,(75,88,79),.55)
    terrain=config['terrainShape'];river=terrain['river'];center=np.zeros_like(x)+river['baseY']
    for w in river['wave']:center+=w['amp']*np.sin(x/w['period']+w.get('phase',0))
    # 岸缘不再刷常量偏移的直带：湿岸线、浅滩、碎墨缘都由 world_river_surface 按世界坐标求值。
    out=dress_river(out,x,y,config)
    for b in terrain['bridges']:
        cy=river['baseY']+sum(w['amp']*np.sin(b['x']/w['period']+w.get('phase',0)) for w in river['wave'])
        rail=(np.abs(y-cy)<river['bankHalf']+18)&(np.abs(x-b['x'])>=b['half']-14)&(np.abs(x-b['x'])<b['half']-3)
        paint(rail,(123,131,121),.9)
        paint(rail & (np.mod(y,40)<4),(75,90,81),.75)
    return out
