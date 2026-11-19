# -*- coding: utf-8 -*-
"""Gate B-fix2 第八步（§8）：合成最终材质 = 程序基底 + 低结构 overlay。

合成公式（§8.2）：
    overlayEffect = (overlay - 128.0) / 128.0
    result        = base * (1.0 + overlayEffect * opacity)
    clip(0, 255)

opacity 上限 0.18；overlay 接缝为 WARN 时降到 maxOpacityIfOverlaySeamWarn；
最终仍 FAIL 时按 0.08 → 去掉 overlay（仅基底）逐级降级，基底必须自身 PASS。

输出：
    Assets/ArtSource/world/materials/{name}.png                最终材质
    Assets/ArtSource/world/materials/previews/{name}.png.2x2.png
    Tools/logs/world-final-material-report.json

water.png 为 useExistingFinal：从 Unity 工程内已 PASS 的材质复制到根 ArtSource。

用法：
    python Tools/compose-final-material.py --names grass stone-road
    python Tools/compose-final-material.py --all
"""
import argparse, io, json, os, shutil, sys, hashlib
import numpy as np
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

CFG = os.path.join(wp.config_root(), 'world-material-gen.json')
LEGACY_MAT = os.path.join(wp.art_source_root(), 'world', 'materials')

sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import importlib.util as _ilu


def _load(mod_file, mod_name):
    spec = _ilu.spec_from_file_location(mod_name, os.path.join(ROOT, 'Tools', mod_file))
    m = _ilu.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


B = _load('make-procedural-base.py', 'mpb')
T = _load('make-tileable-texture.py', 'mtt')


def overlay_report():
    p = os.path.join(ROOT, 'Tools', 'logs', 'world-overlay-report.json')
    if os.path.exists(p):
        return json.load(io.open(p, encoding='utf-8'))
    return {}


def load_rgb(path):
    return np.asarray(Image.open(path).convert('RGB'), dtype=np.float32)


