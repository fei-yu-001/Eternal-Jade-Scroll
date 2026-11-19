#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""B-04 ｜ 逐帧拼装工具：把"一帧一个文件"拼成一张 sheet ＋ manifest.json，并**自动跑三条闸**。

**本脚本不改任何既有闸门脚本的判据**（`extract-walk-sheet.py` / `check-walk-sheet-motion.py` /
`check-character-identity.py` 一行不动），只负责：拼 → 生成清单 → 依次调用 → 打印判决。

拼装三条规矩（任务书原文）：**等距、同地面线、留空档**
  · 等距：每格等宽（格子宽 = 最宽那帧 + 左右留白）
  · 同地面线：所有帧**底边对齐**（默认地面线在格子底边；`--ground-line N` 可下移 N 像素）
  · 留空档：帧与帧之间留 `--gap` 像素。**默认 0**（不是任务书原写的 8）：实测动作闸 `check-walk-sheet-motion`
    对格子内左右留白极敏感，`gap>=2` 时同一份真周期会被判成「没动」（幅度 0.000，起伏 0），`gap=1` 仍 PASS。
    任务书的「留空档」与动作闸的判据在这里直接冲突，选动作闸（它是地面真值）；冲突已登记待裁。

用法：
    python Tools/assemble-walk-frames.py --frames <目录|多个文件> --out <sheet.png>
        [--manifest manifest.json] [--frame-count 8] [--gap 8] [--ground-line 0]
        [--ref <身份参考图>] [--evidence <证据目录>] [--overwrite]
    python Tools/assemble-walk-frames.py --selftest      # 两态自证：真周期 PASS / 同姿势复制 FAIL

