from pathlib import Path
import subprocess, json, hashlib, statistics
root=Path(__file__).resolve().parent.parent
video=root/'renders/qingshi-story-pilot.mp4'
if not video.is_file() or video.stat().st_size<1000: raise SystemExit('Missing delivery MP4')
def run(*args): return subprocess.run(args,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
probe=json.loads(run('ffprobe','-v','error','-show_streams','-show_format','-of','json',str(video)).stdout)
(root/'reports/ffprobe.json').write_text(json.dumps(probe,indent=2),encoding='utf-8')
v=next(s for s in probe['streams'] if s['codec_type']=='video'); aud=[s for s in probe['streams'] if s['codec_type']=='audio']
assert (v['width'],v['height'])==(1920,1080)
assert v['r_frame_rate']=='30/1' and v['avg_frame_rate']=='30/1'
assert int(v['nb_frames'])==972 and abs(float(probe['format']['duration'])-32.4)<.05
assert len(aud)==1 and aud[0]['channels']==2
raw=run('ffmpeg','-v','error','-i',str(video),'-vf','scale=256:144,format=gray','-an','-f','rawvideo','pipe:1').stdout
size=256*144
assert len(raw)==972*size
frames=[memoryview(raw)[i:i+size] for i in range(0,len(raw),size)]
lumas=[sum(f)/size for f in frames]
mads=[0.0]+[sum(abs(a-b) for a,b in zip(frames[i-1],frames[i]))/size for i in range(1,len(frames))]
seams=[]
for index,start in enumerate([234,468,702],1):
 end=start+36
 means=lumas[start:end+1]
 # A dissolve may darken toward the next painting; it must not undershoot both endpoints.
 lower=min(lumas[start-1],lumas[end+1])
 dip=lower-min(means)
 assert dip<2.0, (index,'exposure dip',dip)
 boundary=[{'frame':f,'time':round(f/30,5),'meanAbsDiffFromPrevious':round(mads[f],5)} for f in [start-1,start,start+1,end-1,end,end+1]]
 mid_max=max(mads[start:end+1])
 assert max(x['meanAbsDiffFromPrevious'] for x in boundary)<max(1.0,mid_max*2.0), (index,'hard boundary')
 seams.append({'index':index,'start':start/30,'end':end/30,'lumaStart':round(lumas[start-1],4),'lumaEnd':round(lumas[end+1],4),'lumaMin':round(min(means),4),'undershoot':round(max(0,dip),4),'maxMidMAD':round(mid_max,5),'boundaries':boundary})
 select='+'.join('eq(n,%d)'%f for f in [start-1,start,start+18,end-1,end,end+1])
 dest=root/f'review/decoded-seam-{index}.jpg'
 run('ffmpeg','-v','error','-y','-i',str(video),'-vf',f"select='{select}',scale=236:132,tile=6x1",'-frames:v','1','-q:v','6',str(dest))
keyframes=[('key-01-home',4.5),('key-02-herbs',20.8),('key-03-care',30)]
for name,t in keyframes:
 dest=root/'review'/f'{name}.jpg'
 run('ffmpeg','-v','error','-y','-ss',str(t),'-i',str(video),'-frames:v','1','-vf','scale=1280:-2','-q:v','6',str(dest))
 if dest.stat().st_size>204800: run('ffmpeg','-v','error','-y','-ss',str(t),'-i',str(video),'-frames:v','1','-vf','scale=1200:-2','-q:v','9',str(dest))
# Validate the final shortened line from an actual delivery frame, not the HTML preview.
run('ffmpeg','-v','error','-y','-ss','26','-i',str(video),'-frames:v','1','-vf','scale=1280:-2','-q:v','6',str(root/'review/decoded-warm-26s.jpg'))
max_frozen=current=0
for d in mads[1:]:
 current=current+1 if d==0 else 0
 max_frozen=max(max_frozen,current)
assert max_frozen<30
report={'ok':True,'source':'actual delivery MP4; full 972-frame decode, 256x144 grayscale analysis','duration':float(probe['format']['duration']),'frames':int(v['nb_frames']),'resolution':[v['width'],v['height']],'fps':v['r_frame_rate'],'videoCodec':v['codec_name'],'pixelFormat':v['pix_fmt'],'audioTracks':len(aud),'audioCodec':aud[0]['codec_name'],'audioChannels':aud[0]['channels'],'audioSampleRate':aud[0]['sample_rate'],'fileBytes':video.stat().st_size,'sha256':hashlib.sha256(video.read_bytes()).hexdigest(),'minFrameLuma':round(min(lumas),4),'longestIdenticalFrameRun':max_frozen,'seams':seams,'keyframes':[{'path':'review/'+n+'.jpg','time':t} for n,t in keyframes]}
(root/'reports/delivery-qa.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
