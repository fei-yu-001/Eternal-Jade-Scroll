# -*- coding: utf-8 -*-
"""Gate B-fix2 §9：世界地形材质验收（当前像素 + 可核验来源）。

读取 Tools/logs/world-final-material-report.json（compose-final-material.py 产出）
与 Tools/logs/world-tileable-report.json（历史记录，仅供追溯）。
历史状态不能代替当前文件：接缝重测，旧报告必须含匹配的 finalSha256；
配置 sourceAsset 的用户素材须核对批准记录、源 SHA 和构建输入中的当前材质 SHA。

sourceType 语义（任务书 §9）：
    ai-direct                Grok 直出，仅允许 grass 一次；PASS 或 PASS_WITH_WARN+内容干净
    procedural-base          纯程序基底，无 overlay
    procedural-plus-overlay  程序基底 + overlay（Grok 或 fallback）
    existing-pass            沿用旧材质（仍须当前文件哈希及接缝检查）
    approved-user-art        配置登记且源/材质 SHA 可核验的用户批准素材

判定：默认检查配置中的全部材质；任一 FAIL 或来源 UNKNOWN 即 FAIL。
内容违规（建筑/树冠/花丛/人物/文字/UI…）仍需人工目检，脚本只做像素层检查。

用法：python Tools/check-world-materials.py --files grass.png stone-road.png water.png
"""
import hashlib, io, json, os, sys
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

MAT = os.path.join(wp.art_source_root(), 'world', 'materials')
ORIG = os.path.join(MAT, 'originals')
PREV = os.path.join(MAT, 'previews')
LOG = os.path.join(wp.tools_log_root(), 'world-materials-check.log')
FINAL_REPORT = os.path.join(wp.tools_log_root(), 'world-final-material-report.json')
TILE_REPORT = os.path.join(wp.tools_log_root(), 'world-tileable-report.json')
CFG = os.path.join(wp.config_root(), 'world-material-gen.json')
COMPOSE_CFG = os.path.join(wp.config_root(), 'world-compose.json')

# 材质接缝阈值来自 world-material-gen.json.seamThresholds（Gate C1-A §2.3）。
# 缺失时才回落到内置默认值并告警——阈值不再散落硬编码在各个脚本里。
_DEFAULT_PASS = (12.0, 35.0, 90.0)
_DEFAULT_WARN = (18.0, 50.0, 120.0)


def _load_thresholds():
    st = load_json(CFG).get('seamThresholds') or {}

    def pick(block, defaults):
        src = st.get(block) or {}
        try:
            return (float(src['meanMax']), float(src['p95Max']), float(src['maxMax']))
        except (KeyError, TypeError, ValueError):
            return defaults

    pass_limits = pick('finalPass', _DEFAULT_PASS)
    warn_limits = pick('finalWarn', _DEFAULT_WARN)
    if not st:
        sys.stderr.write(
            'WARNING: world-material-gen.json seamThresholds missing, using built-in defaults.\n')
    return pass_limits, warn_limits


PASS_LIMITS, WARN_LIMITS = (12.0, 35.0, 90.0), (18.0, 50.0, 120.0)

LINES, RESULTS = [], []


def log(s):
    LINES.append(s)
    print(s)


def load_json(path):
    if not os.path.exists(path):
        return {}
    try:
        return json.load(open(path, encoding='utf-8'))
    except Exception:
        return {}


TARGET = int(load_json(CFG).get('size', 1024))      # 尺寸来自配置，不硬编码
PASS_LIMITS, WARN_LIMITS = _load_thresholds()          # 接缝阈值来自配置，缺失才回落默认值


def verdict(seam):
    m, p, x = seam['mean'], seam['p95'], seam['max']
    if m <= PASS_LIMITS[0] and p <= PASS_LIMITS[1] and x <= PASS_LIMITS[2]:
        return 'PASS'
    if m <= WARN_LIMITS[0] and p <= WARN_LIMITS[1] and x <= WARN_LIMITS[2]:
        return 'WARN'
    return 'FAIL'


