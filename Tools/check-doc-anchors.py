#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-doc-anchors.py —— 文档卫生检查器（交接包 2026-10-06-T · S-03 交付）。

扫描 Documents/**/*.md，报告两类问题：

1. 死引用：引用了不存在的 markdown 文件。两种形态：
   a) 下划线/裸文件名引用，如 ``21_架构决策记录.md``、``外包任务书-W4.md``；
   b) 带目录前缀的引用，如 ``Documents/30_AI项目总入口.md``。
   判定按"文件名 basename 在 Documents 树内是否存在"，存在即放过（含 Documents/raw/ 子目录）。

2. 行号锚点：``:9190``、``:9190-9750``、``行 123`` 这类形态。
   行号会随文档追加漂移，锚点必然腐烂（夜线每班都要重新 grep 就是因为这个）。
   轻排除：冒号前紧邻 ``/`` 的（URL 的 host:port，如 localhost:18080）不算锚点。

退出码：扫描有命中 = 1；干净 = 0。
--selftest：把内置的假文档（故意带死引用 + 行号锚点 + 一个应被排除的端口）喂给同一套检测器，
两类必须都命中、端口必须被排除，否则自证失败退出 1。样本必须现造，不写死外部路径。

用法：
    python Tools/check-doc-anchors.py                 # 全量扫描 Documents/
    python Tools/check-doc-anchors.py --selftest      # 自证（不扫 Documents/）
"""
import re
import sys
import tempfile
import os
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DOC_ROOT = REPO / "Documents"

# 下划线/裸文件名引用：抓 *.md 字样（宽抓，靠存在性收口，宁可漏报不误伤）
RE_BARE_MD = re.compile(r"[0-9A-Za-z\u4e00-\u9fff][0-9A-Za-z\-_\u4e00-\u9fff]*\.md")
# 带目录前缀的引用：Documents/xxx（到空白/右括号/竖线/引号/反引号/中文标点为止）
# 2026-10-16 修：全角括号与全角竖线原来没进排除集，把「Documents/09_开发日志.md（文末一条）」
# 整体吃成路径，造成对已存在文件的死引用误报；与注释声明的意图不符。
RE_DOC_PATH = re.compile(r"Documents/[^\s\)\|\>`\"'，。；：、（）【】「」《》｜]+")
# 行号锚点：:123 或 :123-456（3~5 位；URL 端口与裸 IP:端口 由 _is_url_or_ip 排除）
RE_LINE_ANCHOR = re.compile(r":\d{3,5}(?:-\d{3,5})?")
# 中文"行 N"
RE_HANG = re.compile(r"行 \d{2,5}")


def _is_url_or_ip(text, start):
    """冒号前 40 字符内是 //host 结尾或裸 IP 结尾 ⇒ 这个冒号数字是端口，不是行号。"""
    head = text[max(0, start - 40):start]
    if re.search(r"//[^\s/]*$", head):
        return True
    if re.search(r"(?:\d{1,3}\.){3}\d{1,3}$", head):
        return True
    return False


SCAN_SKIP_DIRS = {".git", ".batch", "Library", "obj", "bin", "node_modules", "Temp", "Logs"}


def md_index():
    """全仓 md 索引（os.walk 剪枝，不进产物目录）：basename 集合 + 仓库相对路径集合。

    存在性按全仓判定——引用 `Tools/门禁健壮性台账.md` 这类仓库内真实文件不算死引用，
    死引用的定义是"引用的文件在任何地方都不存在"（任务书口径）。
    """
    basenames, relpaths = set(), set()
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in SCAN_SKIP_DIRS]
        for fn in files:
            if fn.endswith(".md"):
                p = Path(root) / fn
                basenames.add(fn)
                relpaths.add(p.relative_to(REPO).as_posix())
    return basenames, relpaths


def scan_text(text, basenames, relpaths):
    """对一段文本跑两类检测，返回 [(kind, matched)]。索引由调用方算好传入（每行重建一次全仓 walk 是灾难）。"""
    hits = []

    for m in RE_DOC_PATH.finditer(text):
        rel = m.group(0)
        # 2026-10-16 修：路径后面紧跟 :行号 / :起-止 是行号引用，不是路径的一部分。
        # 原实现把整个「Documents/09_开发日志.md:4029-4234」当路径查存在性，于是对已存在文件报死引用。
        rel = re.sub(r":\d{1,6}(?:-\d{1,6})?$", "", rel)
        if not rel:
            continue
        if rel in relpaths or (REPO / rel).is_file():
            continue
        if rel.endswith("/") or "**" in rel or "<" in rel:
            continue  # 目录引用 / 通配符 / 模板占位符（如 架构师B-复核-<日期>），不是死文件引用
        if "*" in rel or "?" in rel or "…" in rel or "..." in rel:
            continue  # 单个通配符与省略号同理：模式引用不是文件引用（本脚本原则：宁可漏报不误伤）
        hits.append(("dead-ref", rel))
    for m in RE_BARE_MD.finditer(text):
        name = m.group(0)
        if name in basenames:
            continue
        hits.append(("dead-ref", name))
    for m in RE_LINE_ANCHOR.finditer(text):
        if _is_url_or_ip(text, m.start()):
            continue
        hits.append(("line-anchor", m.group(0)))
    for m in RE_HANG.finditer(text):
        hits.append(("line-anchor", m.group(0)))
    return hits


