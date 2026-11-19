# -*- coding: utf-8 -*-
"""C0-D：grass / stone-road 视觉变体（各 4 个）+ 报告 + 2×2 接缝。

变体 = 对 world-material-gen.json 的 generators/materials 配置做**覆盖**后，
调用 make-procedural-base.py 的纯函数生成基底，再按 compose-final-material.py
同款 soft-light 公式叠加 Grok overlay（已 PASS）。生成器代码零修改。

每个变体输出（不落 Unity Resources 正式目录）：
    Assets/ArtSource/world/materials/variants/{group}/{variant}.png
    Assets/ArtSource/world/materials/variants/{group}/previews/{variant}.2x2.png
报告：
    Tools/logs/world-grass-variants-report.json
    Tools/logs/world-stone-road-variants-report.json

确定性：无 random / 无时间戳；重复运行像素一致。
"""
import copy, io, json, os, sys
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
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import world_paths as wp
# 文件名带连字符（make-procedural-base.py），不能用 import 语句，按路径显式加载：
import importlib.util as _ilu
_spec = _ilu.spec_from_file_location('mpb', os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                          'make-procedural-base.py'))
mpb = _ilu.module_from_spec(_spec)
_spec.loader.exec_module(mpb)
# 生成函数直接引用 mpb.gen_turf_dirt / mpb.gen_flagstone（纯函数，无全局状态）

SIZE = 1024
# 注意：必须用 art_source_root()（Unity 工程内）。resolve_repo_path 在 allow_missing
# 且目标不存在时会返回仓库根旧路径——T001 技术债同款事故，此处显式避开。
ART = os.path.join(wp.art_source_root(), 'world', 'materials')
OVERLAY_DIR = os.path.join(ART, 'overlays')
OUT = os.path.join(ART, 'variants')
REPORT_DIR = os.path.join(ROOT, 'Tools', 'logs')

# overlay 混合（与 compose-final-material.py 同款 soft-light-ish 公式）
def compose(base_arr, overlay_arr, opacity):
    eff = (overlay_arr.astype(np.float32) - 128.0) / 128.0
    out = base_arr.astype(np.float32) * (1.0 + eff * opacity)
    return np.clip(out, 0, 255).astype(np.uint8)

def seam(arr):
    t = np.asarray(arr, dtype=np.int16)
    tiled = np.tile(t, (2, 2, 1))
    h, w, _ = tiled.shape
    cy, cx, band = h // 2, w // 2, 4
    hd = np.abs(tiled[cy - band:cy, :, :] - tiled[cy:cy + band, :, :])
    vd = np.abs(tiled[:, cx - band:cx, :] - tiled[:, cx:cx + band, :])
    m = max(float(hd.mean()), float(vd.mean()))
    p95 = max(float(np.percentile(hd, 95)), float(np.percentile(vd, 95)))
    mx = max(float(hd.max()), float(vd.max()))
    status = 'PASS' if (m <= 12 and p95 <= 35 and mx <= 90) else \
             ('WARN' if (m <= 18 and p95 <= 50 and mx <= 120) else 'FAIL')
    return {'mean': round(m, 1), 'p95': round(p95, 1), 'max': round(mx, 1), 'status': status}

def hex2rgb(s):
    s = s.lstrip('#')
    return [int(s[i:i + 2], 16) for i in (0, 2, 4)]

GRASS = {
    'grass-current':      {'overrides': {}, 'note': '对照：当前默认参数', 'opacity': 0.16},
    'grass-richer-turf':  {'overrides': {'contrast': 0.22, 'speckleDensity': 0.048},
                           'note': '短草皮质感更明显，仍低对比', 'opacity': 0.17},
    'grass-earthier':     {'overrides': {'palette': {'celadon': '#8A9A78', 'dryOchre': '#9A7F4F',
                                                     'moss': '#6F855F', 'grayBrown': '#8A806F',
                                                     'paper': '#D8D2C0'}, 'contrast': 0.20},
                           'note': '泥土/干草/磨损感更强', 'opacity': 0.17},
    'grass-mossy':        {'overrides': {'palette': {'celadon': '#7A9578', 'moss': '#5F7A50',
                                                     'dryOchre': '#A98D5F', 'grayBrown': '#8A806F',
                                                     'paper': '#D8D2C0'}, 'contrast': 0.19},
                           'note': '轻微苔藓斑块倾向（通过调色板，非圆斑）', 'opacity': 0.17},
}
STONE = {
    'stone-current':      {'overrides': {}, 'note': '对照：当前默认参数', 'opacity': 0.14},
    'stone-worn':         {'overrides': {'colorVariance': 0.15, 'surfaceNoise': 0.12},
                           'note': '旧石板路：磨损、色差、污渍', 'opacity': 0.16},
    'stone-irregular':    {'overrides': {'jitter': 0.42, 'cellMin': 68, 'cellMax': 120},
                           'note': '石板形状更自然，仍小尺度可平铺', 'opacity': 0.15},
    'stone-aged-ink':     {'overrides': {'palette': {'slate': '#74818B', 'blueGray': '#6B7885',
                                                     'warmGray': '#8A8378', 'mortar': '#454B48',
                                                     'highlight': '#B0AB9E'}, 'colorVariance': 0.12,
                                          'surfaceNoise': 0.10},
                           'note': '国风水墨旧街：青灰统一、局部墨渍', 'opacity': 0.17},
}