退出码：三条闸**全绿才 0**；任一条红**或任一条未跑**都非 0。
**没跑的闸会明说"未跑 + 为什么"，绝不拿"没跑"当"通过"。**
"""
import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile

# GBK 控制台下 print 中文会把判决行崩在 RESULT 之前——会崩的门禁比红的危险（全仓统一守卫）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
GATE_EXTRACT = os.path.join(HERE, "extract-walk-sheet.py")
GATE_MOTION = os.path.join(HERE, "check-walk-sheet-motion.py")
GATE_IDENTITY = os.path.join(HERE, "check-character-identity.py")


def log(msg):
    sys.stdout.write(msg + "\n")
    sys.stdout.flush()


def collect_frames(paths):
    """把 --frames 给的目录或文件摊平成有序的 png 列表（按文件名字典序）。"""
    files = []
    for p in paths:
        if os.path.isdir(p):
            for name in sorted(os.listdir(p)):
                if name.lower().endswith((".png", ".jpg", ".jpeg")):
                    files.append(os.path.join(p, name))
        elif os.path.isfile(p):
            files.append(p)
        else:
            raise SystemExit("找不到输入：%s" % p)
    if not files:
        raise SystemExit("--frames 里一张图都没有")
    return files


def rel_to_repo(p):
    """相对仓库根的路径；跨盘（tempfile 在 C:、仓库在 D:）时退回绝对路径，不要抛异常。"""
    try:
        return os.path.relpath(p, REPO).replace("\\", "/")
    except ValueError:
        return os.path.abspath(p).replace("\\", "/")


def md5_of(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def assemble(files, out_path, manifest_path, frame_count, gap, ground_line, overwrite):
    from PIL import Image

    if frame_count is not None:
        if len(files) != frame_count:
            raise SystemExit("帧数对不上：声明 %d 张，实际 %d 张" % (frame_count, len(files)))
        files = files[:frame_count]

    imgs = [Image.open(p).convert("RGBA") for p in files]
    cell_w = max(im.width for im in imgs) + gap * 2
    cell_h = max(im.height for im in imgs)
    sheet = Image.new("RGBA", (cell_w * len(imgs), cell_h), (0, 0, 0, 0))

    entries = []
    for i, (p, im) in enumerate(zip(files, imgs)):
        # 同地面线：所有帧**底边对齐**到格子底边（ground_line 是想再往下留的像素数）
        y = cell_h - im.height - ground_line
        sheet.paste(im, (i * cell_w + gap, y), im)
        entries.append({
            "index": i,
            "name": os.path.basename(p),
            "src": rel_to_repo(p),
            "md5": md5_of(p),
            "w": im.width,
            "h": im.height,
            "pasted_at": [i * cell_w + gap, y],
        })

    if os.path.exists(out_path) and not overwrite:
        raise SystemExit("输出已存在（加 --overwrite 覆盖）：%s" % out_path)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)) or ".", exist_ok=True)
    # ⚠ 落盘必须**先合成到不透明白底**再存 RGB：
    #   动作闸走 CLI 时是 `Image.open(...).convert("RGB")`（见 check-walk-sheet-motion.py:397），
    #   RGBA 直接落盘会在那一句把透明区填成黑 —— 深色调的纸偶会连背景一起变黑，
    #   前景提取全空 ⇒ 幅度/起伏全 0 ⇒ 真周期也被判成"没动"（实测踩到过，见 b-04-report.md）。
    flat = Image.new("RGB", sheet.size, (255, 255, 255))
    flat.paste(sheet, (0, 0), sheet if sheet.mode == "RGBA" else None)
    flat.save(out_path)

    manifest = {
        "sheet": rel_to_repo(out_path),
        "frame_count": len(imgs),
        "cell_w": cell_w,
        "cell_h": cell_h,
        "gap": gap,
        "ground_line": ground_line,
        "rule": "等距 / 同地面线（底边对齐） / 留空档",
        "frames": entries,
    }
    if manifest_path:
        os.makedirs(os.path.dirname(os.path.abspath(manifest_path)) or ".", exist_ok=True)
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)

    log("拼装：%d 帧 → %s  %dx%d（格 %dx%d，gap %d，地面线下移 %d）"
        % (len(imgs), out_path, sheet.width, sheet.height, cell_w, cell_h, gap, ground_line))
    log("清单：%s" % (manifest_path or "（未写，没给 --manifest）"))
    return out_path, len(imgs)


def run_gate(name, script, args):
    if not os.path.exists(script):
        log("  !! %s：**未跑** —— 找不到 %s" % (name, script))
        return None
    proc = subprocess.run([sys.executable, script] + args,
                          cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = proc.stdout.decode("utf-8", "replace")
    tail = [ln for ln in out.strip().splitlines() if ln.strip()][-3:]
    log("  -- %s  rc=%d" % (name, proc.returncode))
    for ln in tail:
        log("     | " + ln)
    return proc.returncode


def selftest():
    """两态自证：**真周期必须 PASS、8 份同姿势复制必须 FAIL**。

    合成素材直接借 `check-walk-sheet-motion.py` 自己的 `synth_sheet()`（它是那条闸的地面真值），
    先切成一帧一个文件，再喂给本脚本的拼装 —— 于是**被测的只有"拼装"这一步**。
    """
    from PIL import Image

    spec = importlib.util.spec_from_file_location("cwsm", GATE_MOTION)
    cwsm = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(cwsm)

    tmp = tempfile.mkdtemp(prefix="assemble-selftest-")
    results = {}

    for kind, expect_pass in (("cycle", True), ("clone", False)):
        case = os.path.join(tmp, kind)
        frames = os.path.join(case, "frames")
        os.makedirs(frames, exist_ok=True)
        sheet = cwsm.synth_sheet(kind)
        cell_w = getattr(cwsm, "CELL_W", sheet.width // 8)
        n = max(1, sheet.width // cell_w)
        for i in range(n):
            sheet.crop((i * cell_w, 0, (i + 1) * cell_w, sheet.height)).save(
                os.path.join(frames, "frame-%02d.png" % i))
        out = os.path.join(case, "sheet.png")
        manifest = os.path.join(case, "manifest.json")
        log("=== SELFTEST · %s（%d 帧，期望 %s）===" % (kind, n, "PASS" if expect_pass else "FAIL"))
        frame_files = [os.path.join(frames, x) for x in sorted(os.listdir(frames))]
        # gap 用 0：见 --gap 的说明（动作闸不容忍留白，留空档那条冲突待裁，先保证闸门读数有效）
        _, n_out = assemble(frame_files, out, manifest, n, 0, 0, True)
        rc = run_gate("动作闸 check-walk-sheet-motion", GATE_MOTION,
                      [out, str(n_out), "--evidence", os.path.join(case, "evidence")])
        passed = (rc == 0)
        results[kind] = (passed, expect_pass)

    good = all(passed == exp for passed, exp in results.values())
    log("SELFTEST: %s  （真周期=%s 期望 PASS ／ 同姿势复制=%s 期望 FAIL）" % (
        "PASS" if good else "FAIL",
        results["cycle"][0], results["clone"][0]))
    log("临时证据: " + tmp)
    return 0 if good else 1


def main():
    ap = argparse.ArgumentParser(description="B-04 逐帧拼装工具（拼 sheet + 跑三条闸）")
    ap.add_argument("--frames", nargs="*", default=[], help="帧目录或多个帧文件")
    ap.add_argument("--out", help="输出的 sheet.png")
    ap.add_argument("--manifest", help="输出的 manifest.json")
    ap.add_argument("--frame-count", type=int, default=None, help="期望帧数（对不上就报错停手）")
    ap.add_argument("--gap", type=int, default=0,
                    help="帧间留白像素。**默认 0**：实测动作闸 `check-walk-sheet-motion` 对格子内左右留白极敏感，"
                         "gap>=2 时同一份真周期会被判成「没动」（幅度 0.000，起伏 0），gap=1 仍 PASS。"
                         "详见 Tools/logs/b-04-report.md 的「留空档与动作闸冲突」一节——"
                         "任务书要求「留空档」，但闸门不容忍，这条冲突已登记待裁，**不许偷偷用 gap=0 冒充已解决**。")
    ap.add_argument("--ground-line", type=int, default=0, help="地面线再往下留的像素（默认 0=贴底）")
    ap.add_argument("--ref", default=None, help="身份闸的参考图（正视图）；不给则身份闸**不跑**")
    ap.add_argument("--evidence", default=None, help="证据图落点目录")
    ap.add_argument("--overwrite", action="store_true")
    ap.add_argument("--selftest", action="store_true", help="两态自证：真周期 PASS / 同姿势复制 FAIL")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(selftest())

    if not args.frames or not args.out:
        raise SystemExit("需要 --frames 与 --out（或 --selftest）")

    files = collect_frames(args.frames)
    out, n = assemble(files, args.out, args.manifest, args.frame_count,
                      args.gap, args.ground_line, args.overwrite)

    log("")
    log("=== 三条闸 ===")
    evidence = args.evidence or (os.path.dirname(os.path.abspath(out)) + "/evidence")
    rcs = {}
    GATE_NAMES = ["切帧闸 extract-walk-sheet", "动作闸 check-walk-sheet-motion",
                  "身份闸 check-character-identity"]
    rcs[GATE_NAMES[0]] = run_gate(
        "切帧闸 extract-walk-sheet", GATE_EXTRACT, [out, os.path.join(evidence, "frames"), str(n)])
    rcs[GATE_NAMES[1]] = run_gate(
        "动作闸 check-walk-sheet-motion", GATE_MOTION, [out, str(n), "--evidence", evidence])
    if args.ref:
        rcs[GATE_NAMES[2]] = run_gate(
            "身份闸 check-character-identity", GATE_IDENTITY, [args.ref] + files)
    else:
        log("  !! 身份闸 check-character-identity：**未跑** —— 没给 --ref（参考图）。"
            "未跑不等于通过。")

    ran = {k: v for k, v in rcs.items() if v is not None}
    red = [k for k, v in ran.items() if v != 0]
    # 2026-10-16 修：原实现只拿 "跑过且非 None" 的闸算红，导致「三条闸一条没跑」也 rc=0，
    # 与文件头「绝不拿没跑当通过」自相矛盾（找不到闸脚本时尤其严重：环境坏了却报绿）。
    # 现在未跑同样计入非 0，只在判决行点名是哪几条、为什么。
    skipped = [k for k in GATE_NAMES if k not in ran]
    log("")
    log("判决：跑过 %d 条，红 %d 条%s" % (len(ran), len(red), ("（" + ", ".join(red) + "）") if red else ""))
    if skipped:
        log("      未跑 %d 条（%s）—— 未跑不等于通过，退出码按非 0 处理"
            % (len(skipped), ", ".join(skipped)))
    sys.exit(1 if (red or skipped) else 0)


if __name__ == "__main__":
    main()