def sha256_file(path):
    with open(path, 'rb') as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def valid_sha256(value):
    return (isinstance(value, str) and len(value) == 64
            and all(c in '0123456789abcdefABCDEF' for c in value))


def repo_key(path):
    return os.path.relpath(path, ROOT).replace('\\', '/')


def current_seam_metrics(arr):
    """保持原 make-procedural-base.seam_metrics 的 band=4 算法及舍入口径。"""
    tiled = np.tile(np.asarray(arr, dtype=np.int16), (2, 2, 1))
    cy, cx = tiled.shape[0] // 2, tiled.shape[1] // 2
    diffs = (np.abs(tiled[cy - 4:cy] - tiled[cy:cy + 4]),
             np.abs(tiled[:, cx - 4:cx] - tiled[:, cx:cx + 4]))
    stats = [(float(d.mean()), float(np.percentile(d, 95)), float(d.max())) for d in diffs]
    return {key: round(max(stats[0][i], stats[1][i]), 1)
            for i, key in enumerate(('mean', 'p95', 'max'))}


def material_provenance(name, rec, final, material_sha):
    """只读核验；既不补写历史报告，也不把计算出的当前哈希当作预期哈希。"""
    proof = {'materialSha256': material_sha, 'status': 'UNKNOWN'}
    issues = []
    materials = load_json(CFG).get('materials') or {}
    spec = materials.get(name)
    if not isinstance(spec, dict):
        return 'UNKNOWN', proof, ['来源 UNKNOWN：配置中缺少材质登记']
    if 'sourceAsset' in spec or rec.get('sourceType') == 'approved-user-art':
        source = spec.get('sourceAsset')
        if not isinstance(source, dict):
            return 'approved-user-art', proof, ['来源 UNKNOWN：配置 sourceAsset 缺失或无效']
        proof['sourceAsset'] = source
        for field in ('path', 'approval', 'transform'):
            if not isinstance(source.get(field), str) or not source[field].strip():
                issues.append('来源 UNKNOWN：sourceAsset.%s 缺失' % field)
        if not valid_sha256(source.get('sha256')):
            issues.append('来源 UNKNOWN：sourceAsset.sha256 缺失或无效')
        if issues:
            return 'approved-user-art', proof, issues
        source_path = wp.resolve_repo_path(source['path'], allow_missing=True)
        try:
            source_sha = sha256_file(source_path)
            proof['sourceSha256'] = source_sha
            if source_sha != source['sha256'].lower():
                issues.append('源 SHA256 不匹配：配置=%s 当前=%s' % (source['sha256'], source_sha))
            compose = load_json(COMPOSE_CFG)
            output_dir = compose.get('outputDir')
            if not isinstance(output_dir, str) or not output_dir:
                raise ValueError('world-compose.outputDir 缺失')
            manifest_path = os.path.join(wp.resolve_repo_path(output_dir, allow_missing=True),
                                         'world-build-manifest.json')
            manifest = load_json(manifest_path)
            generation = manifest.get('generation_input') or {}
            inputs = generation.get('inputs')
            if manifest.get('status') != 'complete' or not isinstance(inputs, dict):
                raise ValueError('缺少完整的构建输入清单')
            digest = hashlib.sha256(json.dumps(inputs, sort_keys=True, ensure_ascii=False,
                                               separators=(',', ':')).encode('utf-8')).hexdigest()
            if generation.get('sha256') != digest:
                raise ValueError('构建输入清单摘要不匹配')
            files = inputs.get('files')
            if not isinstance(files, dict):
                raise ValueError('构建输入缺少 files 哈希表')
            proof.update(manifest=repo_key(manifest_path), generationInputSha256=digest,
                         expectedMaterialSha256=files.get(repo_key(final)))
            # 只检查材料相关三项绑定。地图/Unity 全验收由原 world-build 检查负责。
            for label, path, actual in (('源图', source_path, source_sha),
                                        ('材质配置', CFG, sha256_file(CFG)),
                                        ('当前材质', final, material_sha)):
                expected = files.get(repo_key(path))
                if not valid_sha256(expected):
                    issues.append('来源 UNKNOWN：构建输入缺少%s SHA256' % label)
                elif expected.lower() != actual:
                    issues.append('%s SHA256 与构建输入不匹配：预期=%s 当前=%s'
                                  % (label, expected, actual))
        except (OSError, ValueError, TypeError, AttributeError) as error:
            issues.append('来源 UNKNOWN：%s' % error)
        if not issues:
            proof['status'] = 'VERIFIED'
        return 'approved-user-art', proof, issues

    # 没有源资产登记的旧流程，必须由报告中的预期哈希绑定当前 PNG。
    # 仅有 finalStatus=PASS 或一个文件路径，不足以证明报告描述的是现在这张图。
    expected = rec.get('finalSha256')
    proof['expectedMaterialSha256'] = expected
    if not valid_sha256(expected):
        issues.append('来源 UNKNOWN：历史报告缺少有效 finalSha256，不能证明当前材质')
    elif expected.lower() != material_sha:
        issues.append('当前材质 SHA256 与历史报告不匹配：预期=%s 当前=%s' % (expected, material_sha))
    if not (spec.get('baseGenerator') or spec.get('useExistingFinal')
            or (spec.get('aiDirectRetry') or {}).get('enabled')):
        issues.append('来源 UNKNOWN：配置缺少 sourceAsset 或有效生成来源')
    if issues:
        return 'UNKNOWN', proof, issues
    proof['status'] = 'VERIFIED'
    return rec.get('sourceType', 'UNKNOWN'), proof, []