def scan_files():
    """全量扫描 Documents/**/*.md，返回 [(file, lineno, kind, matched, line_text)]。"""
    findings = []
    if not DOC_ROOT.is_dir():
        return findings
    basenames, relpaths = md_index()  # 全仓索引只算一次
    for p in sorted(DOC_ROOT.rglob("*.md")):
        try:
            text = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            text = p.read_text(encoding="utf-8", errors="replace")
        for lineno, line in enumerate(text.splitlines(), 1):
            for kind, matched in scan_text(line, basenames, relpaths):
                findings.append((p.relative_to(REPO).as_posix(), lineno, kind, matched, line.strip()[:80]))
    return findings


def selftest():
    """内置假文档必须两类都命中、端口必须被排除。"""
    sample = "\n".join([
        "先读 Documents/21_架构决策记录.md 与 23_并行会话规则.md。",
        "任务书见架构文档 :9190-9750，另见 :11292。",
        "答案在第 12 行附近，写于 行 345。",
        "网关地址 http://127.0.0.1:8000 的端口不算锚点。",
        "这份 20_项目状态板.md 是真实存在的引用（不测死引用，只测不误报存在文件）。",
    ])
    basenames, relpaths = md_index()
    hits = scan_text(sample, basenames, relpaths)
    kinds = {k for k, _ in hits}
    matched = {v for _, v in hits}

    checks = [
        ("死引用命中", "Documents/21_架构决策记录.md" in matched or "21_架构决策记录.md" in matched),
        ("死引用命中 23_并行会话规则.md", "23_并行会话规则.md" in matched),
        ("行号锚点命中 :9190-9750", ":9190-9750" in matched),
        ("行号锚点命中 :11292", ":11292" in matched),
        ("行号锚点命中 行 345", "行 345" in matched),
        ("端口 :8000 被排除", ":8000" not in matched),
        ("真实文件 20_项目状态板.md 未误报", "20_项目状态板.md" not in matched),
    ]
    ok = True
    for name, passed in checks:
        print(("SELFTEST: PASS " if passed else "SELFTEST: FAIL ") + name)
        ok = ok and passed
    # 2026-10-16 修：原为 "[selftest] PASS …"，不匹配汇总器的 `grep -E 'RESULT:|SELFTEST:'` 口径，
    # 一旦接入 ALL_LABELS 会被判成「跑了却没出判据字样」。按 B 席 2026-10-06 01:59 通用口径
    # （任何 --selftest* 代码路径一律打 SELFTEST:，永不打 RESULT:）改标题字，判定与阈值未动。
    covered = {"dead-ref", "line-anchor"} <= kinds
    print("SELFTEST: " + ("PASS" if covered else "FAIL")
          + " 两类检测覆盖：" + ("是" if covered else "否"))
    return ok and covered


def main():
    if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass

    if "--selftest" in sys.argv[1:]:
        sys.exit(0 if selftest() else 1)

    findings = scan_files()
    if not findings:
        print("RESULT: PASS —— Documents/ 下未发现死引用或行号锚点")
        sys.exit(0)

    by_file = {}
    for f, lineno, kind, matched, line in findings:
        by_file.setdefault(f, []).append((lineno, kind, matched, line))

    total_dead = sum(1 for x in findings if x[2] == "dead-ref")
    total_line = sum(1 for x in findings if x[2] == "line-anchor")
    print(f"RESULT: FAIL —— 死引用 {total_dead} 处 / 行号锚点 {total_line} 处（按文件分组如下）")
    for f in sorted(by_file):
        items = by_file[f]
        print(f"\n## {f}（{len(items)} 处）")
        for lineno, kind, matched, line in items:
            tag = "死引用" if kind == "dead-ref" else "行号锚点"
            print(f"  行 {lineno} [{tag}] {matched} ｜ 原文：{line}")
    sys.exit(1)


if __name__ == "__main__":
    main()
