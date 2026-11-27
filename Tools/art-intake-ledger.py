#!/usr/bin/env python3
"""美术入库台账：把"盘上现在这一堆素材各自过没过闸门"变成可机械再生的产物（T126）。

为什么要有它：闸门一直在跑，结论也一直如实报，但**只活在批次日志里**
（Tools/logs/<批次>/report.md 记的是"这一轮做了什么"）。于是谁要回答
"现在盘上这 9 张走帧里哪几张是坏的"，只能去翻日志。这个脚本回答的是
"盘上现在是什么状态"，并把答案钉成一份带生成命令与闸门指纹的文件。

用法:
  python Tools/art-intake-ledger.py            # 现跑闸门 -> 写 Tools/ArtSources/walk-sheet-ledger.md
  python Tools/art-intake-ledger.py --check     # 不写盘，比对盘上台账是否已过期（rc=1 表示过期）
  python Tools/art-intake-ledger.py --stdout    # 只打印，不写盘

台账口径：只认闸门自己的退出码与 RESULT 行原文——判级与数字都不许在这里二次解释，
否则台账就成了第二份真相。闸门脚本的 sha256 一并记下：闸门口径一变，台账即过期。
"""
import argparse
import datetime
import hashlib
import pathlib
import re
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parent.parent
GATE = ROOT / "Tools" / "check-walk-sheet-motion.py"
# 2026-11-03 素材整合后，运行时走帧 sheet 从 Art/Characters/ 移到 Art/Characters/Walk/。
CHARACTERS = ROOT / "GameClient" / "TwelveJade" / "Assets" / "Resources" / "Art" / "Characters" / "Walk"
LEDGER = ROOT / "Tools" / "ArtSources" / "walk-sheet-ledger.md"
STAMP_PREFIX = "生成时间"

# 台账里逐行记的那几条判据：键 -> 从闸门输出里抓的 (行内定位串, 数值正则)
METRICS = (
    ("帧高", r"帧高 (\d+)\.\.(\d+)"),
    ("B 起伏(px)", r"B 整体垂直起伏 topSpread\s*=\s*(\d+)"),
    ("C 触地变化(%)", r"C 触地分布变化\s*=\s*([\d.]+)%"),
    ("D 帧高偏离", r"D 帧内实体完整性 heightDev\s*=\s*([\d.]+)"),
    ("A 姿态幅度", r"A 姿态幅度\s+poseSpread\s*=\s*([\d.]+)"),
)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def run_gate(sheet):
    """跑一次闸门，返回 (rc, 输出原文)。闸门判级只认它自己的退出码。"""
    result = subprocess.run([sys.executable, str(GATE), str(sheet)],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    return result.returncode, (result.stdout or "") + (result.stderr or "")


def verdict_line(output):
    for line in output.splitlines():
        if line.startswith("RESULT:"):
            return line.strip()
    return "(闸门没有输出 RESULT 行)"


def metric(output, pattern):
    found = re.search(pattern, output)
    if not found:
        return "—"
    return found.group(1) if len(found.groups()) == 1 else "%s..%s" % found.groups()


def collect():
    if not GATE.is_file():
        raise SystemExit("闸门脚本不在：" + str(GATE))
    sheets = sorted(CHARACTERS.glob("*-walksheet-*.png"))
    if not sheets:
        raise SystemExit("没找到走帧素材：" + str(CHARACTERS))
    rows = []
    for sheet in sheets:
        rc, output = run_gate(sheet)
        rows.append({
            "name": sheet.name,
            "rc": rc,
            "verdict": "PASS" if rc == 0 else "FAIL",
            "result": verdict_line(output),
            "metrics": [(label, metric(output, pattern)) for label, pattern in METRICS],
        })
    return rows


def render(rows, command):
    failed = [row for row in rows if row["rc"] != 0]
    lines = []
    lines.append("# 美术入库台账 · 走帧素材")
    lines.append("")
    lines.append("本文件由脚本生成，**禁止手抄**。判级只认闸门退出码，数字只从闸门输出原样摘录——")
    lines.append("台账答「盘上现在是什么状态」，批次日志答「这一轮做了什么」，两者都要有（T126）。")
    lines.append("")
    lines.append("- 生成命令：`%s`" % command)
    lines.append("- 闸门：`Tools/check-walk-sheet-motion.py`（sha256 前 16 位 `%s`）——闸门一变，本台账即过期，重跑。" % sha256(GATE))
    lines.append("- 素材目录：`GameClient/TwelveJade/Assets/Resources/Art/Characters/Walk/`")
    lines.append("- 盘上 %d 张：**PASS %d / FAIL %d**" % (len(rows), len(rows) - len(failed), len(failed)))
    lines.append("")
    lines.append("| 素材 | 判级 | " + " | ".join(label for label, _ in METRICS) + " |")
    lines.append("|---|---|" + "---|" * len(METRICS))
    for row in rows:
        cells = " | ".join(value for _, value in row["metrics"])
        lines.append("| `%s` | %s | %s |" % (row["name"], row["verdict"], cells))
    lines.append("")
    lines.append("## 闸门 RESULT 行原文（判级凭据）")
    lines.append("")
    lines.append("```text")
    for row in rows:
        lines.append("[rc=%d %s] %s" % (row["rc"], row["name"], row["result"]))
    lines.append("```")
    lines.append("")
    if failed:
        lines.append("## 待裁决（FAIL 的素材）")
        lines.append("")
        for row in failed:
            lines.append("- `%s`" % row["name"])
        lines.append("")
    lines.append("<!-- %s：%s -->" % (STAMP_PREFIX, datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")))
    return "\n".join(lines) + "\n"


def strip_stamp(text):
    return "\n".join(line for line in text.splitlines() if not line.startswith("<!-- " + STAMP_PREFIX))


def main():
    parser = argparse.ArgumentParser(description="走帧素材入库台账（可机械再生）")
    parser.add_argument("--check", action="store_true", help="只比对盘上台账是否过期，不写盘")
    parser.add_argument("--stdout", action="store_true", help="只打印，不写盘")
    args = parser.parse_args()

    command = "python Tools/art-intake-ledger.py"
    rows = collect()
    text = render(rows, command)

    if args.stdout:
        print(text)
        return 0

    if args.check:
        if not LEDGER.is_file():
            print("RESULT: FAIL 台账不存在，需要生成：" + str(LEDGER))
            return 1
        if strip_stamp(LEDGER.read_text(encoding="utf-8")) != strip_stamp(text):
            print("RESULT: FAIL 台账已过期（闸门口径或素材变了）：重跑 python Tools/art-intake-ledger.py")
            return 1
        print("RESULT: PASS 台账是最新的（%d 张，PASS %d / FAIL %d）" % (
            len(rows), sum(1 for row in rows if row["rc"] == 0), sum(1 for row in rows if row["rc"] != 0)))
        return 0

    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    LEDGER.write_text(text, encoding="utf-8", newline="\n")
    print("已写出 " + str(LEDGER))
    print("盘上 %d 张：PASS %d / FAIL %d" % (
        len(rows), sum(1 for row in rows if row["rc"] == 0), sum(1 for row in rows if row["rc"] != 0)))
    for row in rows:
        if row["rc"] != 0:
            print("  FAIL  " + row["name"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
