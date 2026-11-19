# -*- coding: utf-8 -*-
"""Reproducible ground candidates ONLY; never writes Resources or map assets.

Default: replay legacy crop, then enforce equal opposite pixel edges by a compact
cosine correction over 8 pixels. This is C0 edge agreement, not a claim of smooth
normal derivatives or connected masonry joints. FFT and edge correction both
change pixel values. Existing 02:28 files stay untouched.

python Tools/make-qingshi-ground-seamless.py
python Tools/make-qingshi-ground-seamless.py --probe
python Tools/make-qingshi-ground-seamless.py --variant replay-check

All outputs are confined to two qingshi-ground-20261105 owner directories.
Numeric seam PASS is descriptive and never constitutes visual acceptance.
"""
import argparse
import csv
import hashlib
import io
import json
import os
import platform
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
import numpy as np
import PIL
from PIL import Image, ImageDraw, ImageFilter

sys.dont_write_bytecode = True
for stream in (sys.stdout, sys.stderr):
    if hasattr(stream, 'reconfigure'):
        stream.reconfigure(encoding='utf-8', errors='replace')
ROOT = str(Path(__file__).resolve().parents[1])
SOURCE = os.path.join(ROOT, 'GameClient/TwelveJade/Assets/ArtSource/incoming-20261104/ground-qingshi-flagstone-raw.jpg')
RUNTIME = os.path.join(ROOT, 'GameClient/TwelveJade/Assets/Resources/Art/Background/town-ground-seamless.png')
ARCHIVE_DIR = os.path.join(ROOT, 'GameClient/TwelveJade/Assets/ArtSource/qingshi-ground-20261105')
LOG_DIR = os.path.join(ROOT, 'Tools/logs/qingshi-ground-20261105')
TARGET = 512
SEAM_BAND = 4
CANDIDATES = [0, 70, 140, 210, 280]
# Unchanged shared seam statistic limits; no global gate is run or edited.
PASS_LIMITS = (12.0, 35.0, 90.0)
WARN_LIMITS = (18.0, 50.0, 120.0)


def sha256_file(path):
    with open(path, 'rb') as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def sha256_array(arr):
    return hashlib.sha256(np.ascontiguousarray(arr).tobytes()).hexdigest()


def load_source():
    raw = np.fromfile(SOURCE, dtype=np.uint8)
    image = Image.open(io.BytesIO(raw.tobytes())).convert('RGB')
    return image, np.asarray(image, dtype=np.uint8)


def square_crops(arr):
    height, width, _ = arr.shape
    side = min(height, width)
    for left in CANDIDATES:
        if left + side <= width:
            yield left, arr[:, left:left + side]


def periodic_seamless(arr, sign=1.0, scale=1.0):
    """Legacy FFT boundary correction, preserved for exact replay.

    Reduces boundary jumps; does NOT enforce identical opposite pixel edges or
    connect masonry joints. Correction changes pixels throughout the image.
    Clipping and uint8 truncation can change the mean after reconstruction.
    """
    image = arr.astype(np.float64)
    height, width, channels = image.shape
    residual = np.zeros_like(image)
    residual[0] += image[-1] - image[0]
    residual[-1] += image[0] - image[-1]
    residual[:, 0] += image[:, -1] - image[:, 0]
    residual[:, -1] += image[:, 0] - image[:, -1]

    fy = np.fft.fftfreq(height)[:, None]
    fx = np.fft.fftfreq(width)[None, :]
    lam = 2.0 * np.cos(2.0 * np.pi * fy) + 2.0 * np.cos(2.0 * np.pi * fx) - 4.0
    lam[0, 0] = 1.0

    out = np.empty_like(image)
    for channel in range(channels):
        field = np.fft.ifft2(np.fft.fft2(residual[:, :, channel]) / lam).real
        field -= field.mean()
        out[:, :, channel] = image[:, :, channel] - sign * scale * field
    return np.clip(out, 0, 255).astype(np.uint8)


