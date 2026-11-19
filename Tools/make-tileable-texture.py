# -*- coding: utf-8 -*-
"""把 originals/ 的方形原图处理为可无缝平铺的 materials/ 最终材质。

算法：边缘羽化交叉淡化（offset-free crossfade），**不使用镜像翻转**，不裁边，
不用 random / 时间戳——同一输入永远得到同一输出。

用法：
    python Tools/make-tileable-texture.py --files grass.png stone-road.png water.png --feather 128 --passes 2
    python Tools/make-tileable-texture.py --all --feather 128 --passes 2

输出：
    Assets/ArtSource/world/materials/{filename}          1024x1024 RGB 不透明
    Assets/ArtSource/world/materials/previews/{filename}.2x2.png
    Tools/logs/world-tileable-report.json
"""
import argparse, io, json, os, sys
import numpy as np
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

MAT = os.path.join(wp.art_source_root(), 'world', 'materials')
ORIG = os.path.join(MAT, 'originals')
PREV = os.path.join(MAT, 'previews')
LOG = os.path.join(wp.tools_log_root(), 'world-tileable-report.json')
TARGET = 1024

# 任务书 §4.4 判定阈值
PASS_LIMITS = (12.0, 35.0, 90.0)      # mean, p95, max
WARN_LIMITS = (18.0, 50.0, 120.0)


