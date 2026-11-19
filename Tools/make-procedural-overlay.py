# -*- coding: utf-8 -*-
"""Gate B-fix2 第七步（§7）：程序化 overlay 兜底。

当 Grok overlay 不合格（FAIL）时使用。纯程序噪声，确定性、可平铺、灰度中性：
    Assets/ArtSource/world/materials/overlays/{name}-overlay.fallback.png

构成（§7）：中灰基底 128–180 + 高频 hash 斑点 + 极弱低频云斑，无大结构。

用法：
    python Tools/make-procedural-overlay.py --names grass stone
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

CFG = os.path.join(wp.config_root(), 'world-material-gen.json')

sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import importlib.util as _ilu


def _load(mod_file, mod_name):
    spec = _ilu.spec_from_file_location(mod_name, os.path.join(ROOT, 'Tools', mod_file))
    m = _ilu.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


base_mod = _load('make-procedural-base.py', 'mpb')


def gen_overlay(size, kind):
    """灰度中性 overlay：中灰 + 高频斑点 + 极弱低频云。"""
    xx, yy = base_mod.pixel_grid(size)
    # 低频云：周期 8（scale=128），振幅极小
    clouds = base_mod.value_noise_fast(xx, yy, 128.0, 8)
    # 高频斑点：scale=2px，逐像素
    speck = base_mod.value_noise_fast(xx, yy, 2.0, max(2, size // 2))
    # 中频纤维：scale=6px
    fiber = base_mod.value_noise_fast(xx, yy, 6.0, max(2, int(round(size / 6.0))))

    if kind == 'stone':
        base_level, cloud_amp, fiber_amp, speck_amp = 150.0, 10.0, 8.0, 7.0
    else:
        base_level, cloud_amp, fiber_amp, speck_amp = 160.0, 8.0, 7.0, 6.0

    g = base_level + (clouds - 0.5) * 2.0 * cloud_amp + (fiber - 0.5) * 2.0 * fiber_amp + (speck - 0.5) * 2.0 * speck_amp
    g = np.clip(g, 0, 255)
    return np.dstack([g, g, g]).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--names', nargs='*', default=['grass', 'stone'])
    args = ap.parse_args()

    cfg = json.load(io.open(CFG, encoding='utf-8'))
    size = int(cfg['size'])
    out_dir = wp.resolve_repo_path(cfg['outputs']['overlayDir'], allow_missing=True)
    wp.ensure_dir(out_dir)

    for name in args.names:
        arr = gen_overlay(size, name)
        out = os.path.join(out_dir, name + '-overlay.fallback.png')
        Image.fromarray(arr, 'RGB').save(out)
        seam = base_mod.seam_metrics(arr)
        print('%-8s fallback -> %s  seam mean=%s p95=%s max=%s %s' % (
            name, os.path.relpath(out, ROOT).replace('\\', '/'),
            seam['mean'], seam['p95'], seam['max'], base_mod.verdict(seam)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
