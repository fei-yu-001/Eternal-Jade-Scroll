# -*- coding: utf-8 -*-
"""Gate B-fix2 第六步：AI 微纹理 overlay 生成（任务书 §6.2 / §6.3）。

只生成"低结构 overlay"，不生成完整材质。原图存 overlays/originals/，
经 make-tileable-texture.py 处理后存 overlays/。
overlay 自身必须 PASS 或 WARN 才可用；FAIL → 由 make-procedural-overlay.py 兜底。

用法：
    python Tools/make-ai-overlay.py --names grass stone
"""
import argparse, io, json, os, sys, urllib.request
from PIL import Image

# 健壮性（夜1 G-3，2026-10-06）：zh-CN Windows 的 GBK 控制台下 print 中文/箭头会
# UnicodeEncodeError 崩在出结论之前——**会崩的脚本比会红的脚本危险得多**。
# 故统一强制 stdout/stderr 为 UTF-8；先例见 SaveChecks.csproj 的 <CodePage>65001</CodePage>。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp

KEY_FILE = '/tmp/ddyu-art-key.txt'
GATEWAY = 'http://127.0.0.1:8000/v1/images/generations'
CALL_PARAMS = {'model': 'grok-imagine-image', 'n': 1, 'aspect_ratio': '1:1', 'size': '1024x1024'}
PROMPT_DIR = os.path.join(wp.art_source_root(), 'prompts', 'world-materials')

HEAD = """Generate one square 1024x1024 seamless tileable low-contrast micro texture overlay for a 2D Chinese ink-wash xianxia open-world RPG terrain material.
Asset type: micro texture overlay, not a terrain material, not a scene.
View: strict orthographic top-down, no perspective, no isometric, no 3D camera.
The texture must tile seamlessly in X and Y, with no visible grid, no border, no frame, no vignette, no repeated obvious pattern.
It must be opaque, clean, low saturation, and contain only flat micro surface detail.
The image is a neutral overlay layer: mid-gray average, very low contrast, no large shapes, no strong structures.
"""

TAIL = """Important: no objects, no props, no landmarks, no horizon, no sky, no cast shadows. Only flat micro surface detail viewed from above.
If any detail approaches an edge, it must continue naturally on the opposite edge.
"""

SUBJECTS = {
    'grass': {
        'file': 'grass-overlay.png',
        'subject': """Use case: subtle rice-paper fiber, faint ink speckles, tiny dust and moss stains to overlay onto a procedural grass/dirt ground base.

Subject:
rice-paper fiber, faint ink speckles, tiny dust particles, very subtle moss stain, dry brush grain, flat micro surface detail.

Color palette:
neutral warm gray, pale rice-paper beige, faint ink black, very subtle moss green tint, low saturation.

Texture details:
fiber and speckle detail smaller than 8 pixels, extremely low contrast, no shapes larger than 16 pixels, no directional streaks, even coverage across the whole image.""",
        'avoid': ("grass, grass blades, grass tufts, plants, leaves, flowers, bushes, shrubs, tree canopy, forest, rocks, boulders, "
                  "stones, cracks, roads, buildings, houses, walls, roofs, doors, windows, people, animals, text, letters, numbers, "
                  "watermark, logo, UI, icons, markers, arrows, black borders, white borders, frame, vignette, perspective, isometric, "
                  "3D render, photorealistic, blurry, lowres, jpeg artifacts, visible seams, repeated pattern grid, high contrast, "
                  "large shapes, strong structures."),
    },
    'stone': {
        'file': 'stone-overlay.png',
        'subject': """Use case: subtle worn stone surface grain, dust, faint scratches, and ink wash to overlay onto a procedural flagstone road base.

Subject:
worn stone micro surface, fine grain, faint scratches, dust, subtle ink wash, tiny mineral speckles.

Color palette:
neutral gray, warm gray, pale blue-gray, faint ink black, low saturation.

Texture details:
grain and speckle detail smaller than 8 pixels, extremely low contrast, no shapes larger than 16 pixels, no directional streaks, even coverage across the whole image.""",
        'avoid': ("flagstone layout, stone slabs, mortar lines, joints, cracks, pebbles, rocks, boulders, roads, streets, buildings, houses, "
                  "walls, trees, plants, grass, flowers, people, animals, text, letters, numbers, watermark, logo, UI, icons, markers, arrows, "
                  "black borders, white borders, frame, vignette, perspective, isometric, 3D render, photorealistic, blurry, lowres, "
                  "jpeg artifacts, visible seams, repeated pattern grid, high contrast, large shapes, strong structures."),
    },
}


def key():
    if not os.path.exists(KEY_FILE):
        sys.exit('缺少 Grok key：' + KEY_FILE)
    return open(KEY_FILE).read().strip()


def build_prompt(name):
    s = SUBJECTS[name]
    return HEAD + '\n' + s['subject'] + '\n\n' + TAIL + '\n\nAvoid:\n' + s['avoid'] + '\n'


def generate(prompt):
    payload = dict(CALL_PARAMS)
    payload['prompt'] = prompt
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(GATEWAY, data=json.dumps(payload).encode(),
                                 headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    resp = json.loads(op.open(req, timeout=300).read())
    return op.open(resp['data'][0]['url'], timeout=120).read()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--names', nargs='*', default=['grass', 'stone'])
    args = ap.parse_args()

    cfg_path = os.path.join(wp.config_root(), 'world-material-gen.json')
    cfg = json.load(io.open(cfg_path, encoding='utf-8'))
    overlay_dir = wp.resolve_repo_path(cfg['outputs']['overlayDir'], allow_missing=True)
    wp.ensure_dir(overlay_dir)
    orig_dir = os.path.join(overlay_dir, 'originals')
    os.makedirs(orig_dir, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)

    for name in args.names:
        if name not in SUBJECTS:
            print('%-8s SKIP（未知 overlay）' % name)
            continue
        prompt = build_prompt(name)
        with io.open(os.path.join(PROMPT_DIR, name + '-overlay.prompt.txt'), 'w',
                     encoding='utf-8', newline='\n') as fh:
            fh.write(prompt)
        print('[%s] generating overlay...' % name, flush=True)
        raw = generate(prompt)
        im = Image.open(io.BytesIO(raw)).convert('RGB')
        print('[%s] native %s' % (name, im.size), flush=True)
        im.save(os.path.join(orig_dir, SUBJECTS[name]['file']), format='PNG')
    return 0


if __name__ == '__main__':
    sys.exit(main())
