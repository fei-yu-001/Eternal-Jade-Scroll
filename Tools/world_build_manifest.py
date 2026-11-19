"""Build provenance is captured by the composer, never retroactively certified by installation."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CFG=ROOT/'GameClient/TwelveJade/Assets/Resources/Config'
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def snapshot():
    m=json.loads((CFG/'town-map.json').read_text('utf-8'))
    inputs={'terrainShape':m['terrainShape'],'elevation':m.get('elevation'),
            'compose':json.loads((CFG/'world-compose.json').read_text('utf-8'))}
    files=[CFG/'terrain-types.json',ROOT/'Tools/make-world-mask.py',ROOT/'Tools/compose-world-chunks.py',ROOT/'Tools/world_elevation_art.py',ROOT/'Tools/world_geometry.py',ROOT/'Tools/world_paths.py',ROOT/'Tools/world_river_surface.py',Path(__file__),ROOT/'GameClient/TwelveJade/Assets/ArtSource/world/masks/world-mask.png']
    files.append(CFG/'world-material-gen.json')
    material_config=json.loads((CFG/'world-material-gen.json').read_text('utf-8'))
    for material in material_config['materials'].values():
        source=material.get('sourceAsset')
        if source:
            path=ROOT/source['path']
            if sha(path)!=source['sha256']:raise ValueError('Approved material source hash changed: '+source['path'])
            files.append(path)
    files+=sorted((ROOT/'GameClient/TwelveJade/Assets/ArtSource/world/materials').glob('*.png'))
    inputs['files']={p.relative_to(ROOT).as_posix():sha(p) for p in files}
    encoded=json.dumps(inputs,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode('utf-8')
    return {'sha256':hashlib.sha256(encoded).hexdigest(),'inputs':inputs}
def validate(source,current=None):
    source=Path(source);manifest=json.loads((source/'world-build-manifest.json').read_text('utf-8'))
    if manifest.get('status')!='complete' or len(manifest.get('chunks',[]))!=64:raise ValueError('No completed 64-chunk generation manifest')
    if manifest['generation_input']!=(current or snapshot()):raise ValueError('Inputs changed since generation; compose again before install')
    expected={f'chunk_{x}_{y}.png' for y in range(8) for x in range(8)}
    if {r['file'] for r in manifest['chunks']}!=expected:raise ValueError('Incomplete or duplicate chunk manifest')
    for row in manifest['chunks']+[manifest['overview']]:
        if sha(source/row['file'])!=row['sha256']:raise ValueError('Generated output changed: '+row['file'])
    return manifest