def compose(base, overlay, opacity):
    eff = (overlay - 128.0) / 128.0
    return np.clip(base * (1.0 + eff * float(opacity)), 0, 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--names', nargs='*', default=[])
    ap.add_argument('--all', action='store_true')
    args = ap.parse_args()

    cfg = json.load(io.open(CFG, encoding='utf-8'))
    size = int(cfg['size'])
    out_cfg = cfg['outputs']
    proc_dir = wp.resolve_repo_path(out_cfg['proceduralDir'])
    ov_dir = wp.resolve_repo_path(out_cfg['overlayDir'], allow_missing=True)
    final_dir = wp.resolve_repo_path(out_cfg['finalDir'], allow_missing=True)
    prev_dir = wp.resolve_repo_path(out_cfg['previewDir'], allow_missing=True)
    for d in (final_dir, prev_dir):
        wp.ensure_dir(d)

    names = [n[:-4] if n.endswith('.png') else n for n in args.names]
    if args.all or not names:
        names = [k[:-4] for k in cfg['materials']]

    ov_rep = overlay_report()
    report_path = os.path.join(ROOT, 'Tools', 'logs', 'world-final-material-report.json')
    # 增量合并：--names 只跑子集时不能丢掉其它材质的既有结论
    # （否则 check-world-materials.py 会把未参与本次运行的材质判为 UNKNOWN/FAIL）。
    report = {}
    if os.path.exists(report_path):
        try:
            report = json.load(io.open(report_path, encoding='utf-8'))
        except Exception:
            report = {}
    if args.names and not args.all:
        print('增量模式：仅重算 %s，保留报告中其余 %d 项' % (
            ', '.join(n + '.png' for n in names),
            len([k for k in report if k not in {n + '.png' for n in names}])))

    for name in names:
        key = name + '.png'
        m = cfg['materials'].get(key)
        if not m:
            continue
        rec = {'sourceType': None}

        source = m.get('sourceAsset')
        if source:
            src = wp.resolve_repo_path(source['path'])
            data = open(src, 'rb').read()
            if hashlib.sha256(data).hexdigest() != source['sha256']:
                raise ValueError('Registered source changed: ' + key)
            with Image.open(io.BytesIO(data)) as image:
                expected = int(m.get('size', size))
                if image.size != (expected, expected):
                    raise ValueError('Native source dimensions differ from declaration: ' + key)
            dst = os.path.join(final_dir, key)
            if os.path.abspath(src) != os.path.abspath(dst):
                shutil.copyfile(src, dst)
            arr = load_rgb(dst); seam = B.seam_metrics(arr); status = B.verdict(seam)
            rec.update(sourceType='approved-user-art', final=os.path.relpath(dst, ROOT).replace('\\','/'),
                       finalSha256=hashlib.sha256(data).hexdigest(), finalSeam=seam, finalStatus=status, opacity=0.0)
            report[key] = rec
            print('%-14s %-5s registered native %d source' % (key, status, expected))
            continue

        # ---- useExistingFinal（water）：直接沿用已 PASS 的旧材质 ----
        if m.get('useExistingFinal'):
            src = os.path.join(LEGACY_MAT, key)
            dst = os.path.join(final_dir, key)
            if os.path.exists(src) and os.path.abspath(src) != os.path.abspath(dst):
                shutil.copyfile(src, dst)
            arr = load_rgb(dst)
            seam = B.seam_metrics(arr)
            st = B.verdict(seam)
            Image.fromarray(np.tile(arr.astype(np.uint8), (2, 2, 1)), 'RGB').save(
                os.path.join(prev_dir, key + '.2x2.png'))
            rec.update({'sourceType': 'existing-pass', 'final': os.path.relpath(dst, ROOT).replace('\\', '/'),
                        'finalSeam': seam, 'finalStatus': st, 'opacity': 0.0, 'overlayStatus': 'NONE'})
            report[key] = rec
            print('%-14s %-5s existing-pass  seam mean=%s p95=%s max=%s' % (
                key, st, seam['mean'], seam['p95'], seam['max']))
            continue

        base_path = os.path.join(proc_dir, name + '.base.png')
        if not os.path.exists(base_path):
            rec.update({'sourceType': 'procedural-base', 'error': '缺少 base'})
            report[key] = rec
            print('%-14s FAIL 缺少 base：%s' % (key, base_path))
            continue
        base = load_rgb(base_path)
        base_seam = B.seam_metrics(base)
        base_status = B.verdict(base_seam)

        ov_cfg = m.get('overlay') or {}
        overlay = None
        overlay_status = 'NONE'
        overlay_file = None
        if ov_cfg.get('enabled'):
            cand = os.path.join(ov_dir, ov_cfg['file'])
            fb = os.path.join(ov_dir, ov_cfg.get('fallbackFile', name + '-overlay.fallback.png'))
            rec_ov = ov_rep.get(ov_cfg['file'], {})
            if os.path.exists(cand) and rec_ov.get('status') in ('PASS', 'WARN'):
                overlay_file, overlay_status = cand, rec_ov['status']
            elif os.path.exists(fb):
                overlay_file, overlay_status = fb, 'FALLBACK'
            if overlay_file:
                overlay = load_rgb(overlay_file)

        # ---- opacity 决策 ----
        opacity = float(ov_cfg.get('opacity', 0.0)) if overlay is not None else 0.0
        if overlay_status == 'WARN':
            opacity = min(opacity, float(ov_cfg.get('maxOpacityIfOverlaySeamWarn', opacity)))
        if overlay_status == 'FALLBACK':
            opacity = min(opacity, float(ov_cfg.get('maxOpacityIfOverlaySeamWarn', opacity)))

        # ---- 合成 + 逐级降级 ----
        attempts = []
        if overlay is not None and opacity > 0:
            attempts.append(opacity)
            if opacity > 0.08:
                attempts.append(0.08)
            attempts.append(0.0)
        else:
            attempts.append(0.0)

        chosen = None
        for op in attempts:
            arr = compose(base, overlay, op) if (overlay is not None and op > 0) else base.astype(np.uint8)
            seam = B.seam_metrics(arr)
            st = B.verdict(seam)
            attempts_rec = {'opacity': op, 'seam': seam, 'status': st}
            rec.setdefault('attempts', []).append(attempts_rec)
            if st == 'PASS':
                chosen = (arr, seam, st, op)
                break
            if chosen is None:
                chosen = (arr, seam, st, op)      # 保留最好的一次用于报告
        arr, seam, st, op = chosen

        final_path = os.path.join(final_dir, key)
        Image.fromarray(arr, 'RGB').save(final_path)
        Image.fromarray(np.tile(arr, (2, 2, 1)), 'RGB').save(os.path.join(prev_dir, key + '.2x2.png'))

        # sourceType 区分三态：base-only / fallback overlay / grok overlay。
        # overlay_file 为 None 或最终 opacity 归零（降级到底）时都算 base-only。
        # grok 与 fallback 走同一个 cand 路径进来，只能靠文件名判定——配置里的
        # overlay.source 是描述性声明、代码从不读它，所以以实际落盘的文件名为准。
        if overlay is None or op <= 0:
            src_type = 'procedural-base-only'
        elif 'fallback' in os.path.basename(overlay_file).lower():
            src_type = 'procedural-plus-fallback-overlay'
        else:
            src_type = 'procedural-plus-grok-overlay'
        rec.update({
            'sourceType': src_type,
            'base': os.path.relpath(base_path, ROOT).replace('\\', '/'),
            'baseSeam': base_seam, 'baseStatus': base_status,
            'overlay': os.path.relpath(overlay_file, ROOT).replace('\\', '/') if overlay_file else None,
            'overlayStatus': overlay_status,
            'opacity': op,
            'final': os.path.relpath(final_path, ROOT).replace('\\', '/'),
            'finalSeam': seam, 'finalStatus': st,
        })
        report[key] = rec
        print('%-14s %-5s %s  base=%s ov=%s op=%.2f  seam mean=%s p95=%s max=%s' % (
            key, st, src_type, base_status, overlay_status, op,
            seam['mean'], seam['p95'], seam['max']))

    log_dir = wp.tools_log_root()
    wp.ensure_dir(log_dir)
    with io.open(os.path.join(log_dir, 'world-final-material-report.json'), 'w',
                 encoding='utf-8', newline='\n') as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)

    bad = [k for k, r in report.items() if r.get('finalStatus') != 'PASS']
    print('OVERALL: %s' % ('PASS' if not bad else 'FAIL'))
    for k in bad:
        print('  FAIL 项: %s' % k)
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
