#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""清理已归一化的八帧纸底走帧（numpy / Pillow / scipy）。

使用逐格纸色、背景连通域与封闭缝隙遮罩去底，再对三像素轮廓带做颜色去污染。
保留内部 RGB、原始帧顺序、画布尺寸与姿态，不缩放、不补帧、不调验收阈值。
本工具面向本批深色衣物纸底素材；新的浅色服装必须先人工看对照图，不能仅凭闸门放行。
输出只能进入候选目录，人工复核和独立门禁通过后再复制 PNG 到 Resources；不写 .meta。

sources JSON: [{"name":"...-walksheet-...png", "source":"相对仓库路径",
                "sha256":"源文件指纹", "stripNeutralGround":false}, ...]

python Tools/clean-walksheets.py --sources sources.json --out .batch/cleaned
python Tools/clean-walksheets.py --selftest
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as ndi
import world_paths as wp

ROOT = Path(wp.repo_root())
PAPER = np.array([248, 240, 222], dtype=np.float32)
_spec = importlib.util.spec_from_file_location("walksheet_cutout_gate", Path(__file__).with_name("check-walksheet-cutout.py"))
gate = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gate)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def clean_frame(cell):
    """逐格去纸底，内部颜色保持原样；仅轮廓带估计前景色与 alpha。"""
    rgb = cell[:, :, :3].astype(np.float32)
    original = cell[:, :, 3]
    if not (original > 200).any():
        raise ValueError("输入格没有不透明人物")
    paper = gate._paper_color(rgb.astype(int), original, original.size).astype(float)
    distance = np.max(np.abs(rgb - paper), axis=2)
    candidate = (distance <= 30) | (rgb.min(axis=2) >= 242)
    labels, _ = ndi.label(candidate & (original > 0))
    sizes = np.bincount(labels.ravel())
    edge = ndi.binary_dilation(original == 0, iterations=2)
    edge[0, :] = edge[-1, :] = True
    edge[:, 0] = edge[:, -1] = True
    touched = np.unique(labels[edge])
    touched = touched[touched != 0]
    large = np.flatnonzero(sizes >= 500)
    large = large[large != 0]
    remove = np.isin(labels, np.union1d(touched, large))

    # 手指、腿间的封闭纸色洞不连通画布边缘。头部内部的小块高光保留。
    exact = ((distance <= 16) | (np.max(np.abs(rgb - PAPER), axis=2) <= 16)
             | (rgb.min(axis=2) >= 245)) & (original > 0)
    exact_labels, count = ndi.label(exact)
    exact_sizes = np.bincount(exact_labels.ravel())
    surface_distance = ndi.distance_transform_edt((original > 200) & ~remove)
    yy = np.arange(original.shape[0])[:, None]
    head_interior = (yy < original.shape[0] * .30) & (surface_distance > 5)
    outside_head = np.bincount(exact_labels.ravel(), weights=(~head_interior).ravel(), minlength=count + 1)
    kill = (exact_sizes >= 3) & ~((exact_sizes < 400) & (outside_head == 0))
    kill[0] = False
    remove |= kill[exact_labels]
    alpha = original.copy()
    alpha[remove] = 0
    # 丢弃与人物脱离的纸屑、矩形边缘碎片；保留原画布坐标。
    fg_labels, _ = ndi.label(alpha > 0)
    areas = np.bincount(fg_labels.ravel())
    areas[0] = 0
    if not areas.any():
        raise ValueError("清理后人物为空，拒绝生成")
    alpha[fg_labels != areas.argmax()] = 0

    # 三像素轮廓带的 C = aF + (1-a)B 去污染。颜色参考避开纸色，
    # 处理范围仍由几何轮廓决定，不能把人物内部高光当作边缘改色。
    interior = ndi.binary_erosion(alpha > 200, iterations=3)
    solid = interior & ~exact
    if not solid.any():
        raise ValueError("缺少用于轮廓去污染的内部像素")
    _, indices = ndi.distance_transform_edt(~solid, return_indices=True)
    foreground = rgb[indices[0], indices[1]]
    band = (alpha > 0) & ~interior
    direction = foreground - paper
    mix = np.sum((rgb - paper) * direction, axis=2) / np.maximum(np.sum(direction * direction, axis=2), 1)
    mix = np.clip(mix, 0, 1)
    alpha[band] = np.minimum(alpha[band], np.round(255 * mix[band]).astype(np.uint8))
    rgb[band] = foreground[band]
    pale = ((np.max(np.abs(rgb - PAPER), axis=2) < 18)
            | (np.max(np.abs(rgb - paper), axis=2) < 18) | (rgb.min(axis=2) > 242))
    contour = ndi.distance_transform_edt(alpha > 200) <= 5
    alpha[pale & contour] = 0
    rgb[alpha == 0] = 0
    return np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), alpha])


