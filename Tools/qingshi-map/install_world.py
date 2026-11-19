"""Install the 64 freshly composed chunks and retain every existing Unity GUID."""
import datetime,hashlib,json,shutil,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];CFG=ROOT/'GameClient/TwelveJade/Assets/Resources/Config'
SRC=ROOT/'GameClient/TwelveJade/Assets/ArtSource/world/chunks-full';DST=CFG.parent/'Art/World/Chunks';LOG=ROOT/'Tools/logs/qingshi-map-20261105'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
sys.path.insert(0,str(ROOT/'Tools'))
import world_build_manifest as provenance
def main():
    generated=provenance.validate(SRC)
    files=[SRC/f'chunk_{x}_{y}.png' for y in range(8) for x in range(8)]
    if not all(p.exists() for p in files):raise SystemExit('Full 64-chunk output required')
    DST.mkdir(parents=True,exist_ok=True);rows=[]
    for p in files:
        dest=DST/p.name;shutil.copy2(p,dest);rows.append({'file':p.name,'sha256':sha(dest)})
    overview=DST.parent/'world-overview.png';shutil.copy2(SRC/'world-overview-thumb.png',overview)
    report={'generation_input':generated['generation_input'],'source_build_manifest_sha256':sha(SRC/'world-build-manifest.json'),'chunks':rows,'overview_sha256':sha(overview)}
    shutil.copy2(SRC/'world-build-manifest.json',DST.parent/'world-build-manifest.json')
    LOG.mkdir(parents=True,exist_ok=True)
    prior=LOG/'world-install-manifest.json'
    if prior.exists():shutil.copy2(prior,LOG/('world-install-manifest-before-'+datetime.datetime.now().strftime('%H%M%S')+'.json'))
    (LOG/'world-install-manifest.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print('Installed 64 runtime chunks + overview; existing .meta preserved.')
if __name__=='__main__':main()