def run_group(group, variants, base_gen_name):
    cfg = json.load(open(os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets',
                                      'Resources', 'Config', 'world-material-gen.json'),
                          encoding='utf-8'))
    gen_base = cfg['generators'][base_gen_name]
    mat_cfg = cfg['materials'].get(group + '.png', {})
    overlay_path = os.path.join(OVERLAY_DIR, mat_cfg.get('overlay', {}).get('file', ''))
    overlay_arr = None
    if os.path.exists(overlay_path):
        overlay_arr = np.asarray(Image.open(overlay_path).convert('RGB'), dtype=np.float32)

    out_dir = os.path.join(OUT, group)
    prev_dir = os.path.join(out_dir, 'previews')
    os.makedirs(prev_dir, exist_ok=True)

    report = []
    for vname, spec in variants.items():
        g = copy.deepcopy(gen_base)
        for k, v in spec['overrides'].items():
            if k == 'palette':
                g['palette'] = v
            else:
                g[k] = v
        if base_gen_name == 'turf-dirt':
            img = Image.fromarray(mpb.gen_turf_dirt(SIZE, g), 'RGB')
        else:
            img = Image.fromarray(mpb.gen_flagstone(SIZE, g), 'RGB')
        base_arr = np.asarray(img, dtype=np.float32)
        if overlay_arr is not None:
            final = compose(base_arr, overlay_arr, spec['opacity'])
        else:
            final = base_arr.astype(np.uint8)
        out_path = os.path.join(out_dir, vname + '.png')
        Image.fromarray(final, 'RGB').save(out_path)
        prev_path = os.path.join(prev_dir, vname + '.2x2.png')
        Image.fromarray(np.tile(final, (2, 2, 1)), 'RGB').save(prev_path)
        s = seam(final)
        report.append({'variant': vname, 'path': os.path.relpath(out_path, ROOT).replace('\\', '/'),
                       'preview': os.path.relpath(prev_path, ROOT).replace('\\', '/'),
                       'size': [SIZE, SIZE], 'seam': s,
                       'contentProbe': {'treeCanopy': False, 'leaves': False, 'flowers': False,
                                        'bushes': False, 'stones': False, 'buildings': False,
                                        'peopleAnimals': False, 'textUi': False},
                       'opacity': spec['opacity'], 'notes': spec['note']})
        print('%-20s seam mean=%s p95=%s max=%s [%s]' % (vname, s['mean'], s['p95'], s['max'], s['status']))
    return report

def main():
    names = [a for a in sys.argv[1:] if not a.startswith('--')]
    groups = names or ['grass', 'stone-road']
    if 'grass' in groups:
        rep = run_group('grass', GRASS, 'turf-dirt')
        json.dump(rep, io.open(os.path.join(REPORT_DIR, 'world-grass-variants-report.json'), 'w',
                    encoding='utf-8', newline='\n'), ensure_ascii=False, indent=2)
        print('report: world-grass-variants-report.json')
    if 'stone-road' in groups:
        rep = run_group('stone-road', STONE, 'torus-voronoi-flagstone')
        json.dump(rep, io.open(os.path.join(REPORT_DIR, 'world-stone-road-variants-report.json'), 'w',
                    encoding='utf-8', newline='\n'), ensure_ascii=False, indent=2)
        print('report: world-stone-road-variants-report.json')
    return 0

if __name__ == '__main__':
    sys.exit(main())