def strip_neutral_ground(cell):
    """仅由源清单点名启用：农户侧面鞋外的浅灰长条；不使用它处理鞋袜。"""
    cell = cell.copy()
    rgb = cell[:, :, :3].astype(int)
    alpha = cell[:, :, 3]
    rows = np.flatnonzero((alpha > 128).any(axis=1))
    yy = np.indices(alpha.shape)[0]
    neutral = ((rgb.max(2) - rgb.min(2) < 45) & (rgb.mean(2) > 95)
               & (yy > int(rows[-1]) - 100) & (alpha > 0))
    labels, count = ndi.label(neutral)
    remove = np.zeros(count + 1, dtype=bool)
    for label, box in enumerate(ndi.find_objects(labels), 1):
        if box is None:
            continue
        height = box[0].stop - box[0].start
        width = box[1].stop - box[1].start
        # 袜带与鞋边远窄于 90px；细长地面块才允许清掉。
        if width >= 90 and width / max(height, 1) >= 3:
            remove[label] = True
    alpha[remove[labels]] = 0
    labels, _ = ndi.label(alpha > 0)
    areas = np.bincount(labels.ravel())
    areas[0] = 0
    alpha[labels != areas.argmax()] = 0
    cell[alpha == 0, :3] = 0
    return cell


def erase_reviewed_ground(cell, boxes):
    """按逐帧人工复核矩形清理脚外灰块，保留同框内较暖的鞋底。"""
    cell = cell.copy()
    rgb = cell[:, :, :3].astype(int)
    neutral = (rgb.max(2) - rgb.min(2) < 50) & (rgb.mean(2) > 80)
    region = np.zeros(cell.shape[:2], dtype=bool)
    for x0, y0, x1, y1 in boxes:
        if not (0 <= x0 < x1 <= cell.shape[1] and 0 <= y0 < y1 <= cell.shape[0]):
            raise ValueError("人工复核遮罩越界")
        region[y0:y1, x0:x1] = True
    cell[neutral & region, 3] = 0
    cell[cell[:, :, 3] == 0, :3] = 0
    return cell