def edge_is_dead(arr, width=2):
    bands = [arr[:width, :, :], arr[-width:, :, :], arr[:, :width, :], arr[:, -width:, :]]
    for b in bands:
        if b.std() < 3.0:
            mean = b.reshape(-1, 3).mean(axis=0)
            if mean.max() < 24 or mean.min() > 232:
                return True, mean.round(1).tolist()
    return False, None


def content_probe(arr):
    """内容违规的**启发式**探针（§9 content 字段）。

    脚本无法真正"看懂"图，这里用两个像素级代理指标，并在报告中标注为 heuristic：
      - strongEdgeRatio : Sobel 梯度 > 40 的像素占比
                          → 强轮廓（树冠/建筑/石头/人物/文字/UI 都会产生）
      - structureRatio  : 与 32px 高斯背景的偏差 > 28 的像素占比
                          → 大尺度结构（树冠团块、大石头、建筑）
      - biggestStructure: 最大连通结构块占全图比例

    阈值由实测标定：对已知含树冠/岩石的 grass v3 原图三个指标全部触发，
    对本轮程序化产物三个指标全部为 0。仍以人工目检为准。
    """
    lum = arr.astype(np.float32).mean(axis=2)
    out = {}
    try:
        from scipy import ndimage
    except Exception:
        out.update({'treeCanopy': None, 'leaves': None, 'flowers': None, 'bushes': None,
                    'stones': None, 'buildings': None, 'peopleAnimals': None, 'textUi': None,
                    '_method': 'scipy 不可用，内容探针跳过'})
        return out

    gx = ndimage.sobel(lum, 1)
    gy = ndimage.sobel(lum, 0)
    grad = np.hypot(gx, gy) / 4.0
    dev = np.abs(lum - ndimage.gaussian_filter(lum, 32.0))

    strong_edge = float((grad > 40.0).mean())
    structure = float((dev > 28.0).mean())
    mask = dev > 28.0
    biggest = 0.0
    if mask.any():
        lab, n = ndimage.label(mask)
        if n:
            sizes = np.bincount(lab.ravel())[1:]
            biggest = float(sizes.max()) / float(lum.size)

    out['strongEdgeRatio'] = round(strong_edge, 5)
    out['structureRatio'] = round(structure, 5)
    out['biggestStructure'] = round(biggest, 5)

    hits = strong_edge > 0.008 or structure > 0.08 or biggest > 0.004
    out['treeCanopy'] = bool(hits)
    out['leaves'] = bool(hits)
    out['bushes'] = bool(hits)
    out['stones'] = bool(hits)
    out['buildings'] = bool(hits)
    out['peopleAnimals'] = bool(hits)
    out['flowers'] = bool(hits)
    out['textUi'] = bool(hits)
    out['_method'] = 'heuristic: sobel>40 ratio, 32px-blur deviation, largest structure blob; manual review still required'
    return out


