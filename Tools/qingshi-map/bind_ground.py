"""Explicit stone-road binding. Preparing is read-only; installing requires the reviewed source hash."""
import argparse,datetime,hashlib,json,shutil
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
LOG=ROOT/'Tools/logs/qingshi-map-20261105'
DEST=ROOT/'GameClient/TwelveJade/Assets/ArtSource/world/materials/stone-road.png'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--source',required=True);ap.add_argument('--expected-sha256',required=True)
    ap.add_argument('--install',action='store_true');ap.add_argument('--review-record');args=ap.parse_args()
    source=(ROOT/args.source).resolve();actual=sha(source)
    if actual!=args.expected_sha256.lower():raise SystemExit('Source hash changed; review and select the actual candidate again')
    with Image.open(source) as im:
        if im.width!=im.height or im.width<256:raise SystemExit('Square tile of at least 256 pixels required')
        size=list(im.size)
    target=LOG/('ground-binding-installed.json' if args.install else 'ground-binding-prepared.json')
    if target.exists():raise SystemExit('Binding evidence already exists; preserve it before making a new selection')
    review=None
    if args.install:
        if not args.review_record:raise SystemExit('Candidate pending visual review; pass its approved review JSON to install')
        review=json.loads((ROOT/args.review_record).read_text('utf-8'))
        if review.get('status')!='approved' or review.get('sourceSha256')!=actual or not review.get('reviewer'):
            raise SystemExit('Approval must identify the reviewer and this exact source SHA256')
    report={'status':'installed' if args.install else 'prepared-awaiting-review','timestamp':datetime.datetime.now().astimezone().isoformat(),
            'source':source.relative_to(ROOT).as_posix(),'sourceSha256':actual,'size':size,'transform':'byte-for-byte copy; no resize or color changes',
            'destination':DEST.relative_to(ROOT).as_posix(),'previousDestinationSha256':sha(DEST),'review':review}
    if args.install:
        backup=LOG/('stone-road-before-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.png');shutil.copy2(DEST,backup)
        shutil.copy2(source,DEST);report['installedSha256']=sha(DEST)
        if report['installedSha256']!=actual:raise SystemExit('Copy verification failed')
    target.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(report['status']+': '+actual)
    if args.install:print('Recompose all 64 chunks and rerun seam/provenance gates before installing the new world.')
if __name__=='__main__':main()