def selftest():
    cell = np.zeros((640, 400, 4), dtype=np.uint8)
    cell[10:630, 20:380] = (248, 240, 222, 255)
    cell[100:540, 125:275] = (75, 100, 65, 255)
    cell[50:140, 160:240] = (185, 143, 104, 255)
    cell[70:76, 190:196] = (248, 240, 222, 255)  # 头部内部高光必须保留
    cell[260:290, 185:215] = (248, 240, 222, 255)  # 封闭背景洞
    cell[500:610, 135:165] = (80, 65, 40, 255)
    cell[500:610, 235:265] = (80, 65, 40, 255)
    result = clean_frame(cell)
    checks = {
        "paper-card-transparent": bool((result[20:40, 40:360, 3] == 0).all()),
        "closed-hole-transparent": bool((result[265:285, 190:210, 3] == 0).all()),
        "interior-highlight-preserved": bool(np.array_equal(result[70:76, 190:196], cell[70:76, 190:196])),
        "body-colors-preserved": bool(np.array_equal(result[350:400, 170:230], cell[350:400, 170:230])),
        "both-feet-preserved": bool((result[550:600, 145:155, 3] == 255).all() and (result[550:600, 245:255, 3] == 255).all()),
        "no-new-opaque-pixels": bool((result[:, :, 3] <= cell[:, :, 3]).all()),
    }
    ground_case = result.copy()
    ground_case[605:615, 40:350] = (180, 172, 155, 255)
    ground_case[540:550, 138:162] = (180, 172, 155, 255)
    stripped = strip_neutral_ground(ground_case)
    checks["long-ground-strip-removed"] = bool((stripped[610:615, 60:120, 3] == 0).all())
    checks["sock-and-feet-preserved"] = bool(
        (stripped[540:550, 138:162, 3] == 255).all()
        and (stripped[580:600, 145:155, 3] == 255).all()
        and (stripped[580:600, 245:255, 3] == 255).all())
    try:
        clean_frame(np.zeros_like(cell))
    except ValueError:
        checks["empty-input-rejected"] = True
    else:
        checks["empty-input-rejected"] = False
    for name, ok in checks.items():
        print("SELFTEST:", "PASS" if ok else "FAIL", name)
    return 0 if all(checks.values()) else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--sources", type=Path)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    if args.sources is None or args.out is None:
        ap.error("需要 --sources 和 --out")
    out = args.out.resolve()
    resources = Path(wp.unity_project_root()) / "Assets/Resources"
    if out.is_relative_to(resources.resolve()):
        ap.error("先输出候选目录，独立验收后再入库")
    entries = json.loads(args.sources.read_text(encoding="utf-8"))
    names = [e["name"] for e in entries]
    if not names or len(names) != len(set(names)):
        ap.error("源清单为空或有重复输出文件")
    for entry in entries:
        name = entry["name"]
        source = ROOT / entry["source"]
        if Path(name).name != name or not name.endswith(".png"):
            ap.error("输出名必须是 PNG 文件名")
        if source.resolve() == out / name:
            ap.error("禁止原地覆盖源图")
        if sha256(source) != entry["sha256"]:
            ap.error("源图指纹不符：" + str(source))
    out.mkdir(parents=True, exist_ok=True)
    report = []
    failed = 0
    for entry in entries:
        source = ROOT / entry["source"]
        arr = gate.read_sheet(source)
        cw = arr.shape[1] // gate.CELLS
        frames = []
        for i in range(gate.CELLS):
            frame = clean_frame(arr[:, i*cw:(i+1)*cw])
            if entry.get("stripNeutralGround", False):
                frame = strip_neutral_ground(frame)
            boxes = entry.get("reviewedGroundBoxes", {}).get(str(i), [])
            if boxes:
                frame = erase_reviewed_ground(frame, boxes)
            frames.append(frame)
        cleaned = np.concatenate(frames, axis=1)
        destination = out / entry["name"]
        Image.fromarray(cleaned).save(destination)
        # 检查磁盘产物；唯一判据来自独立门禁，工具自己不重新解释阈值。
        metrics = gate.analyze(gate.read_sheet(destination))
        verdicts = gate.verdicts(metrics)
        ok = all(v[0] for v in verdicts.values())
        failed += not ok
        report.append({**entry, "outputSha256": sha256(destination),
                       "size": list(metrics["size"]), "verdicts": verdicts,
                       "alphaNeverIncreased": bool((cleaned[:, :, 3] <= arr[:, :, 3]).all())})
        print("RESULT:", "PASS" if ok else "FAIL", entry["name"], flush=True)
    (out / "cleanup-manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("RESULT: %s (%d/%d sheets)" % ("PASS" if failed == 0 else "FAIL", len(entries)-failed, len(entries)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