def brightness_probe(arr):
    """观感亮度探针（M8 / 裁决 D-16）：平均亮度、>238 的近白占比、>=250 的死白占比。

    **只出 WARN，永不出 FAIL**——"镇心过曝算不算坏"要用户看过图才算数（T019/T041 等选图），
    机器替人下判决就是越权。但这组数字必须每轮打印并留痕：上一轮它只存在于架构师的一句
    "我看了眼图"，脚本里零记录，于是下一轮没人知道该不该变好。
    """
    lum = arr.astype(np.float32).mean(axis=2)
    return {'meanLuma': round(float(lum.mean()), 1),
            'nearWhiteRatio': round(float((lum > 238.0).mean()), 4),
            'clippedRatio': round(float((lum >= 250.0).mean()), 4)}


MEAN_LUMA_WARN = 215.0      # 平均亮度上限（经验值：正常地面材质在 120-190 之间）
NEAR_WHITE_WARN = 0.15      # >238 的像素占比上限（15%）


def check(name, final_rep, tile_rep):
    entry = {'file': name, 'issues': [], 'warns': []}
    final = os.path.join(MAT, name)
    prev = os.path.join(PREV, name + '.2x2.png')
    rec = final_rep.get(name, {})
    if not isinstance(rec, dict):
        rec = {}
    entry['reportedSourceType'] = rec.get('sourceType', 'UNKNOWN')

    if not os.path.exists(final):
        entry['issues'].append('materials 文件不存在')
        RESULTS.append(entry)
        log('%-16s FAIL materials 缺失' % name)
        return entry

    try:
        with open(final, 'rb') as handle:
            material_bytes = handle.read()
        entry['materialSha256'] = hashlib.sha256(material_bytes).hexdigest()
        with Image.open(io.BytesIO(material_bytes)) as im:
            entry['size'] = list(im.size)
            if im.format != 'PNG':
                entry['issues'].append('格式非 PNG：%s' % im.format)
            spec = (load_json(CFG).get('materials') or {}).get(name, {})
            target = int(spec.get('size', TARGET))
            if im.size != (target, target):
                entry['issues'].append('尺寸 %dx%d（非声明原生 %d×%d）' % (im.size[0], im.size[1], target, target))
            alpha = np.asarray(im.convert('RGBA'))[:, :, 3]
            rgb = np.asarray(im.convert('RGB'), dtype=np.int16)
    except (OSError, ValueError) as error:
        entry['issues'].append('材质不可读：%s' % error)
        RESULTS.append(entry)
        log('%-16s FAIL %s' % (name, entry['issues'][-1]))
        return entry
    source_type, proof, provenance_issues = material_provenance(name, rec, final, entry['materialSha256'])
    entry.update(sourceType=source_type, provenance=proof)
    entry['issues'].extend(provenance_issues)
    if source_type == 'approved-user-art':
        rec = {}  # 用户源由配置/构建输入证明，不能混入旧石路的 base/overlay/status。
    if alpha.min() < 255:
        entry['issues'].append('非不透明（alpha=%d）' % alpha.min())
    dead, dead_mean = edge_is_dead(rgb)
    if dead:
        entry['issues'].append('四边存在纯色死边 mean=%s' % dead_mean)
    if not os.path.exists(prev):
        entry['issues'].append('2×2 preview 缺失')

    # originals 为可选（程序化材质没有 AI 原图）；存在则必须是 1:1 方形
    orig = os.path.join(ORIG, name)
    if os.path.exists(orig):
        oim = Image.open(orig)
        entry['origSize'] = list(oim.size)
        if oim.size[0] != oim.size[1]:
            entry['issues'].append('originals 非 1:1 方形：%dx%d' % oim.size)
    else:
        entry['origSize'] = None

    # 当前 PNG 的 band-4 实测是唯一接缝输入；历史 finalSeam/finalStatus 不参与判定。
    entry['seam'] = current_seam_metrics(rgb)
    entry['seamStatus'] = verdict(entry['seam'])
    entry['seamMeasurement'] = 'current-png-band4'

    entry['base'] = rec.get('base')
    entry['baseStatus'] = rec.get('baseStatus')
    entry['overlay'] = rec.get('overlay')
    entry['overlayStatus'] = rec.get('overlayStatus')
    entry['opacity'] = rec.get('opacity')

    # ---- 内容探针（§9 content 字段）----
    # existing-pass 按 §9 只校验存在性与接缝，内容探针仅记录不作判定
    # （water 的涟漪本身是高对比结构，会误触发结构探针）。
    content = content_probe(rgb)
    entry['content'] = content
    if entry['sourceType'] != 'existing-pass':
        for flag in ('treeCanopy', 'leaves', 'flowers', 'bushes', 'stones',
                     'buildings', 'peopleAnimals', 'textUi'):
            if content.get(flag):
                entry['warns'].append('内容探针命中 %s（疑似，需人工确认）' % flag)

    if entry['seamStatus'] == 'FAIL':
        entry['issues'].append('接缝 FAIL %s' % entry['seam'])
    elif entry['seamStatus'] == 'WARN':
        entry['warns'].append('接缝 WARN（需人工复核）%s' % entry['seam'])
    elif entry['seamStatus'] == 'MISSING':
        entry['issues'].append('缺少接缝报告（未跑 compose-final-material.py）')

    # ---- sourceType 规则（§9）----
    st = entry['sourceType']
    if st == 'approved-user-art':
        pass  # 来源已在 material_provenance 核验；像素和接缝门槛照常执行。
    elif st == 'existing-pass':
        entry['content'] = {'note': '沿用旧材质，仅校验存在性与接缝'}
    elif st in ('procedural-base', 'procedural-base-only',
            'procedural-plus-overlay', 'procedural-plus-fallback-overlay',
            'procedural-plus-grok-overlay'):
        if not entry['base'] or not os.path.exists(wp.resolve_repo_path(entry['base'])):
            entry['issues'].append('base 缺失：%s' % entry['base'])
        if entry['baseStatus'] != 'PASS':
            entry['issues'].append('base 未 PASS：%s' % entry['baseStatus'])
        if st in ('procedural-plus-overlay', 'procedural-plus-fallback-overlay',
                   'procedural-plus-grok-overlay'):
            if entry['overlay'] and not os.path.exists(wp.resolve_repo_path(entry['overlay'])):
                entry['issues'].append('overlay 缺失：%s' % entry['overlay'])
            if entry['overlayStatus'] == 'FAIL':
                entry['issues'].append('overlay FAIL 仍被采用')
            if entry['overlayStatus'] == 'WARN':
                entry['warns'].append('overlay 接缝 WARN，已按上限降 opacity')
    elif st == 'ai-direct':
        entry['warns'].append('AI 直出材质（仅 grass 允许一次），需人工确认内容干净')
    else:
        entry['issues'].append('sourceType 未知：%s' % st)

    # ---- 观感亮度 WARN（M8 / 裁决 D-16）----
    bright = brightness_probe(rgb)
    entry['brightness'] = bright
    if bright['meanLuma'] > MEAN_LUMA_WARN or bright['nearWhiteRatio'] > NEAR_WHITE_WARN:
        entry['warns'].append(
            '偏白 WARN：平均 %.1f（界 %g）· >238 占比 %.2f%%（界 %g%%）· >=250 死白 %.2f%%'
            % (bright['meanLuma'], MEAN_LUMA_WARN, bright['nearWhiteRatio'] * 100.0,
               NEAR_WHITE_WARN * 100.0, bright['clippedRatio'] * 100.0))

    entry['ok'] = not entry['issues']
    entry['warnOnly'] = not entry['issues'] and bool(entry['warns'])
    RESULTS.append(entry)
    log('%-16s %-5s %-24s size=%s seam=%s [%s]  base=%s ov=%s op=%s  %s' % (
        name, ('WARN' if entry['warnOnly'] else 'PASS') if entry['ok'] else 'FAIL',
        entry['sourceType'], entry.get('size'), entry.get('seam'), entry.get('seamStatus'),
        entry.get('baseStatus'), entry.get('overlayStatus'), entry.get('opacity'),
        '; '.join(entry['issues'] + entry['warns'])))
    log('  provenance=%s materialSha256=%s expectedMaterialSha256=%s sourceSha256=%s' % (
        proof['status'], entry['materialSha256'], proof.get('expectedMaterialSha256'),
        proof.get('sourceSha256')))
    return entry


