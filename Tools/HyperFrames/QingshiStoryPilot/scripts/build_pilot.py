from pathlib import Path
import json, hashlib, html, struct
root=Path(__file__).resolve().parent.parent
repo=Path('D:/qiqi_me/HarmonyOS/apps/EternalJadeScroll')
book_path=repo/'GameClient/TwelveJade/Assets/Resources/Config/qingshi-story-panels.json'
book=json.loads(book_path.read_text(encoding='utf-8-sig'))
ids=['home-coming-of-age','livelihood-forge','livelihood-herbs','livelihood-father-care']
keys=['home','forge','herbs','care']
starts=[0,7.8,15.6,23.4]
selected=[]
for scene in book['scenes']:
    for index,panel in enumerate(scene['panels']):
        if panel['art'] in ids and not panel.get('choices'):
            selected.append({'scene':scene['id'],'index':index,**panel})
assert len(selected)==4
captions=[
 ('age',.4,2.6,'今日十八。',0),
 ('wish',3.3,4.2,'盼把病痛和灾厄都滚远些。',0),
 ('work',8.5,3.5,'风箱拉得肩膀发酸，',1),
 ('whetstone',12.3,3.0,'收工前，他把磨刀石推过来。',1),
 ('grind',16.3,2.9,'踩碾槽，捣药泥。',2),
 ('herbs',19.5,3.5,'药老又从锅底捞出一包药渣。',2),
 ('warm',24.1,4.1,'热气慢慢透进爹的膝头。',3),
 ('relief',28.5,3.5,'他攥了半天的手，终于松开一点。',3),
]
for _,_,_,text,source in captions: assert text in selected[source]['text'], text
variables=[{'id':'matte','type':'color','label':'画外暗色','default':'#211c18'}, {'id':'subtitleInk','type':'color','label':'字幕字色','default':'#fff4df'}]
for key,art in zip(keys,ids): variables.append({'id':key+'Art','type':'string','label':key+' / 原始分镜','default':'assets/art/'+art+'.png'})
for key,_,_,text,_ in captions: variables.append({'id':'caption-'+key,'type':'string','label':'字幕 / '+key,'default':text})
attr=html.escape(json.dumps(variables,ensure_ascii=False,separators=(',',':')),quote=True)
hosts=[]
for n,(key,start) in enumerate(zip(keys,starts)):
    hosts.append(f'    <div id="{key}" class="clip scene-host" data-composition-id="{key}" data-composition-src="compositions/{key}.html" data-start="{start}" data-duration="9" data-width="1920" data-height="1080" data-track-index="{n%2}" data-track-kind="graphics" data-layout-allow-overlap style="z-index:{n+1}"></div>')
main=f'''<!doctype html>
<html lang="zh-CN" data-resolution="landscape" data-composition-variables="{attr}">
<head><meta charset="UTF-8"><meta name="viewport" content="width=1920,height=1080">
<title>青石故里 · 一点暖意</title>
<script src="assets/vendor/gsap.min.js"></script>
<style>
@font-face {{ font-family: "Qingshi Serif"; src: url("assets/fonts/NotoSerifCJKsc-Regular.otf") format("opentype"); font-weight:400; font-style:normal; font-display:block; }}
:root {{ --matte:#211c18; --subtitleInk:#fff4df; }}
* {{ box-sizing:border-box; }}
html,body {{ width:1920px; height:1080px; margin:0; overflow:hidden; background:var(--matte); }}
#root {{ position:relative; width:1920px; height:1080px; overflow:hidden; background:var(--matte); }}
.scene-host,.caption-host {{ position:absolute; inset:0; width:100%; height:100%; }}
</style></head>
<body><div id="root" data-composition-id="main" data-start="0" data-duration="32.4" data-width="1920" data-height="1080" data-fps="30">
{chr(10).join(hosts)}
    <div id="captions" class="clip caption-host" data-composition-id="captions" data-composition-src="compositions/captions.html" data-start="0" data-duration="32.4" data-width="1920" data-height="1080" data-track-index="3" data-track-kind="captions" style="z-index:10" data-layout-allow-overlap></div>
    <audio id="hidden-stream-bed" class="clip" src="assets/audio/hidden-stream-pilot.wav" data-start="0" data-duration="32.4" data-track-index="4" data-volume="1" data-fade-in="1.2" data-fade-out="2.4" preload="auto"></audio>
</div><script>
window.__timelines = window.__timelines || {{}};
const tl = gsap.timeline({{paused:true}});
tl.to({{clock:0}}, {{clock:32.4,duration:32.4,ease:"none"}}, 0);
window.__timelines["main"] = tl;
</script></body></html>
'''
(root/'index.html').write_text(main,encoding='utf-8')
for n,(key,art) in enumerate(zip(keys,ids)):
    # Uniform camera direction survives the handoff; every head/hand remains inside frame.
    # Original 3:2 image is contained in 16:9, without repainting/cropping a person.
    entry='' if n==0 else f'tl.fromTo("#{key}-surface", {{opacity:0}}, {{opacity:1,duration:1.2,ease:"sine.inOut"}}, 0);'
    scene=f'''<template>
<style>
#root {{ position:absolute; inset:0; overflow:hidden; }}
#{key}-surface {{ position:absolute; inset:0; background:var(--matte,#211c18); }}
#{key}-window {{ position:absolute; left:150px; top:0; width:1620px; height:1080px; overflow:hidden; }}
#{key}-art {{ position:absolute; left:60px; top:40px; width:1500px; height:1000px; object-fit:contain; transform-origin:50% 30%; }}
</style>
<div id="root" data-composition-id="{key}" data-width="1920" data-height="1080" data-duration="9">
  <div id="{key}-surface" data-layout-allow-overlap>
    <div id="{key}-window"><img id="{key}-art" src="assets/art/{art}.png" data-var-src="{key}Art" alt="{html.escape(selected[n]['speaker'])}" draggable="false"></div>
  </div>
</div>
<script>
const tl = gsap.timeline({{paused:true}});
// Adapted from registry transitions-dissolve: incoming layer alone rises to opacity 1.
// Keeping the outgoing exposure intact avoids a dip to black and preserves continuous motion.
{entry}
tl.fromTo("#{key}-art", {{x:10,y:0,scale:1}}, {{x:-10,y:-1,scale:1.018,duration:9,ease:"none"}}, 0);
window.__timelines = window.__timelines || {{}};
window.__timelines["{key}"] = tl;
</script>
</template>
'''
    (root/f'compositions/{key}.html').write_text(scene,encoding='utf-8')
