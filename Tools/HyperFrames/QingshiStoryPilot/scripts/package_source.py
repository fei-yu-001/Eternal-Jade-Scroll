from pathlib import Path
import json,hashlib,zipfile
root=Path(__file__).resolve().parent.parent
files=['.gitignore','AGENTS.md','index.html','index.motion.json','package.json','hyperframes.json','meta.json','README.md','SPEC.md','STORYLINES.md','REVIEW.md','COPYLIST.md']
files += ['compositions/'+key+ext for key in ['home','forge','herbs','care'] for ext in ['.html','.motion.json']]
files += ['compositions/captions.html','assets/vendor/gsap.min.js','assets/fonts/LICENSE.txt']
files += ['scripts/'+name for name in ['hf.mjs','build_pilot.py','restore_assets.py','verify_delivery.py','package_source.py']]
files += ['sources/'+name for name in ['ledger.json','asset-manifest.json','choice-boundaries.json','registry/transitions-dissolve.html']]
files += ['reports/'+name for name in ['check-final.json','snapshot-final-caption.json','ffprobe.json','delivery-qa.json','audio-delivery.json','tooling.json','preview-http.json']]
missing=[name for name in files if not (root/name).is_file()]
if missing:raise SystemExit('Source package waits for: '+', '.join(missing))
manifest={'format':1,'sourceFileCount':len(files),'excluded':['assets/art/*.png (official copies)','assets/audio/* (official copy + deterministic local derivative)','assets/fonts/*.otf (official copy)','.hyperframes/','review/','renders/'],'files':[{'path':name,'bytes':(root/name).stat().st_size,'sha256':hashlib.sha256((root/name).read_bytes()).hexdigest()} for name in files]}
out=root/'source-package';out.mkdir(exist_ok=True)
manifest_text=json.dumps(manifest,ensure_ascii=False,indent=2)
(out/'source-files.json').write_text(manifest_text,encoding='utf-8')
archive=out/'qingshi-story-pilot-source.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
 for name in files:
  info=zipfile.ZipInfo(name,date_time=(2026,11,27,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
  z.writestr(info,(root/name).read_bytes())
 z.writestr('SOURCE-MANIFEST.json',manifest_text)
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 for entry in manifest['files']:
  assert hashlib.sha256(z.read(entry['path'])).hexdigest()==entry['sha256']
print(json.dumps({'path':str(archive),'sourceFiles':len(files),'archiveBytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()},ensure_ascii=False))