def main():
    # 默认检查 world-material-gen.json 里登记的全部材质，而不是硬编码三张——
    # 否则新增的材质在无参运行下零校验，日志却照打 RESULT: PASS（Gate C1-A 复核发现）。
    default_files = sorted((load_json(CFG).get('materials') or {}).keys()) or \
        ['grass.png', 'stone-road.png', 'water.png']
    files = [a for a in sys.argv[1:] if not a.startswith('--')] or default_files
    final_rep = load_json(FINAL_REPORT)
    tile_rep = load_json(TILE_REPORT)
    log('=== world materials check (Gate B-fix2) ===')
    log('final report: %s' % ('OK' if final_rep else 'MISSING'))
    for f in files:
        check(f, final_rep, tile_rep)
    log('')
    bad = [r for r in RESULTS if not r.get('ok') and not r.get('warnOnly')]
    warns = [r for r in RESULTS if r.get('warnOnly')]
    log('RESULT: ' + ('FAIL' if bad else ('PASS_WITH_WARN' if warns else 'PASS')))
    for w in warns:
        log('WARN 项: %s — %s' % (w['file'], '; '.join(w['warns'])))
    for b in bad:
        log('FAILED 项: %s — %s' % (b['file'], '; '.join(b['issues'])))
    log('人工目检项（脚本无法判断）：建筑 / 树冠 / 树叶 / 花丛 / 灌木 / 人物 / 动物 / 独立石头 / 文字 / UI / 透视 / 是否像 3D / 过艳 / 过脏')
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(LINES) + '\n')
    return 1 if bad else 0