def crossfade_seamless(arr, feather=160):
    """旧候选的半偏移三角遮罩对照；并非共享 make_tileable 的逐字实现。"""
    out = arr.astype(np.float32)
    height, width, _ = out.shape
    rolled = np.roll(out, (height // 2, width // 2), axis=(0, 1))
    radius_y = min(feather, height) / 2.0
    radius_x = min(feather, width) / 2.0
    mask_y = np.clip(1.0 - np.abs(np.arange(height) - height / 2.0) / radius_y, 0, 1)
    mask_x = np.clip(1.0 - np.abs(np.arange(width) - width / 2.0) / radius_x, 0, 1)
    mask = np.maximum(mask_y[:, None], mask_x[None, :])[:, :, None]
    return np.uint8(np.clip(out * (1 - mask) + rolled * mask, 0, 255))


def boundary_diff(arr):
    """四边对边差：末行 vs 首行、末列 vs 首列。这就是 2x2 拼接后的跨缝相邻差。"""
    image = arr.astype(np.float32)
    vertical = np.abs(image[-1] - image[0])
    horizontal = np.abs(image[:, -1] - image[:, 0])
    return {
        'vertical': {'mean': round(float(vertical.mean()), 2),
                     'p95': round(float(np.percentile(vertical, 95)), 2),
                     'max': round(float(vertical.max()), 2)},
        'horizontal': {'mean': round(float(horizontal.mean()), 2),
                       'p95': round(float(np.percentile(horizontal, 95)), 2),
                       'max': round(float(horizontal.max()), 2)},
    }


def gradient_profile(arr, band=SEAM_BAND):
    """梯度对照：图内自然相邻差 vs 跨缝相邻差 vs 缝内侧带。

    四边差数值小不等于无缝——真正要判的是缝处梯度有没有跳离纹理自身的分布。
    比值是描述量；小于1也可能来自边缘变平，不能据此断言视觉通过。
    """
    image = arr.astype(np.float32)
    height, width, _ = image.shape

    inner_vertical = np.abs(image[1:, :, :] - image[:-1, :, :]).reshape(-1, 3)
    inner_horizontal = np.abs(image[:, 1:, :] - image[:, :-1, :]).reshape(-1, 3)
    interior = np.concatenate([inner_vertical, inner_horizontal], axis=0)

    edge_rows = np.concatenate([
        np.abs(image[1:band + 1] - image[:band]).reshape(-1, 3),
        np.abs(image[-1:-band - 1:-1] - image[-2:-band - 2:-1]).reshape(-1, 3),
    ], axis=0)
    edge_cols = np.concatenate([
        np.abs(image[:, 1:band + 1] - image[:, :band]).reshape(-1, 3),
        np.abs(image[:, -1:-band - 1:-1] - image[:, -2:-band - 2:-1]).reshape(-1, 3),
    ], axis=0)
    edge_band = np.concatenate([edge_rows, edge_cols], axis=0)

    seam = np.abs(np.concatenate([
        np.abs(image[-1] - image[0]).reshape(-1, 3),
        np.abs(image[:, -1] - image[:, 0]).reshape(-1, 3),
    ], axis=0))

    def pack(values):
        return {'mean': round(float(values.mean()), 2),
                'p95': round(float(np.percentile(values, 95)), 2),
                'max': round(float(values.max()), 2)}

    interior_stats = pack(interior)
    edge_stats = pack(edge_band)
    seam_stats = pack(seam)
    return {
        'interior': interior_stats,
        'edgeBandInner': edge_stats,
        'seamCross': seam_stats,
        'seamOverInterior': round(seam_stats['mean'] / interior_stats['mean'], 3),
        'edgeOverInterior': round(edge_stats['mean'] / interior_stats['mean'], 3),
    }


def seam_metrics(arr, band=SEAM_BAND):
    """2x2 拼接后中心十字缝两侧相邻带的差异，口径与 make-tileable-texture.py 一致。"""
    image = np.tile(arr.astype(np.int16), (2, 2, 1))
    height, width, _ = arr.shape
    horizontal = np.abs(image[height - band:height, :, :].astype(np.float32)
                        - image[height:height + band, :, :].astype(np.float32)).reshape(-1, 3)
    vertical = np.abs(image[:, width - band:width, :].astype(np.float32)
                      - image[:, width:width + band, :].astype(np.float32)).reshape(-1, 3)
    values = []
    for diff in (horizontal, vertical):
        values.append((float(diff.mean()), float(np.percentile(diff, 95)), float(diff.max())))
    return {
        'mean': round(max(values[0][0], values[1][0]), 1),
        'p95': round(max(values[0][1], values[1][1]), 1),
        'max': round(max(values[0][2], values[1][2]), 1),
    }


def natural_baseline(arr, samples=400, band=SEAM_BAND):
    """对照组基线：图内自然相邻带差异。拼接缝不该比纹理自身起伏更突兀。"""
    image = arr.astype(np.float32)
    height, width, _ = image.shape
    rng = np.random.default_rng(20261105)
    rows = rng.integers(0, height - 2 * band, samples)
    cols = rng.integers(0, width - 2 * band, samples)
    vertical = np.array([np.abs(image[r + band] - image[r]).mean() for r in rows])
    horizontal = np.array([np.abs(image[:, c + band] - image[:, c]).mean() for c in cols])
    combined = np.maximum(vertical, horizontal)
    return {'mean': round(float(np.median(combined)), 1),
            'p95': round(float(np.percentile(combined, 95)), 1)}


def corner_metrics(arr, size=16):
    """四角块两两比较：拼接后四个角必须在灰度上对得上。"""
    image = arr.astype(np.float32)
    corners = {
        'tl': image[:size, :size],
        'tr': image[:size, -size:],
        'bl': image[-size:, :size],
        'br': image[-size:, -size:],
    }
    pairs = [('tl', 'tr'), ('tl', 'bl'), ('br', 'tr'), ('br', 'bl')]
    worst = max(float(np.abs(corners[a] - corners[b]).mean()) for a, b in pairs)
    return round(worst, 2)


def quadrant_metrics(arr):
    """2x2 四个象限的亮度：查低频明暗断层（拼完一块亮一块暗）。"""
    image = arr.astype(np.float32)
    height, width, _ = image.shape
    half_h, half_w = height // 2, width // 2
    blocks = {
        'tl': image[:half_h, :half_w], 'tr': image[:half_h, half_w:],
        'bl': image[half_h:, :half_w], 'br': image[half_h:, half_w:],
    }
    means = {name: round(float(block.mean()), 2) for name, block in blocks.items()}
    values = list(means.values())
    return {'blockMean': means, 'spread': round(max(values) - min(values), 2)}


def verdict(seam):
    if seam['mean'] <= PASS_LIMITS[0] and seam['p95'] <= PASS_LIMITS[1] and seam['max'] <= PASS_LIMITS[2]:
        return 'PASS'
    if seam['mean'] <= WARN_LIMITS[0] and seam['p95'] <= WARN_LIMITS[1] and seam['max'] <= WARN_LIMITS[2]:
        return 'WARN'
    return 'FAIL'


def evaluate(arr):
    seam = seam_metrics(arr)
    seam['status'] = verdict(seam)
    return {
        'boundaryDiff': boundary_diff(arr),
        'seam': seam,
        'gradient': gradient_profile(arr),
        'corner': corner_metrics(arr),
        'quadrant': quadrant_metrics(arr),
    }


def score_candidates(base):
    records = []
    for left, crop in square_crops(base):
        square = np.asarray(Image.fromarray(crop).resize((TARGET, TARGET), Image.Resampling.LANCZOS))
        periodic = periodic_seamless(square, sign=1.0, scale=1.0)
        fade = crossfade_seamless(square)
        records.append({
            'left': left,
            'source': [int(crop.shape[1]), int(crop.shape[0])],
            'periodic': evaluate(periodic),
            'crossfade': evaluate(fade),
        })
    records.sort(key=lambda r: (r['periodic']['seam']['mean'], r['periodic']['seam']['p95']))
    return records


def build_repeat(tile, rows, cols):
    return np.tile(tile, (rows, cols, 1))


def build_seam_zoom(tile, zoom=4, half=48):
    """接缝特写：十字缝必须在画面正中，四倍最近邻放大，人眼看得清石缝是否接上。

    2x2 的缝在 tile 边长处，所以取 [side-half, side+half) 这段，缝落在中心。
    """
    side = tile.shape[0]
    block = build_repeat(tile, 2, 2)[side - half:side + half, side - half:side + half]
    span = half * 2 * zoom
    image = Image.fromarray(block, 'RGB').resize((span, span), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(image)
    draw.line([(0, span // 2), (span, span // 2)], fill=(255, 64, 64), width=1)
    draw.line([(span // 2, 0), (span // 2, span)], fill=(255, 64, 64), width=1)
    return image


def stats(values):
    values = np.asarray(values, dtype=np.float64)
    return {'mean': float(values.mean()), 'p95': float(np.percentile(values, 95)),
            'max': float(values.max()), 'count': int(values.size)}


def compact_edge_match(arr, band=8):
    """Equal endpoint pixel values with bounded cosine correction, not mirroring.

    For each axis, d=(last-first)/2. Add d*w(k) to first k, subtract
    from last k, where w(k)=(1+cos(pi*k/band))/2. We correct endpoint
    residuals, not blend two shifted masonry patterns. Normal gradients are
    measured separately. Round once after both axes to retain exact equality.
    """
    out = arr.astype(np.float64)
    for axis in (0, 1):
        view = np.moveaxis(out, axis, 0)
        delta = (view[-1] - view[0]) * .5
        for k in range(band):
            weight = (1 + np.cos(np.pi * k / band)) * .5
            view[k] += delta * weight
            view[-1-k] -= delta * weight
    return np.clip(np.rint(out), 0, 255).astype(np.uint8)


def delta_metrics(arr, baseline):
    delta = np.abs(arr.astype(np.float64) - baseline.astype(np.float64))
    return {**stats(delta), 'changedPixelFraction': float(np.any(delta != 0, axis=2).mean()),
            'changedChannelFraction': float((delta != 0).mean())}


def gradient_rows(arr, label, radius=16):
    rows = []
    for axis, name in ((0, 'TB'), (1, 'LR')):
        a = np.moveaxis(arr.astype(np.float64), axis, 0)
        for offset in range(-radius, radius+1):
            signed = a[offset % len(a)] - a[(offset-1) % len(a)]
            rows.append({'image': label, 'axis': name, 'offsetFromJoin': offset,
                         **stats(np.abs(signed)), 'signedMean': float(signed.mean())})
    return rows


def detail_metrics(arr):
    a = arr.astype(np.float64)
    # Exclude 16px borders from interior control to avoid mixing in corrected bands.
    control = a[16:-16, 16:-16]
    grad = np.concatenate([np.diff(control, axis=0).ravel(), np.diff(control, axis=1).ravel()])
    lap = -4*a + np.roll(a,1,0) + np.roll(a,-1,0) + np.roll(a,1,1) + np.roll(a,-1,1)
    lum = a @ np.array([.2126,.7152,.0722])
    low = np.asarray(Image.fromarray(arr).filter(ImageFilter.GaussianBlur(32))).astype(float)
    fy = np.fft.fftfreq(arr.shape[0])[:,None]
    fx = np.fft.fftfreq(arr.shape[1])[None,:]
    power = np.abs(np.fft.fft2(lum-lum.mean()))**2
    high = fx*fx + fy*fy >= .25**2
    autocorr = np.fft.ifft2(power).real
    autocorr /= max(float(autocorr[0,0]), 1e-12)
    derivatives = {}
    for axis, name in ((0, 'TB'), (1, 'LR')):
        v = np.moveaxis(a, axis, 0)
        before = v[-1]-v[-2]; cross = v[0]-v[-1]; after = v[1]-v[0]
        derivatives[name] = {'before': stats(np.abs(before)), 'cross': stats(np.abs(cross)),
                             'after': stats(np.abs(after)),
                             'slopeJumpBefore': stats(np.abs(cross-before)),
                             'slopeJumpAfter': stats(np.abs(after-cross))}
    return {'interiorExcluding16px': stats(np.abs(grad)),
            'laplacianAbs': stats(np.abs(lap)),
            'highFrequencyPowerFractionAboveQuarterCycle': float(power[high].sum()/max(power.sum(),1e-12)),
            'lowpassSigma32Range': float(low.max()-low.min()),
            'lowpassSigma32Std': float(low.std()), 'normalDerivatives': derivatives,
            'autocorrelation': {'halfWidth': float(autocorr[0,arr.shape[1]//2]),
                                'halfHeight': float(autocorr[arr.shape[0]//2,0]),
                                'fullPeriod': 1.0, 'periodPixels': TARGET,
                                'note': 'All tiling repeats exactly at one tile; this is not a repetition-quality verdict.'}}


def rel(path):
    return Path(path).relative_to(ROOT).as_posix()


def fingerprint(path):
    p = Path(path)
    if not p.exists():
        return {'path': rel(p), 'exists': False}
    b = p.read_bytes()
    return {'path': rel(p), 'exists': True, 'sha256': hashlib.sha256(b).hexdigest(), 'bytes': len(b)}


def save_once(path, data):
    """Never silently replace a candidate somebody might currently be reviewing."""
    path = Path(path)
    if path.exists() and path.read_bytes() != data:
        raise RuntimeError('Output already differs; use a NEW --variant: ' + str(path))
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def png_bytes(arr):
    b = io.BytesIO()
    (arr if isinstance(arr, Image.Image) else Image.fromarray(arr)).save(b, format='PNG')
    return b.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe', action='store_true')
    parser.add_argument('--variant', default='candidate-c0-v1')
    parser.add_argument('--left', type=int, default=0)
    parser.add_argument('--edge-band', type=int, default=8, choices=(8,16,24))
    parser.add_argument('--legacy-only', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,63}', args.variant):
        parser.error('--variant must be a plain directory name')
    watched = [Path(SOURCE), Path(RUNTIME), Path(RUNTIME+'.meta')]
    before = [fingerprint(p) for p in watched]
    source_image, base = load_source()
    scores = score_candidates(base)
    if args.probe:
        print(json.dumps(scores,ensure_ascii=False,indent=2))
        return 0
    side = min(source_image.size)
    if args.left < 0 or args.left+side > source_image.width:
        parser.error('crop outside original source')
    crop = base[:side,args.left:args.left+side]
    square = np.asarray(Image.fromarray(crop).resize((TARGET,TARGET),Image.Resampling.LANCZOS))
    legacy = periodic_seamless(square)
    tile = legacy if args.legacy_only else compact_edge_match(legacy,args.edge_band)
    archive = Path(ARCHIVE_DIR)/args.variant
    evidence = Path(LOG_DIR)/args.variant
    for output_root in (archive,evidence):
        root = output_root.resolve()
        allowed = Path(ARCHIVE_DIR if output_root==archive else LOG_DIR).resolve()
        if allowed not in root.parents:
            raise RuntimeError('Output path escapes ownership')
    images = {'town-ground-seamless.png': tile,
              'ground-qingshi-seamless-test-2x2.png': np.tile(tile,(2,2,1)),
              'ground-qingshi-seamless-test-3x3.png': np.tile(tile,(3,3,1))}
    repeat = np.tile(tile,(2,2,1)); s = TARGET
    images['seam-cross-plain-4x.png'] = Image.fromarray(repeat[s-48:s+48,s-48:s+48]).resize((384,384),Image.Resampling.NEAREST)
    images['seam-cross-marked-4x.png'] = build_seam_zoom(tile)
    images['seam-LR-full-height-2x.png'] = Image.fromarray(repeat[:s,s-32:s+32]).resize((128,1024),Image.Resampling.NEAREST)
    images['seam-TB-full-width-2x.png'] = Image.fromarray(repeat[s-32:s+32,:s]).resize((1024,128),Image.Resampling.NEAREST)
    comparison = np.concatenate([square,legacy,tile],axis=1)
    images['source-legacy-candidate.png'] = comparison
    images['delta-vs-legacy-x8.png'] = np.clip(np.abs(tile.astype(int)-legacy.astype(int))*8,0,255).astype(np.uint8)
    images['half-offset-review.png'] = np.roll(tile,(TARGET//2,TARGET//2),(0,1))
    outputs = []
    for name,im in images.items():
        data = png_bytes(im)
        save_once(evidence/name,data)
        outputs.append(fingerprint(evidence/name))
        if name in ('town-ground-seamless.png','ground-qingshi-seamless-test-2x2.png','ground-qingshi-seamless-test-3x3.png'):
            save_once(archive/name,data)
            outputs.append(fingerprint(archive/name))
    profiles = sum([gradient_rows(a,label) for label,a in [('source-square',square),('legacy',legacy),('candidate',tile)]],[])
    buf = io.StringIO(newline='')
    writer = csv.DictWriter(buf,fieldnames=list(profiles[0]))
    writer.writeheader();writer.writerows(profiles)
    save_once(evidence/'gradient-profile.csv',buf.getvalue().encode('utf-8'))
    algorithm = 'legacy-fft' if args.legacy_only else 'legacy-fft-plus-compact-cosine-endpoint-correction'
    report = {
        'schema': 2, 'status': 'CANDIDATE_WARN_VISUAL_PENDING', 'accepted': False,
        'source': {**fingerprint(SOURCE),'size':list(source_image.size)},
        'generator': {**fingerprint(__file__), 'python':platform.python_version(),
                      'numpy':np.__version__,'pillow':PIL.__version__},
        'command': f'python Tools/make-qingshi-ground-seamless.py --variant {args.variant} --left {args.left} --edge-band {args.edge_band}' + (' --legacy-only' if args.legacy_only else ''),
        'parameters': {'left':args.left,'top':0,'sourceSide':side,'targetSide':TARGET,'edgeBand':args.edge_band,'legacyOnly':args.legacy_only},
        'algorithm': algorithm, 'changesPixels':True, 'pixelDeltaVsResizedSource':delta_metrics(tile,square),
        'pixelDeltaVsLegacy':delta_metrics(tile,legacy),
        'equality': {'LR':bool(np.array_equal(tile[:,0],tile[:,-1])),
                     'TB':bool(np.array_equal(tile[0],tile[-1])),
                     'fourCorners':bool(np.all(np.stack([tile[0,0],tile[0,-1],tile[-1,0],tile[-1,-1]])==tile[0,0]))},
        'legacyMetrics':evaluate(legacy),'candidateMetrics':evaluate(tile),
        'rawPixelSha256':sha256_array(tile),'legacyCropScores':scores,
        'thresholds':{'PASS':PASS_LIMITS,'WARN':WARN_LIMITS,'sharedDefinition':fingerprint(Path(ROOT)/'Tools/make-tileable-texture.py'),
                      'scope':'4px seam statistic only; not visual approval; unchanged'},
        'detail':{'sourceSquare':detail_metrics(square),'legacy':detail_metrics(legacy),'candidate':detail_metrics(tile)},
        'gradientProfile':'gradient-profile.csv','outputs':outputs,
        'review':{'humanApproval':'pending','legacyReportRemains':'WARN',
                  'risks':['Masonry-joint correspondence remains unresolved.',
                           '512px pattern repeats; source crop contains only 360px of native detail.',
                           'Identical edge pixels create one flat cross-edge step; C1 gradients are not guaranteed.',
                           'No in-engine or map integration validation in this line.'],
                  'stateSeparation':{'candidate':rel(archive/'town-ground-seamless.png'),
                                     'existingResources':before[1:],
                                     'mapInput':'GameClient/TwelveJade/Assets/ArtSource/world/materials/stone-road.png; audited separately'}},
        'sourceAndRuntimeUnchanged':before==[fingerprint(p) for p in watched],
        'runtimeWritten':False, 'mapWritten':False}
    if not report['sourceAndRuntimeUnchanged']:
        raise RuntimeError('Source or existing runtime changed concurrently; evidence needs recheck')
    save_once(evidence/'report.json',(json.dumps(report,ensure_ascii=False,indent=2)+'\n').encode('utf-8'))
    print(json.dumps({'status':report['status'],'equality':report['equality'],
                      'numericSeamOnly':report['candidateMetrics']['seam'],'candidate':rel(archive/'town-ground-seamless.png'),
                      'sha256':sha256_file(archive/'town-ground-seamless.png'),
                      'report':rel(evidence/'report.json'),'runtimeWritten':False},ensure_ascii=False,indent=2))
    return 0


if __name__=='__main__':
    sys.exit(main())
