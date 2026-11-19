#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""员工 B 用的生图网关连通性探针（只读探测，不出图、不写产物）。

网关：用户自有的本地 grok2api，OpenAI 兼容；地址 http://127.0.0.1:8000，
key 明文放在 D:\\tmp\\ddyu-art-key.txt（**不入库、不写进任何文档**）。
请求必须绕过系统代理，否则会被本机代理吞掉。

用法：
    python Tools/grok-ping.py            # 只探连通 + 列模型
"""
import json
import sys
import urllib.error
import urllib.request

# GBK 控制台下 print 中文会 UnicodeEncodeError 崩在结论之前（全仓统一守卫）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

BASE = "http://127.0.0.1:8000"
KEY_PATH = r"D:\tmp\ddyu-art-key.txt"


def opener():
    # 绕过系统代理（本机有代理时会把 127.0.0.1 的请求也拦下来）
    return urllib.request.build_opener(urllib.request.ProxyHandler({}))


def main():
    try:
        with open(KEY_PATH, "r", encoding="utf-8") as f:
            key = f.read().strip()
    except OSError as e:
        print("KEY-READ-FAIL: %s" % e)
        return 1
    if not key:
        print("KEY-EMPTY: %s" % KEY_PATH)
        return 1

    req = urllib.request.Request(
        BASE + "/v1/models",
        headers={"Authorization": "Bearer " + key},
    )
    try:
        body = opener().open(req, timeout=10).read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        print("HTTP-ERROR: %s" % e.code)
        print(e.read().decode("utf-8", "replace")[:400])
        return 1
    except OSError as e:
        print("CONNECT-FAIL: %s  （网关没起？）" % e)
        return 1

    try:
        data = json.loads(body)
    except ValueError:
        print("BAD-JSON: %s" % body[:400])
        return 1

    ids = []
    for it in data.get("data", []) if isinstance(data, dict) else []:
        mid = it.get("id") or it.get("name")
        if mid:
            ids.append(mid)
    print("GATEWAY-OK %s" % BASE)
    print("MODELS(%d): %s" % (len(ids), ", ".join(ids)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