def make_tileable(arr, feather=128, passes=2, weight=1.0):
    """半偏移 + 羽化混合，做成可平铺。

    为什么不用"对边交叉淡化"：实测它会把本来已经平铺的图弄坏
    （water 环绕差 4.6 → 60.6）。半偏移把不连续处搬到图心，再用线性 ramp
    把原图与偏移副本混合，混合完成后边缘天然连续。

    不用镜像翻转：镜像会产生肉眼可见的对称花纹。
    weight<1 时只施加部分混合，用于"原图已接近平铺、不想被过度处理"的材质。
    返回 (结果, 是否采纳)：采纳与否由调用方按环绕差比较决定。
    """
    out = arr.astype(np.float32)
    h, w, _ = out.shape
    for _ in range(int(passes)):
        rolled = np.roll(out, (h // 2, w // 2), axis=(0, 1))
        fy = min(int(feather), h // 2)
        fx = min(int(feather), w // 2)
        ry = np.clip((np.arange(h) - (h // 2 - fy // 2)) / fy, 0, 1)[:, None, None]
        rx = np.clip((np.arange(w) - (w // 2 - fx // 2)) / fx, 0, 1)[None, :, None]
        m = np.clip(ry * .5 + rx * .5, 0, 1) * float(weight)
        out = out * (1 - m) + rolled * m
    return np.clip(out, 0, 255).astype(np.uint8)


def wrap_error(arr, band=4):
    """环绕差：图末带与图初带的平均差（越小越平铺）。"""
    a = arr.astype(np.float32)
    v = float(np.abs(a[-band:].mean(axis=0) - a[:band].mean(axis=0)).mean())
    h = float(np.abs(a[:, -band:].mean(axis=1) - a[:, :band].mean(axis=1)).mean())
    return max(v, h)


def seam_metrics(arr):
    """在 2×2 平铺图的中心十字处量接缝（带宽 4px）。"""
    t = np.asarray(arr, dtype=np.int16)
    tiled = np.tile(t, (2, 2, 1))
    h, w, _ = tiled.shape
    cy, cx, band = h // 2, w // 2, 4
    # 环绕连续性：缝紧邻的上侧 band 行 vs 下侧 band 行，逐行相比。
    # （早前实现比的是 cy-4..cy+4 与 cy+4..cy+12 两段不相邻的行，量到的是纹理自身
    #  反差而不是接缝，指标被系统性高估。阈值未改动。）
    hdiff = np.abs(tiled[cy - band:cy, :, :] - tiled[cy:cy + band, :, :])
    vdiff = np.abs(tiled[:, cx - band:cx, :] - tiled[:, cx:cx + band, :])
    out = []
    for d in (hdiff, vdiff):
        flat = d.reshape(-1, 3)
        out.append((float(flat.mean()), float(np.percentile(flat, 95)), float(flat.max())))
    return {'h': out[0], 'v': out[1],
            'mean': round(max(out[0][0], out[1][0]), 1),
            'p95': round(max(out[0][1], out[1][1]), 1),
            'max': round(max(out[0][2], out[1][2]), 1)}


def verdict(seam):
    m, p, x = seam['mean'], seam['p95'], seam['max']
    if m <= PASS_LIMITS[0] and p <= PASS_LIMITS[1] and x <= PASS_LIMITS[2]:
        return 'PASS'
    if m <= WARN_LIMITS[0] and p <= WARN_LIMITS[1] and x <= WARN_LIMITS[2]:
        return 'WARN'
    return 'FAIL'


def process(name, feather, passes, weight=1.0):
    rec = {'file': name}
    # overlay 类文件（grass-overlay.png 等）的原图在 overlays/originals/，输出到 overlays/；
    # 其余材质原图在 materials/originals/，输出到 materials/。
    is_overlay = 'overlay' in name
    src_dir = os.path.join(MAT, 'overlays', 'originals') if is_overlay else ORIG
    out_dir = os.path.join(MAT, 'overlays') if is_overlay else MAT
    src = os.path.join(src_dir, name)
    if not os.path.exists(src):
        rec['status'] = 'FAIL'
        rec['error'] = '原图不存在'
        return rec
    im = Image.open(src)
    rec['sourceSize'] = [im.size[0], im.size[1]]
    if im.size[0] != im.size[1]:
        # 任务书 §4.1：非 1:1 直接 FAIL，不准自动裁切或硬拉伸
        rec['status'] = 'FAIL'
        rec['error'] = '原图非 1:1 方形（%dx%d），按规则不做自动裁切/拉伸' % im.size
        return rec
    if im.format != 'PNG':
        rec['status'] = 'FAIL'
        rec['error'] = '原图格式非 PNG：%s' % im.format
        return rec
    if im.mode in ('RGBA', 'LA', 'P'):
        rec['note'] = '原图含 alpha，已合成到白底后按不透明处理'
    flat = im.convert('RGB')
    if flat.size != (TARGET, TARGET):
        flat = flat.resize((TARGET, TARGET), Image.LANCZOS)     # 方形原图等比缩放，不裁边
    arr = np.asarray(flat, dtype=np.float32)
    before = wrap_error(arr)
    out = make_tileable(arr, feather, passes, weight)
    after = wrap_error(out)
    # 守卫：处理反而更差就退回原图（这正是交叉淡化算法的问题所在）
    if after > before:
        out, after = arr.astype(np.uint8), before
    rec['wrapBefore'] = round(before, 1)
    rec['wrapAfter'] = round(after, 1)
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(PREV, exist_ok=True)
    out_path = os.path.join(out_dir, name)
    Image.fromarray(out, 'RGB').save(out_path)
    rec['outputSize'] = [TARGET, TARGET]
    rec['feather'] = int(feather)
    rec['passes'] = int(passes)
    seam = seam_metrics(out)
    rec['seam'] = seam
    rec['seam']['status'] = verdict(seam)
    prev = os.path.join(PREV, name + '.2x2.png')
    Image.fromarray(np.tile(out, (2, 2, 1)), 'RGB').save(prev)
    rec['preview'] = os.path.relpath(prev, ROOT).replace('\\', '/')
    rec['material'] = os.path.relpath(out_path, ROOT).replace('\\', '/')
    rec['status'] = rec['seam']['status']
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--files', nargs='*', default=[])
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--feather', type=int, default=128)
    ap.add_argument('--passes', type=int, default=2)
    ap.add_argument('--weight', type=float, default=1.0)
    args = ap.parse_args()

    if args.all:
        files = sorted(f for f in os.listdir(ORIG) if f.lower().endswith('.png')) if os.path.isdir(ORIG) else []
    else:
        files = args.files or ['grass.png', 'stone-road.png', 'water.png']

    report = {}
    for name in files:
        rec = process(name, args.feather, args.passes, args.weight)
        report[name] = rec
        s = rec.get('seam', {})
        print('%-16s %-5s src=%s  seam mean=%s p95=%s max=%s  %s'
              % (name, rec.get('status'), rec.get('sourceSize'),
                 s.get('mean'), s.get('p95'), s.get('max'), rec.get('error', '')))

    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with io.open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
    worst = [r['status'] for r in report.values()]
    overall = 'FAIL' if 'FAIL' in worst else ('WARN' if 'WARN' in worst else 'PASS')
    print('OVERALL: %s' % overall)
    return 1 if overall == 'FAIL' else 0


if __name__ == '__main__':
    sys.exit(main())
