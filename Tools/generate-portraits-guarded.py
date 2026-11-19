#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""受检角色图生成管线：亮度/角标/身份闸，走帧另接切帧与运动闸。

本文件正式化自 Tools/logs/tb-42-43/generate-portraits-guarded.py。
阈值与判定实现逐条继承既有工具，不在这里放宽或重写。

走帧用法：
  python Tools/generate-portraits-guarded.py --walk --view front --attempts 1
  python Tools/generate-portraits-guarded.py --walk --view back --attempts 1
  python Tools/generate-portraits-guarded.py --selftest

默认只把候选与证据写入 Tools/logs/rig-01/；只有所有数值闸通过并显式
--install，才把最终 sheet 复制到 ArtSource/originals 与 Resources。
"""
import argparse
import base64
import hashlib
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import urllib.request
from datetime import datetime
from pathlib import Path

from PIL import Image
import numpy as np

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
ASSETS = ROOT / "GameClient" / "TwelveJade" / "Assets"
PROMPT_DIR = ASSETS / "ArtSource" / "prompts" / "character-rig"
ORIGINAL_DIR = ASSETS / "ArtSource" / "originals" / "characters-rig"
RESOURCE_DIR = ASSETS / "Resources" / "Art" / "Characters"
LOG_DIR = ROOT / "Tools" / "logs" / "rig-01"
BASELINE = ROOT / "Tools" / "logs" / "tb-42-43" / "expected-baseline.json"

# gpt-image-2.5 通道（hub.oaifree.com，与 generate-building-art / generate-prop-art-v2 同一后端）：
# 请求参数集中在这里，密钥只从本机文件读取，不写入仓库。
IMAGE_ENDPOINT = "https://hub.oaifree.com/v1/images/generations"
IMAGE_MODEL = "gpt-image-2.5"
IMAGE_SIZE = "1024x1536"
IMAGE_KEY_FILE = Path(r"D:/tmp/ddyu-art-key.txt")


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


corner = load_module("corner_defect_scan", ROOT / "Tools" / "logs" / "tb-42-43" / "corner-defect-scan.py")
identity = load_module("character_identity", HERE / "check-character-identity.py")

LUMA_MIN = 80.0
LUMA_TARGET = 177.6
PALETTE_MIN = 0.50
PHASES = (
    ("contact", "L", "左腿向前，左脚跟刚刚触地、脚尖略抬；右腿向后，右脚跟抬起，仅右脚尖触地；右臂前摆，左臂后摆；躯干竖直。"),
    ("down", "L", "前腿承重、膝盖微屈，身体比 contact 略低；左腿在前，右腿在后；双臂随步态相对摆动。"),
    ("pass", "L", "左腿竖直支撑、左脚掌踩地；右腿抬起向前摆，右膝明显弯曲，右脚完全离地；身体处于本半周期最高点。"),
    ("up", "L", "右腿向前伸、准备触地，左腿向后承重；身体略降；双臂与腿部反向自然摆动。"),
    ("contact", "R", "右腿向前，右脚跟刚刚触地、脚尖略抬；左腿向后，左脚跟抬起，仅左脚尖触地；左臂前摆，右臂后摆；躯干竖直。"),
    ("down", "R", "前腿承重、膝盖微屈，身体比 contact 略低；右腿在前，左腿在后；双臂随步态相对摆动。"),
    ("pass", "R", "右腿竖直支撑、右脚掌踩地；左腿抬起向前摆，左膝明显弯曲，左脚完全离地；身体处于本半周期最高点。"),
    ("up", "R", "左腿向前伸、准备触地，右腿向后承重；身体略降；双臂与腿部反向自然摆动。"),
)

STYLE = (
    "Asset type: a single full-body walk-cycle frame for a hand-painted 2D game, "
    "fine ink linework with soft watercolour washes on warm ivory paper, muted natural colours, "
    "elegant historical Chinese indie game art set in the late Yuan / early Ming dynasty. "
    "Use the exact same character appearance as the supplied farmer-male-0 reference, not a redesign. "
    "One isolated character only, full body, no other person. "
)

SHARED_FRONT = (
    "Character appearance copied literally from the actual farmer-male-0 front reference: black long hair gathered "
    "into a slightly loose topknot bun on top of the head; no visible wide headband, only a small binding at the bun; "
    "olive-green knee-length outer robe with natural folds and a small faded stain, long sleeves hanging naturally below "
    "the elbows and never rolled; brown wide waist belt with a loose tail at the side and no metal buckle; grey-brown "
    "loose shorts ending two or three finger-widths below the knees; pale beige or grey-white cloth puttees wrapped "
    "around both calves to above the ankles; light brown straw sandals with ankle straps and woven texture. No basket, "
    "no shoulder cord, and no back equipment is visible in this straight front view. Dominant colours copied from the "
    "plate: robe #787858 and #686848, sash #583838 and #B89878, shorts #686060, puttees #B8B0A0, hair #080808, "
    "skin #D8B898, background #EDE6D2. "
)

SHARED_BACK = (
    "Character appearance copied literally from the actual farmer-male-0 back reference: black hair tied into a topknot "
    "at the top rear of the head, with a pale beige or grey-white cloth band around the bun; olive-green outer robe "
    "ending around the knees or slightly below the hips, with long unrolled sleeves hanging naturally; dark brown wide "
    "waist belt with its tail hanging down; grey-brown knee-length shorts; grey-brown calf puttees close in colour to "
    "the shorts; light brown woven straw sandals with straps. A round or oval cylindrical woven back basket is clearly "
    "carried on the back, brown straw colour, fixed by rope shoulder straps, with the back surface and top knot visible. "
    "Dominant colours copied from the plate: robe #787858 and #686848, sash #583838 and #B89878, lower garment #686060, "
    "puttees #686060, hair #080808, basket muted brown straw, background #EDE6D2. "
)

VIEW_HEAD = {
    "front": "View: straight front view, the character faces the viewer. ",
    "back": "View: straight back view, the character is seen from behind. ",
}


def md5_of(path):
    h = hashlib.md5()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def md5_bytes(blob):
    return hashlib.md5(blob).hexdigest()


def image_api_key():
    if not IMAGE_KEY_FILE.is_file():
        raise RuntimeError(f"image API key file missing: {IMAGE_KEY_FILE}")
    key = IMAGE_KEY_FILE.read_text(encoding="utf-8").strip()
    if not key:
        raise RuntimeError("image API key is empty")
    return key


def generate_image(prompt):
    payload = {
        "model": IMAGE_MODEL,
        "prompt": prompt,
        "n": 1,
        "size": IMAGE_SIZE,
        "response_format": "b64_json",
    }
    request = urllib.request.Request(
        IMAGE_ENDPOINT,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + image_api_key(),
                 "Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        body = json.loads(response.read().decode("utf-8"))
    items = body.get("data") or []
    if not items or not items[0].get("b64_json"):
        raise RuntimeError("image API response has no data[0].b64_json")
    return base64.b64decode(items[0]["b64_json"]), body.get("generation_id")


def fit_slot(im):
    """走帧槽位归一化 816x1088(3:4,与已装机产物及 Runtime 消费一致);API 原图先在内存中缩放。"""
    if im.mode in ("RGBA", "LA") or "transparency" in im.info:
        rgba = im.convert("RGBA")
        paper = Image.new("RGBA", rgba.size, (237, 230, 210, 255))
        paper.alpha_composite(rgba)
        im = paper.convert("RGB")
    else:
        im = im.convert("RGB")
    tw, th = 816, 1088
    want = tw / float(th)
    have = im.width / float(im.height)
    if have > want:
        w = int(round(im.height * want))
        x = (im.width - w) // 2
        im = im.crop((x, 0, x + w, im.height))
    else:
        h = int(round(im.width / want))
        y = max(0, (im.height - h) // 3)
        im = im.crop((0, y, im.width, y + h))
    return im.resize((tw, th), Image.LANCZOS)


def mean_luma(im):
    a = np.asarray(im.convert("RGB")).astype(np.float64)
    return float((0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]).mean()) if a.size else 0.0


def prompt_for(view, phase, side, index):
    appearance = SHARED_FRONT if view == "front" else SHARED_BACK
    _, _, motion = PHASES[index]
    return (
        STYLE + appearance + VIEW_HEAD[view] +
        "Pose: captured in one exact walk-cycle phase. " + motion + " "
        "The two feet must be visibly separated on one horizontal implied ground line, with a clear gap "
        "between the legs; arms and sleeves must remain readable against the torso. "
        "Lighting: one soft key light from the upper-left at about 45 degrees and gentle ambient fill from the "
        "lower-right; painted shading and a darker core shadow inside cloth folds so the figure reads as volume. "
        "Framing: full body from top of head to soles, centred, at least 12 percent empty margin on all sides; "
        "uniform flat warm ivory background, no scene, no ground, no cast shadow outside the figure, no gradient, "
        "no text, no labels, no logo, no watermark, no modern elements."
    )


def write_prompts(view):
    PROMPT_DIR.mkdir(parents=True, exist_ok=True)
    files = []
    for i, (phase, side, _) in enumerate(PHASES):
        name = f"farmer-male-0-walk-{view}-{i:02d}-{phase}-{side}-v1.txt"
        path = PROMPT_DIR / name
        path.write_text(prompt_for(view, phase, side, i) + "\n", encoding="utf-8")
        files.append(path)
    return files


def identity_detail(ref_path, candidate):
    ref = identity.foreground(Image.open(ref_path))
    cand = identity.foreground(candidate)
    if ref is None or cand is None:
        return {"palette": 0.0, "pass": False, "reason": "找不到前景"}
    pal = identity.intersection(ref["hist"], cand["hist"])
    aspect_delta = abs(cand["aspect"] - ref["aspect"]) / ref["aspect"]
    return {"palette": round(float(pal), 4), "palette_min": PALETTE_MIN,
            "aspect_delta": round(float(aspect_delta), 4), "pass": pal >= PALETTE_MIN}


def gate_candidate(im, ref_path, baseline):
    im = im.convert("RGB")
    luma = mean_luma(im)
    metrics = corner.measure(im)
    red, why = corner.judge(metrics, baseline)
    ident = identity_detail(ref_path, im)
    ok = luma >= LUMA_MIN and not red and ident["pass"]
    return ok, {
        "size": list(im.size), "mean_luma": round(luma, 1),
        "luma_min": LUMA_MIN, "luma_pass": luma >= LUMA_MIN,
        "corner_small": metrics["corner_small"], "corner_big": metrics["corner_big"],
        "corner_red": red, "corner_why": why, "identity": ident,
        "passed": ok,
    }


def select_best(records):
    """沿用 guarded 旧管线的选优规则：角碎块、角色块、亮度距锚点，最后按尝试序号稳定排序。"""
    passed = [r for r in records if r.get("passed")]
    if not passed:
        return None
    return min(passed, key=lambda r: (r["corner_small"], r["corner_big"],
                                      abs(r["mean_luma"] - LUMA_TARGET), r["attempt"]))


def generate_one(view, index, attempts, ref_path, baseline, candidate_dir):
    phase, side, _ = PHASES[index]
    candidate_dir.mkdir(parents=True, exist_ok=True)
    records = []
    prompt = prompt_for(view, phase, side, index)
    for attempt in range(1, attempts + 1):
        print(f"  GEN {view} f{index} {phase}-{side} attempt={attempt}/{attempts} model={IMAGE_MODEL}")
        try:
            blob, generation_id = generate_image(prompt)
            im = fit_slot(Image.open(io.BytesIO(blob)))
            ok, detail = gate_candidate(im, ref_path, baseline)
            detail.update({"attempt": attempt, "source_bytes": len(blob),
                           "source_md5": md5_bytes(blob), "generation_id": generation_id,
                           "_image": im})
            records.append(detail)
            print(f"    size={detail['size']} luma={detail['mean_luma']:.1f} 角碎块={detail['corner_small']} "
                  f"角色块={detail['corner_big']} palette={detail['identity'].get('palette', 0):.3f} "
                  f"⇒ {'PASS' if ok else 'FAIL'}")
        except Exception as err:
            records.append({"attempt": attempt, "error": f"{type(err).__name__}: {err}"})
            print(f"    GEN-FAIL {type(err).__name__}: {err}")
    best = select_best(records)
    if best is None:
        return None, records
    # 仅在亮度/角标/身份三道逐帧闸全绿且完成选优后落盘；运动闸随后对整张 sheet 执行。
    path = candidate_dir / f"f{index:02d}-{phase}-{side}-attempt-{best['attempt']:02d}.png"
    # 候选图仍只在内存中，命中记录保存的是已归一化图；重新请求不可接受，故从本轮内存候选缓存落盘。
    best_im = best.pop("_image", None)
    if best_im is None:
        # 选优记录应绑定内存图；这是内部完整性错误，不静默写入。
        raise RuntimeError("selected candidate image missing from memory")
    best_im.save(path)
    best["path"] = str(path)
    best["selected"] = True
    return path, records


def run_assemble(view, accepted, out_dir, ref_path):
    out_dir.mkdir(parents=True, exist_ok=True)
    sheet = out_dir / f"farmer-male-0-walksheet-{view}-v1.png"
    manifest = out_dir / f"farmer-male-0-walksheet-{view}-manifest.json"
    evidence = out_dir / f"evidence-{view}"
    cmd = [sys.executable, str(HERE / "assemble-walk-frames.py"), "--frames"]
    cmd.extend(str(p) for p in accepted)
    cmd.extend(["--out", str(sheet), "--manifest", str(manifest), "--frame-count", "8",
                 "--gap", "0", "--ref", str(ref_path), "--evidence", str(evidence), "--overwrite"])
    proc = subprocess.run(cmd, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    output = proc.stdout.decode("utf-8", "replace")
    (out_dir / f"assemble-{view}.out.txt").write_text(output, encoding="utf-8")
    print(output.rstrip())
    height_gate = {"ran": False, "pass": False, "reason": "extract manifest missing"}
    extract_manifest = evidence / "frames" / "manifest.json"
    if extract_manifest.is_file():
        extracted = json.loads(extract_manifest.read_text(encoding="utf-8"))
        heights = [frame["sourceH"] for frame in extracted.get("frames", [])]
        spread = extracted.get("groundSpreadPx", 0)
        height_ratio = ((max(heights) - min(heights)) / float(min(heights))) if heights and min(heights) else 1.0
        height_gate = {"ran": True, "pass": len(heights) == 8 and spread <= 6 and height_ratio <= 0.08,
                       "frame_count": len(heights), "height_min": min(heights) if heights else 0,
                       "height_max": max(heights) if heights else 0, "height_ratio": round(height_ratio, 6),
                       "ground_spread_px": spread, "height_max_ratio": 0.08,
                       "ground_spread_max_px": 6}
        if not height_gate["pass"]:
            print("  补充切帧门 FAIL：高度差或脚底散布未满足既有阈值")
    if proc.returncode == 0 and not height_gate["pass"]:
        proc_returncode = 1
    else:
        proc_returncode = proc.returncode
    return proc_returncode, sheet, manifest, evidence, output, height_gate


def run_view(view, attempts, install):
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
    ref_path = ASSETS / "Resources" / "Art" / "farmer-male-0-front.png"
    view_dir = LOG_DIR / view
    run_dir = view_dir / (datetime.now().strftime("run-%Y%m%d-%H%M%S") + f"-{os.getpid()}")
    candidate_dir = run_dir / "candidates"
    accepted_dir = run_dir / "accepted-frames"
    run_dir.mkdir(parents=True, exist_ok=True)
    prompt_files = write_prompts(view)
    records = []
    accepted = []
    for i, prompt_file in enumerate(prompt_files):
        path, attempts_records = generate_one(view, i, attempts, ref_path, baseline, candidate_dir)
        records.append({"index": i, "phase": PHASES[i][0], "side": PHASES[i][1],
                        "prompt": str(prompt_file), "attempts": attempts_records,
                        "accepted": str(path) if path else None})
        if path is None:
            continue
        accepted_dir.mkdir(parents=True, exist_ok=True)
        dst = accepted_dir / f"walk-{view}-{i:02d}-{PHASES[i][0]}-{PHASES[i][1]}-v1.png"
        shutil.copyfile(path, dst)
        accepted.append(dst)
    def clean_record(value):
        if isinstance(value, dict):
            return {key: clean_record(item) for key, item in value.items() if key != "_image"}
        if isinstance(value, list):
            return [clean_record(item) for item in value]
        return value

    report_frames = clean_record(records)
    report = {"view": view, "created_at": datetime.now().isoformat(timespec="seconds"),
              "run_dir": str(run_dir),
              "generator": {"endpoint": IMAGE_ENDPOINT, "model": IMAGE_MODEL, "size": IMAGE_SIZE,
                            "response_format": "b64_json"},
              "reference": str(ref_path), "attempts_per_frame": attempts, "frames": report_frames,
              "per_frame_gate": "brightness >= 80 AND corner gate green AND palette >= 0.50",
              "motion_gate": "extract-walk-sheet + check-walk-sheet-motion; thresholds unchanged"}
    (run_dir / "generation-manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if len(accepted) != 8:
        print(f"RESULT {view}: FAIL（逐帧过闸 {len(accepted)}/8，未拼装；不会装机）")
        return False, report
    rc, sheet, manifest, evidence, output, height_gate = run_assemble(view, accepted, run_dir, ref_path)
    report.update({"assemble_rc": rc, "sheet": str(sheet), "sheet_manifest": str(manifest),
                   "evidence": str(evidence), "extract_height_gate": height_gate,
                   "numeric_gate_pass": rc == 0})
    (run_dir / "generation-manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if rc == 0 and install:
        original = ORIGINAL_DIR / sheet.name
        resource = RESOURCE_DIR / sheet.name
        ORIGINAL_DIR.mkdir(parents=True, exist_ok=True)
        RESOURCE_DIR.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(sheet, original)
        shutil.copyfile(sheet, resource)
        print(f"  已装入 originals: {original}")
        print(f"  已装入 Resources: {resource}")
    elif rc == 0:
        print("  数值闸全绿；未 --install，正式资源未写入。")
    return rc == 0, report


def selftest():
    print("formalized guarded pipeline selftest")
    problems = 0
    problems += 0 if BASELINE.is_file() else 1
    problems += 0 if (HERE / "assemble-walk-frames.py").is_file() else 1
    problems += 0 if len(PHASES) == 8 else 1
    for view in ("front", "back"):
        text = prompt_for(view, "contact", "L", 0).lower()
        if not all(x in text for x in ("#787858", "puttee", "straw sandals", "walk-cycle phase")):
            problems += 1
    print(f"suite finished, {problems} problem(s)")
    return problems


def main():
    ap = argparse.ArgumentParser(description="RIG-01 guarded walk-frame generator")
    ap.add_argument("--walk", action="store_true", help="run walk-frame pipeline")
    ap.add_argument("--view", choices=("front", "back", "all"), default="all")
    ap.add_argument("--attempts", type=int, default=1, help="generation attempts per frame")
    ap.add_argument("--install", action="store_true", help="install only numeric-pass sheets")
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()
    if args.selftest:
        raise SystemExit(selftest())
    if not args.walk:
        raise SystemExit("本正式脚本当前用于 RIG-01，请加 --walk")
    if args.attempts < 1:
        raise SystemExit("--attempts 必须 >= 1")
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    views = ("front", "back") if args.view == "all" else (args.view,)
    results = []
    for view in views:
        ok, _ = run_view(view, args.attempts, args.install)
        results.append((view, ok))
    problems = sum(1 for _, ok in results if not ok)
    print("SUMMARY " + ", ".join(f"{v}={'PASS' if ok else 'FAIL'}" for v, ok in results))
    print(f"suite finished, {problems} problem(s)")
    raise SystemExit(1 if problems else 0)


if __name__ == "__main__":
    main()
