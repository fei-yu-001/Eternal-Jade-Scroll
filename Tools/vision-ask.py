#!/usr/bin/env python3
"""Ask a vision model about one or more images.

Why this exists: the editor's built-in vision tool caches whichever model was
configured when the session started, so switching vision.json mid-session has no
effect until restart. This script talks to the provider directly instead, so any
agent (or the architect) can look at art without restarting anything.

The API key is read from ~/.pi/agent/models.json at runtime and never printed.

Usage:
  python Tools/vision-ask.py <image> [<image> ...] --prompt "what is this?"
  python Tools/vision-ask.py a.png b.png --prompt "compare" --model minimax-m3
"""
from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import sys
import urllib.error
import urllib.request

for _s in (sys.stdout, sys.stderr):
    _s.reconfigure(encoding="utf-8", errors="replace")

CONFIG = os.path.join(os.path.expanduser("~"), ".pi", "agent", "models.json")
UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
)
# Vision-capable models seen in the 1024 group.
# 2026-10-24 换装 kunyou(虾蹬王) 网关后的实测名单（sweep REAL 且声明 image input）：
VISION_MODELS = (
    "deepseek-v4-flash-vision-exp",
    "MiniMax-M3",
    "qwen3.8-27b",
)


def load_provider(provider: str):
    with open(CONFIG, encoding="utf-8") as fh:
        cfg = json.load(fh)["providers"][provider]
    return cfg["baseUrl"].rstrip("/"), cfg["apiKey"]


def as_data_uri(path: str) -> str:
    mime = mimetypes.guess_type(path)[0] or "image/png"
    with open(path, "rb") as fh:
        blob = base64.b64encode(fh.read()).decode("ascii")
    return "data:%s;base64,%s" % (mime, blob)


def ask(provider: str, model: str, prompt: str, images, timeout: int) -> str:
    base, key = load_provider(provider)
    content = [{"type": "text", "text": prompt}]
    for path in images:
        content.append({"type": "image_url", "image_url": {"url": as_data_uri(path)}})
    body = json.dumps(
        {"model": model, "messages": [{"role": "user", "content": content}], "max_tokens": 1600}
    ).encode()
    req = urllib.request.Request(
        base + "/chat/completions",
        data=body,
        headers={
            "Authorization": "Bearer " + key,
            "Content-Type": "application/json",
            "User-Agent": UA,
        },
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        payload = json.loads(resp.read().decode("utf-8", "replace"))
    message = (payload.get("choices") or [{}])[0].get("message") or {}
    text = message.get("content") or ""
    if not text.strip():
        raise RuntimeError("empty reply (usage=%s)" % (payload.get("usage"),))
    return text


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("images", nargs="+")
    ap.add_argument("--prompt", required=True)
    ap.add_argument("--provider", default="1024")
    ap.add_argument("--model", default="deepseek-v4-flash-vision")
    ap.add_argument("--timeout", type=int, default=180)
    args = ap.parse_args()

    missing = [p for p in args.images if not os.path.exists(p)]
    if missing:
        for path in missing:
            print("missing file: %s" % path)
        return 2
    for path in args.images:
        print("image: %s (%d bytes)" % (path, os.path.getsize(path)))
    print("-" * 72)

    candidates = [args.model] + [m for m in VISION_MODELS if m != args.model]
    last = ""
    for model in candidates:
        try:
            print(ask(args.provider, model, args.prompt, args.images, args.timeout))
            return 0
        except urllib.error.HTTPError as exc:
            last = "HTTP %d %s" % (exc.code, exc.read()[:160].decode("utf-8", "replace"))
        except Exception as exc:  # noqa: BLE001 - probing, keep falling back
            last = str(exc)[:200]
        print("  [%s failed: %s]" % (model, last))
    print("all vision candidates failed; last error: %s" % last)
    return 1


if __name__ == "__main__":
    sys.exit(main())
