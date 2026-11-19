# -*- coding: utf-8 -*-
"""Gate B：生成第一批 3 张世界地形材质（grass / stone-road / water）。

提示词严格按任务书 §11（公共头）、§12（负面词）、§13（结尾约束）、§14（三张主体）
组装。生成走本地 grok2api 网关，原图存 originals/，正式图 1024×1024 存 materials/，
提示词原文归档到 Assets/ArtSource/prompts/world-materials/。

不硬编码尺寸：1024 来自 world-compose.json / 任务书规格，缩放策略见任务书 §10.3。
"""
import io, json, os, sys, time, urllib.request
from PIL import Image

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp

ART = os.path.join(wp.art_source_root(), 'world', 'materials')
ORIG = os.path.join(ART, 'originals')
PREV = os.path.join(ART, 'previews')
PROMPTS = os.path.join(wp.art_source_root(), 'prompts', 'world-materials')
KEY_FILE = '/tmp/ddyu-art-key.txt'
GATEWAY = 'http://127.0.0.1:8000/v1/images/generations'
TARGET = 1024

# ---- 任务书 §11 公共提示词头（不得省略/改写）----
HEAD = """Generate one square 1024x1024 seamless tileable top-down terrain material texture for a 2D Chinese ink-wash xianxia open-world RPG.
Asset type: terrain material tile.
Use case: stylized-concept ground texture, not a scene.
Visual target: hand-painted rice-paper terrain, subtle gothic survival-game linework, HD-2D miniature diorama lighting, muted natural palette, elegant brush strokes, soft top-left light, high gameplay readability.
View: strict orthographic top-down, no perspective, no isometric, no 3D camera.
The texture must tile seamlessly in X and Y, with no visible grid, no border, no frame, no vignette, no repeated obvious pattern.
It must be opaque, clean, low saturation, and contain only ground surface material.
"""

# ---- 任务书 §12 公共负面提示词 ----
NEGATIVE = ("buildings, houses, walls, roofs, doors, windows, NPCs, people, characters, animals, monsters, trees, "
            "saplings, bushes, flowers, large grass blades, rocks as objects, boulders, decorative stones, stalls, lanterns, "
            "signs, banners, fences, railings, bridges as whole structures, boats, fish, clouds, snow, desert, modern "
            "asphalt, regular bricks, text, letters, numbers, watermark, logo, UI, icons, markers, arrows, black borders, "
            "white borders, frame, vignette, perspective, isometric, 3D render, photorealistic, blurry, lowres, jpeg "
            "artifacts, visible seams, repeated pattern grid, cartoon sticker style, oversaturated, high contrast, harsh "
            "shadow, cast shadow.")

# ---- 任务书 §13 公共结尾约束 ----
TAIL = ("Important: no objects, no props, no landmarks, no horizon, no sky, no shadows cast by external objects. "
        "Only flat ground material viewed from above. If any detail approaches an edge, it must continue naturally on "
        "the opposite edge. The final image must be usable as a tileable terrain material in a top-down open-world map.")

# ---- 任务书 §14 三张主体 ----
SUBJECTS = {
    'grass': """Subject:
quiet grassland ground tile for a Chinese ink-wash open-world RPG, subtle earth patches, faint brush strokes, calm natural terrain, readable as walkable grassland.

Color palette:
muted celadon green, dry ochre, soft gray-brown, rice-paper off-white, low saturation.

Texture details:
very small grass tufts, soft soil mottling, delicate ink lines, gentle paper grain, no prominent objects, no large stones, no flowers, no bushes, edge details must be sparse and tile-friendly.""",
    'stone-road': """Subject:
old blue-gray flagstone street ground tile for a Chinese town main street, worn stone road, readable as walkable stone pavement.

Color palette:
slate gray, warm gray, faded blue-gray, subtle moss green hint, rice-paper off-white highlights.

Texture details:
irregular rectangular flagstones, worn joints, subtle ink shading, gentle surface scratches, old town street feeling, not modern cobblestone, not brick road, no text, no symbols, no drain covers.""",
    'water': """Subject:
shallow river water surface ground tile for a Chinese ink-wash RPG, calm flowing river, readable as unwalkable water.

Color palette:
blue-gray, teal, ink green, soft cyan-gray, muted river tone.

Texture details:
gentle flow lines, soft ripples, subtle ink-wash gradients, translucent feeling but opaque image, no banks, no boats, no fish, no splashes, no waterfall, no ocean waves, no ice, no swamp bubbles.""",
}

SUBJECTS['grass'] = """Subject:
flat ground surface of short turf, worn pasture soil, mossy dirt and tiny grass speckles, seen from directly above. This is a ground material tile, not a grass field scene.

Color palette:
muted celadon green, dry ochre, soft gray-brown, moss green, rice-paper off-white, low saturation.

Texture details:
very short turf integrated into soil, tiny grass speckles, soft dirt patches, delicate ink lines, gentle paper grain, low contrast, no prominent objects, no large stones, no flowers, no bushes, no tree canopy, no leaves, no plant silhouettes, edge details must be sparse and tile-friendly.

Critical content constraint:
This must NOT be an overhead forest canopy.
This must NOT contain trees, shrubs, bushes, flower clusters, leaf shapes, plant silhouettes, or grass tufts as separate objects.
It must look like a walkable ground surface made of short turf, dirt, moss and tiny grass speckles."""