cue_html=[];cue_tweens=[]
for key,start,duration,text,_ in captions:
    cue_html.append(f'  <div id="cue-{key}" class="clip cue" data-start="{start}" data-duration="{duration}" data-track-index="0"><p id="ink-{key}" data-var-text="caption-{key}">{text}</p></div>')
    cue_tweens.append(f'tl.fromTo("#ink-{key}", {{opacity:0}}, {{opacity:1,duration:.3,ease:"sine.inOut"}}, {start});')
    cue_tweens.append(f'tl.to("#ink-{key}", {{opacity:0,duration:.3,ease:"sine.inOut"}}, {start+duration-.3:.3f});')
caption_doc=f'''<template>
<style>
@font-face {{ font-family:"Qingshi Serif"; src:url("assets/fonts/NotoSerifCJKsc-Regular.otf") format("opentype"); font-weight:400; font-style:normal; font-display:block; }}
#root {{ position:absolute; inset:0; pointer-events:none; font-family:"Qingshi Serif",serif; }}
.cue {{ position:absolute; left:192px; top:894px; width:1536px; height:74px; text-align:center; display:flex; align-items:center; justify-content:center; }}
.cue p {{ display:inline-block; max-width:1480px; margin:0; padding:7px 24px 9px; font-size:40px; font-weight:400; line-height:56px; letter-spacing:1px; white-space:nowrap; color:var(--subtitleInk,#fff4df); background:rgba(29,23,18,.87); }}
</style>
<div id="root" data-composition-id="captions" data-width="1920" data-height="1080" data-duration="32.4">
{chr(10).join(cue_html)}
</div><script>
const tl = gsap.timeline({{paused:true}});
{chr(10).join(cue_tweens)}
window.__timelines = window.__timelines || {{}};
window.__timelines["captions"] = tl;
</script></template>
'''
(root/'compositions/captions.html').write_text(caption_doc,encoding='utf-8')
ledger={'sourceJson':str(book_path),'sourceJsonSha256':hashlib.sha256(book_path.read_bytes()).hexdigest(),'sourcePageCount':sum(len(s['panels']) for s in book['scenes']),'scope':'only home/livelihood excerpt; no future scenes copied','panels':selected,'captions':[dict(id=k,start=s,duration=d,text=t,sourcePanel=selected[i]['art']) for k,s,d,t,i in captions],'assets':[],'audio':{'source':'assets/audio/hidden-stream.mp3','trimStart':18,'trimDuration':32.4,'gainDb':-6.24,'sampleRate':48000,'fadeIn':1.2,'fadeOut':2.4,'speech':False,'sfx':False}}
for rel in [*(f'assets/art/{x}.png' for x in ids),'assets/audio/hidden-stream.mp3','assets/audio/hidden-stream-pilot.wav','assets/fonts/NotoSerifCJKsc-Regular.otf','assets/vendor/gsap.min.js']:
    p=root/rel;data=p.read_bytes();record={'path':rel,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
    if p.suffix=='.png': record['size']=struct.unpack('>II',data[16:24])
    ledger['assets'].append(record)
(root/'sources/ledger.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'duration':32.4,'frames':972,'scenes':4,'captions':len(captions),'sourcePages':ledger['sourcePageCount']},ensure_ascii=False))