def selftest():
    """自检（夜1 G-3 第 3 条，2026-10-06）：喂一个**必然缺失**的材质名，断言本门禁变红且退出码非零。

    坏法：副本配置里登记 selftest-missing.png，而 materials 目录下根本没有这张图 →
    check() 走 'FAIL materials 缺失' 分支。**不生成、不删除任何真实美术文件**，
    日志也改写到 .batch/night1/selftest/；判据 / 阈值 / 期望值一律未改。
    """
    global CFG, LOG
    tmp = os.path.join(ROOT, '.batch', 'night1', 'selftest', 'world-materials')
    os.makedirs(tmp, exist_ok=True)
    cfg_copy = os.path.join(tmp, 'world-material-gen.json')
    src = load_json(CFG) or {}
    with open(cfg_copy, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'size': src.get('size', 1024),
                   'materials': {'selftest-missing.png': {'sourceType': 'SELFTEST'}}},
                  fh, ensure_ascii=False, indent=2)
    CFG, LOG = cfg_copy, os.path.join(tmp, 'world-materials-check.log')
    sys.argv = [sys.argv[0], 'selftest-missing.png']
    rc = main()
    print('SELFTEST: %s rc=%d（注入：登记了不存在的 selftest-missing.png）'
          % ('PASS 门禁确实变红' if rc != 0 else 'FAIL 缺失材质竟然没让它红', rc))
    return 0 if rc != 0 else 1


if __name__ == '__main__':
    sys.exit(selftest() if '--selftest' in sys.argv else main())
