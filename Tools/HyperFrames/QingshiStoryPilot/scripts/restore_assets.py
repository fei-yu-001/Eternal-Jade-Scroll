"""Restore reused local assets after extracting the small source archive; never modifies the game."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess
root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-root',default='D:/qiqi_me/HarmonyOS/apps/EternalJadeScroll')
parser.add_argument('--check-only',action='store_true')
args=parser.parse_args(); game=Path(args.game_root).resolve()
manifest=json.loads((root/'sources/asset-manifest.json').read_text(encoding='utf-8'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
for asset in manifest['assets']:
 dest=(root/asset['path']).resolve()
 if not dest.is_relative_to(root):raise SystemExit('Destination outside pilot refused')
 if dest.exists():
  if sha(dest)!=asset['sha256']:raise SystemExit('Existing modified asset preserved: '+asset['path'])
  continue
 if args.check_only:raise SystemExit('Missing asset: '+asset['path'])
 src=asset.get('sourceRelativeToGame')
 if src:
  source=(game/src).resolve()
  if not source.is_relative_to(game) or not source.is_file() or sha(source)!=asset['sha256']:
   raise SystemExit('Source missing or changed; inspect asset-manifest.json: '+str(source))
  dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
 elif asset['path']=='assets/audio/hidden-stream-pilot.wav':
  dest.parent.mkdir(parents=True,exist_ok=True)
  subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-n','-ss','18','-t','32.4','-i',str(root/'assets/audio/hidden-stream.mp3'),'-af','volume=-6.24dB','-ar','48000','-c:a','pcm_s16le',str(dest)],check=True)
  if sha(dest)!=asset['sha256']:raise SystemExit('Derived WAV differs with this FFmpeg version; file retained for inspection')
 else:raise SystemExit('Missing packaged vendor asset: '+asset['path'])
print('All 9 assets match the frozen source manifest; game files remain read-only.')
