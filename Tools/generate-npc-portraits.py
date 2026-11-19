# -*- coding: utf-8 -*-
"""员工 J · M7：青石镇五大灵魂人物立绘剪影（ADR-025 生图管线对齐）。

流程：提示词归档 → Grok 网关生图 → 原图归档 → 抠白转透明通道 → 落 Resources/Art/Portraits/。
输出 512×768 RGBA 水墨剪影：美术后续换全彩高清立绘时，同名覆盖即可，程序层不动。
生图失败不中断：回退成程序化几何剪影，保证槽位永远有图且有透明通道。
"""
import os, sys, json, urllib.request

# 健壮性（夜1 G-3，2026-10-06）：zh-CN Windows 的 GBK 控制台下 print 中文/箭头会
# UnicodeEncodeError 崩在出结论之前——**会崩的脚本比会红的脚本危险得多**。
# 故统一强制 stdout/stderr 为 UTF-8；先例见 SaveChecks.csproj 的 <CodePage>65001</CodePage>。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import world_paths as wp
from PIL import Image

W, H = 512, 768
INK = (26, 36, 32, 255)
KEY_FILE = '/tmp/ddyu-art-key.txt'
BASE = 'http://127.0.0.1:8000'
MODEL = 'grok-imagine-image'

# (npcId, 文件短名, 人物描述)
NPCS = [
    ("npc-shenhuai-an", "shenhuai-an",
     "a 72-year-old Chinese fortune teller, very thin, long beard, Taoist crown, long-sleeved plain robe, slightly hunched"),
    ("npc-shitou", "shitou",
     "a teenage peasant boy, coarse cloth jacket with rolled-up sleeves, sturdy, short hair, poor"),
    ("huolang", "huolang",
     "a round-faced stout travelling peddler with a shoulder pole and two baskets, cheerful"),
    ("npc-zhao-da", "zhao-da",
     "a burly bare-armed blacksmith with beard stubble and an apron, holding a hammer, broad shoulders"),
    ("npc-yao-lao", "yao-lao",
     "an elderly herbalist in a long robe with a long beard, carrying a bamboo basket of herbs, stooped"),
]

TEMPLATE = """Use case: in-game dialogue character portrait (left slot, 512x768)
Asset type: 2D ink-wash silhouette with transparent background, game UI art

{who}
Chinese ink-wash (shuimo) bust silhouette. Solid flat black ink fill, no shading,
no facial features, no internal detail, no colour.
Pure white background, no frame, no text, no watermark, no props floating.
Bust framing (head and shoulders), centered, filling the frame vertically.
Clean continuous outer contour with a slightly dry brush edge.
"""


def call_grok(prompt):
    if not (os.path.exists(KEY_FILE) and os.path.getsize(KEY_FILE) > 10):
        return None
    key = open(KEY_FILE).read().strip()
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    body = json.dumps({'model': MODEL, 'prompt': prompt, 'n': 1}).encode()
    req = urllib.request.Request(BASE + '/v1/images/generations', data=body,
                                 headers={'Authorization': 'Bearer ' + key,
                                          'Content-Type': 'application/json'})
    r = json.loads(op.open(req, timeout=300).read())
    return op.open(r['data'][0]['url'], timeout=120).read()


def to_silhouette(src, dst):
    """白底抠成透明通道：墨越黑越不透明，纯白全透。边缘按亮度渐变，不产生硬白边。"""
    im = Image.open(src).convert('RGB').resize((W, H), Image.LANCZOS)
    gray = im.convert('L')
    alpha = gray.point(lambda v: 0 if v >= 238 else min(255, int((238 - v) * 1.7)))
    blank = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    ink = Image.new('RGBA', (W, H), INK)
    Image.composite(ink, blank, alpha).save(dst)


def fallback_silhouette(dst, seed):
    """生图不可用时的程序化占位：一个居中的墨色人形轮廓，透明底、512×768。"""
    from PIL import ImageDraw
    blank = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(blank)
    cx = W // 2
    head = 96 - seed * 6
    d.ellipse([cx - head // 2, 120, cx + head // 2, 120 + head], fill=INK)
    d.polygon([(cx - 150, H), (cx - 108, 250), (cx + 108, 250), (cx + 150, H)], fill=INK)
    blank.save(dst)


def main():
    prompts = os.path.join(wp.art_source_root(), 'prompts')
    originals = os.path.join(wp.art_source_root(), 'originals', 'portraits')
    portraits = os.path.join(wp.unity_project_root(), 'Assets', 'Resources', 'Art', 'Portraits')
    for path in (prompts, originals, portraits):
        wp.ensure_dir(path)

    report = []
    for index, (npc_id, short, who) in enumerate(NPCS):
        prompt = TEMPLATE.format(who=who)
        prompt_path = os.path.join(prompts, 'portrait-%s-v1.txt' % short)
        wp.assert_canonical_output_path(prompt_path)
        open(prompt_path, 'w', encoding='utf-8').write(prompt)

        raw_path = os.path.join(originals, 'portrait-%s-v1.raw' % short)
        out_path = os.path.join(portraits, npc_id + '.png')
        wp.assert_canonical_output_path(out_path)
        try:
            data = call_grok(prompt)
            if not data:
                raise RuntimeError('no key / no image')
            open(raw_path, 'wb').write(data)
            to_silhouette(raw_path, out_path)
            report.append('%s  OK  grok' % npc_id)
        except Exception as exc:
            fallback_silhouette(out_path, index)
            report.append('%s  FALLBACK  %s: %s' % (npc_id, type(exc).__name__, exc))

    print('--- 立绘装配报告 ---')
    for line in report:
        print(line)


if __name__ == '__main__':
    main()
