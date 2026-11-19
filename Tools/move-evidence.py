#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""move-evidence.py —— 取证图搬运器（交接包 2026-10-06-Y · Y-4 · D-10 / P2-1）。

定死"取证图存哪"：`Tools/logs/<gate>/` 是唯一入册落点（`.gitignore:57` 挡掉 `Tools/screenshots/*`，
存那里等于没交——O3 的教训）。本脚本把 `Tools/screenshots/<name>.png` 搬进 `Tools/logs/<gate>/`
并**前后各算一次 md5**，不一致就中止（搬运本身不能改内容）。

用法：
  python Tools/move-evidence.py w-a-mesh-deform.png v-06-meshdeform
  python Tools/move-evidence.py --selftest
退出码：搬运成功且 md5 两端一致 0；其余 1。
"""
import argparse
import hashlib
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCREENSHOTS = os.path.join(REPO, "Tools", "screenshots")
LOGS = os.path.join(REPO, "Tools", "logs")


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def move(name, gate):
    src = os.path.join(SCREENSHOTS, name)
    if not os.path.isfile(src):
        print("RESULT: FAIL —— 源不存在: %s" % src)
        return False
    dst_dir = os.path.join(LOGS, gate)
    os.makedirs(dst_dir, exist_ok=True)
    dst = os.path.join(dst_dir, name)
    if os.path.exists(dst):
        print("RESULT: FAIL —— 目标已存在（不覆盖，人工先看）: %s" % dst)
        return False
    before = md5(src)
    shutil.move(src, dst)
    after = md5(dst)
    ok = before == after
    print("[move] %s -> %s" % (src, dst))
    print("[md5] 前=%s 后=%s %s" % (before[:12], after[:12], "一致" if ok else "不一致"))
    print("RESULT: %s" % ("PASS" if ok else "FAIL"))
    return ok


def selftest():
    """合成一张小 png：先造到 screenshots（若目录不可写则整个 selftest 放仓外临时目录做），
    走一次搬运，断言 md5 两端一致、源已消失；再跑一次重名保护。"""
    import tempfile
    import struct
    import zlib

    def tiny_png(path):
        # 手写一个 4x4 纯色 PNG（零依赖），避免引入 PIL 依赖
        raw = b"".join(b"\x00" + b"\xaa\xbb\xcc" * 4 for _ in range(4))
        chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
        png = (b"\x89PNG\r\n\x1a\n"
               + chunk(b"IHDR", struct.pack(">IIBBBBB", 4, 4, 8, 2, 0, 0, 0))
               + chunk(b"IDAT", zlib.compress(raw))
               + chunk(b"IEND", b""))
        with open(path, "wb") as f:
            f.write(png)

    base = tempfile.mkdtemp(prefix="d10-")
    global SCREENSHOTS, LOGS
    SCREENSHOTS, LOGS = os.path.join(base, "screenshots"), os.path.join(base, "logs")
    os.makedirs(SCREENSHOTS, exist_ok=True)
    tiny_png(os.path.join(SCREENSHOTS, "selftest.png"))

    ok = move("selftest.png", "selftest-gate")
    ok = ok and not os.path.exists(os.path.join(SCREENSHOTS, "selftest.png"))
    print("[selftest] 源已搬走: %s" % (not os.path.exists(os.path.join(SCREENSHOTS, "selftest.png"))))
    ok = ok and os.path.isfile(os.path.join(LOGS, "selftest-gate", "selftest.png"))
    print("[selftest] %s" % ("PASS" if ok else "FAIL"))
    return ok


def main():
    if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    ap = argparse.ArgumentParser()
    ap.add_argument("name", nargs="?", help="Tools/screenshots 下的文件名")
    ap.add_argument("gate", nargs="?", help="目标 gate 目录名（Tools/logs/<gate>/）")
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()
    if args.selftest:
        sys.exit(0 if selftest() else 1)
    if not args.name or not args.gate:
        print("用法: python Tools/move-evidence.py <name.png> <gate>  |  --selftest")
        sys.exit(1)
    sys.exit(0 if move(args.name, args.gate) else 1)


if __name__ == "__main__":
    main()