SUBJECTS['stone-road'] = """Subject:
old blue-gray flagstone street ground tile for a Chinese town main street, worn stone pavement, seen from directly above. This is a flat ground material tile, not a street scene.

Color palette:
slate gray, warm gray, faded blue-gray, subtle moss green hint, rice-paper off-white highlights, low saturation.

Texture details:
small irregular flagstones, worn mortar joints, subtle ink shading, gentle surface scratches, old town street feeling. Stone pieces should be medium to small scale, low contrast, and edge-safe. Avoid one large complete slab crossing the image border. Avoid strong directional layout. No modern cobblestone, no brick road, no drain covers, no text, no symbols.

Critical tileability constraint:
The four borders must contain only repetitive small stone fragments and mortar texture.
Do not place large distinct stones that are cut by the image edge.
Keep edge areas low-contrast and pattern-safe."""

SUBJECTS['water'] = """Subject:
shallow river water surface ground tile for a Chinese ink-wash RPG, calm flowing river, seen from directly above. This is a flat water material tile, not a river scene.

Color palette:
blue-gray, teal, ink green, soft cyan-gray, muted river tone, low saturation.

Texture details:
gentle flow lines, soft ripples, subtle ink-wash gradients, translucent feeling but opaque image. Keep ripple scale medium-small and edge-safe. No banks, no boats, no fish, no splashes, no waterfall, no ocean waves, no ice, no swamp bubbles, no rocks, no reeds."""

# Gate B-fix 只做这三张
BATCH = ['grass', 'stone-road', 'water']

# 任务书 §16：不合格重试时按问题追加约束。键为材质名，值为追加到正向 prompt 末尾的文本。
RETRY_HINTS = {
    'grass': 'reduce detail scale. all green marks must be tiny surface speckles, not individual plants. '
             'no circular canopy shapes. no leaf outlines. no bright white specks. low overall contrast. '
             'edge continuity is critical, patterns must wrap perfectly, avoid high-contrast features near borders, '
             'keep edge details sparse and low frequency, no distinct large clumps that would repeat visibly.',
    'stone-road': 'smaller flagstone scale, lower contrast, no stone larger than 120 pixels, '
                  'no stone touching more than one border, edge-safe pavement texture, '
                  'edge continuity is critical, the flagstone pattern must continue across the tile border, '
                  'no stone may be cut in half at the edge, keep the border rows statistically identical to the interior, '
                  'avoid a distinct border course or kerb line along any edge.',
    'water': 'edge continuity is critical, ripples must continue across the tile border, keep border rows identical '
             'to the interior, no visible repeating wave crest.',
}


def build_prompt(name, attempt=1):
    # 任务书 §16：重试时按问题追加约束（平铺不合格 → 强调边缘连续性）。
    hint = RETRY_HINTS.get(name, '') if attempt > 1 else ''
    body = HEAD + '\n' + SUBJECTS[name]
    if hint:
        body += '\n' + hint
    return body + '\n' + TAIL + '\n\nAvoid:\n' + NEGATIVE + '\n'


def key():
    if not os.path.exists(KEY_FILE):
        sys.exit('缺少 Grok key：' + KEY_FILE)
    return open(KEY_FILE).read().strip()


CALL_PARAMS = {'model': 'grok-imagine-image', 'n': 1,
               'aspect_ratio': '1:1', 'size': '1024x1024'}


def generate(prompt, model='grok-imagine-image'):
    # aspect_ratio=1:1 是必需的：实测不传时网关返回 768x1152 竖图，
    # 传了才给 960x960 方图（竖图硬拉成方图会毁掉平铺与比例）。
    payload = dict(CALL_PARAMS)
    payload['model'] = model
    payload['prompt'] = prompt
    body = json.dumps(payload).encode()
    req = urllib.request.Request(GATEWAY, data=body,
                                 headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    resp = json.loads(opener.open(req, timeout=300).read())
    return opener.open(resp['data'][0]['url'], timeout=120).read()


def main():
    for d in (ART, ORIG, PREV, PROMPTS):
        os.makedirs(d, exist_ok=True)
    argv = [a for a in sys.argv[1:] if not a.startswith('--')]
    attempt = int(argv[0]) if argv else 1
    only = [a[:-4] if a.endswith('.png') else a for a in argv[1:]]
    batch = only or BATCH
    for name in batch:
        prompt = build_prompt(name, attempt)
        io.open(os.path.join(PROMPTS, name + '.png.txt'), 'w', encoding='utf-8', newline='\n').write(prompt)
        print('[%s] generating...' % name, flush=True)
        t0 = time.time()
        raw = generate(prompt)
        orig_path = os.path.join(ORIG, name + '.png')
        # 网关实际返回 JPEG 字节（响应里的 mime_type 可证），直接存成 .png 会得到
        # '扩展名 PNG / 内容 JPEG' 的假文件，make-tileable-texture 的格式校验会判 FAIL。
        # 这里只做容器转码（不裁切、不缩放），保证 originals 是真正的 PNG。
        src_im = Image.open(io.BytesIO(raw)).convert('RGB')
        src_im.save(orig_path, format='PNG')
        im = Image.open(orig_path).convert('RGB')
        native = im.size
        note = 'native' if native == (TARGET, TARGET) else 'resized from %dx%d' % native
        if im.size != (TARGET, TARGET):
            im = im.resize((TARGET, TARGET), Image.LANCZOS)     # 只缩放，不裁边（裁边会破坏平铺）
        final = os.path.join(ART, name + '.png')
        im.save(final)
        print('[%s] %s  %.1fs  -> %s' % (name, note, time.time() - t0, final), flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
