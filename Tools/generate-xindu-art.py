#!/usr/bin/env python3
"""Generate concept art through the xindu paid image channel.

The channel is OpenAI-compatible image generation at https://xindu.xyz/v1 and
returns base64 PNGs. The API key is read at runtime from, in order:

  1. the XINDU_API_KEY environment variable
  2. the pi extension that registers the channel
     (~/.pi/agent/extensions/xindu-paid.ts)

It is never written into this repository, never echoed, and never logged.

Every call is appended to <out>/calls.jsonl with the prompt, model, size, elapsed
time and output path -- so a batch is reproducible and auditable without the key.

Usage:
  python Tools/generate-xindu-art.py --prompt "..." --out Tools/logs/art/x.png
  python Tools/generate-xindu-art.py --batch batch.json --max-calls 6
  python Tools/generate-xindu-art.py --list-models
  # edits (reference images, multipart; order matters: ref 1, ref 2 ...)
  python Tools/generate-xindu-art.py --prompt "..." --ref anchor.png --ref guide.png --out x.png

batch.json is a list of {"id": "...", "prompt": "...", "size": "1024x1024"}; an item
with "refs": ["a.png", "b.png"] goes to /images/edits instead of /images/generations.

Budget guards (every HTTP request counts, retries included -- each one may be billed):
  --max-calls N      cap for this invocation
  --budget-total N   cap across invocations: requests already logged in <out-dir>/calls.jsonl
                     count against it, so a batch split over several runs still stops at N.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

for _s in (sys.stdout, sys.stderr):
    _s.reconfigure(encoding="utf-8", errors="replace")

BASE_URL = "https://xindu.xyz/v1"
DEFAULT_MODEL = "gpt-image-2.5"
UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
)
EXTENSION = os.path.join(os.path.expanduser("~"), ".pi", "agent", "extensions", "xindu-paid.ts")


def load_key() -> str:
    key = os.environ.get("XINDU_API_KEY")
    if key:
        return key.strip()
    if os.path.exists(EXTENSION):
        with open(EXTENSION, encoding="utf-8") as fh:
            text = fh.read()
        match = re.search(r'"?XINDU_API_KEY"?\s*[:=]\s*\n?\s*"([^"]+)"', text)
        if match:
            return match.group(1)
    raise SystemExit(
        "no xindu key: set XINDU_API_KEY or keep the pi extension at %s" % EXTENSION
    )


def list_models(key: str) -> int:
    req = urllib.request.Request(
        BASE_URL + "/models",
        headers={"Authorization": "Bearer " + key, "User-Agent": UA},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        payload = json.loads(resp.read().decode("utf-8", "replace"))
    for item in payload.get("data", []):
        print(item.get("id"))
    return 0


def generate(key: str, model: str, prompt: str, size: str, timeout: int) -> tuple[bytes, str]:
    body = json.dumps(
        {"model": model, "prompt": prompt, "size": size, "n": 1}
    ).encode()
    req = urllib.request.Request(
        BASE_URL + "/images/generations",
        data=body,
        headers={
            "Authorization": "Bearer " + key,
            "Content-Type": "application/json",
            "User-Agent": UA,
        },
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        payload = json.loads(resp.read().decode("utf-8", "replace"))
    item = (payload.get("data") or [{}])[0]
    if item.get("b64_json"):
        return base64.b64decode(item["b64_json"]), (item.get("revised_prompt") or "")
    raise RuntimeError("no b64_json in response; keys=%s" % list(item))


def _md5(path: str) -> str:
    with open(path, "rb") as fh:
        return hashlib.md5(fh.read()).hexdigest()


def build_edit_body(model: str, prompt: str, size: str, refs: list[str]) -> tuple[bytes, str, list[dict]]:
    """multipart body for /images/edits. Field name `image` repeated once per reference, in order
    (same shape as Tools/generate-walk-rig-miaosf.py post_edit). Returns (body, boundary, parts)."""
    boundary = "----xinduEditBoundary" + hashlib.md5(prompt.encode("utf-8")).hexdigest()[:12]
    parts = []

    def field(name: str, value: str) -> bytes:
        parts.append({"field": name, "chars": len(value)})
        return (("--" + boundary + "\r\n").encode()
                + ('Content-Disposition: form-data; name="%s"\r\n\r\n' % name).encode()
                + value.encode("utf-8") + b"\r\n")

    body = field("model", model) + field("prompt", prompt) + field("size", size) + field("n", "1")
    for slot, path in enumerate(refs, start=1):
        with open(path, "rb") as fh:
            blob = fh.read()
        if not blob.startswith(b"\x89PNG"):
            raise SystemExit("reference %d is not a real PNG (convert it first): %s" % (slot, path))
        parts.append({"field": "image", "slot": slot, "file": os.path.basename(path),
                      "bytes": len(blob), "md5": hashlib.md5(blob).hexdigest()})
        body += (("--" + boundary + "\r\n").encode()
                 + ('Content-Disposition: form-data; name="image"; filename="reference-%d.png"\r\n' % slot).encode()
                 + b"Content-Type: image/png\r\n\r\n" + blob + b"\r\n")
    body += ("--" + boundary + "--\r\n").encode()
    return body, boundary, parts


def edit(key: str, model: str, prompt: str, size: str, refs: list[str], timeout: int) -> tuple[bytes, str]:
    body, boundary, _ = build_edit_body(model, prompt, size, refs)
    req = urllib.request.Request(
        BASE_URL + "/images/edits",
        data=body,
        headers={
            "Authorization": "Bearer " + key,
            "Content-Type": "multipart/form-data; boundary=" + boundary,
            "User-Agent": UA,
            "Accept": "application/json",
        },
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        payload = json.loads(resp.read().decode("utf-8", "replace"))
    item = (payload.get("data") or [{}])[0]
    if item.get("b64_json"):
        return base64.b64decode(item["b64_json"]), (item.get("revised_prompt") or "")
    raise RuntimeError("no b64_json in response; keys=%s" % list(item))


def logged_requests(out_dir: str) -> int:
    path = os.path.join(out_dir, "calls.jsonl")
    if not os.path.exists(path):
        return 0
    with open(path, encoding="utf-8") as fh:
        return sum(1 for line in fh if line.strip())


def log_call(out_dir: str, record: dict) -> None:
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "calls.jsonl"), "a", encoding="utf-8") as fh:
        fh.write(json.dumps(record, ensure_ascii=False) + "\n")


def run_one(key, model, spec, out_dir, timeout, dry) -> dict:
    target = spec.get("out") or os.path.join(out_dir, "%s.png" % spec["id"])
    refs = spec.get("refs") or []
    size = spec.get("size", "1024x1024")
    endpoint = "/images/edits" if refs else "/images/generations"
    if dry:
        print("[dry-run] %-28s POST %s%s  size=%s" % (spec["id"], BASE_URL, endpoint, size))
        print("          prompt (%d chars): %s" % (len(spec["prompt"]), spec["prompt"][:160]))
        if refs:
            body, boundary, parts = build_edit_body(model, spec["prompt"], size, refs)
            print("          multipart %d bytes, boundary=%s" % (len(body), boundary))
            for part in parts:
                print("          part %s" % json.dumps(part, ensure_ascii=False))
        print("          -> %s" % target)
        return {"id": spec["id"], "out": target, "dry_run": True}
    started = time.time()
    record = {
        "id": spec["id"],
        "model": model,
        "endpoint": endpoint,
        "size": size,
        "prompt": spec["prompt"],
        "refs": [{"file": os.path.relpath(p).replace("\\", "/"), "md5": _md5(p)} for p in refs],
        "out": os.path.relpath(target).replace("\\", "/"),
    }
    try:
        if refs:
            blob, revised = edit(key, model, spec["prompt"], size, refs, timeout)
        else:
            blob, revised = generate(key, model, spec["prompt"], size, timeout)
    except Exception as exc:
        # 失败的请求也可能计费，必须落账，--budget-total 才数得准。
        record.update({"status": "error", "error": str(exc)[:200],
                       "seconds": round(time.time() - started, 2)})
        log_call(out_dir, record)
        raise
    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    with open(target, "wb") as fh:
        fh.write(blob)
    record.update({
        "status": "ok",
        "revised_prompt": revised[:400],
        "seconds": round(time.time() - started, 2),
        "bytes": len(blob),
        "out_md5": hashlib.md5(blob).hexdigest(),
    })
    log_call(out_dir, record)
    print("  %-28s %5.1fs  %8d bytes  %s" % (spec["id"], record["seconds"], len(blob), target))
    return record


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--prompt")
    ap.add_argument("--out")
    ap.add_argument("--id")
    ap.add_argument("--batch")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--size", default="1024x1024")
    ap.add_argument("--out-dir", default="Tools/logs/xindu-art")
    ap.add_argument("--timeout", type=int, default=300)
    ap.add_argument("--max-calls", type=int, default=20,
                    help="hard cap on HTTP requests in this invocation (retries count)")
    ap.add_argument("--budget-total", type=int, default=None,
                    help="hard cap on HTTP requests across runs, counted from <out-dir>/calls.jsonl")
    ap.add_argument("--ref", action="append", default=None,
                    help="reference image for /images/edits; repeat for ref 1, ref 2 ... (order matters)")
    ap.add_argument("--retries", type=int, default=0,
                    help="automatic retries on 429/5xx (default 0: a retry spends budget, decide by hand)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--list-models", action="store_true")
    args = ap.parse_args()

    specs = []
    if args.batch:
        with open(args.batch, encoding="utf-8") as fh:
            specs = json.load(fh)
    elif args.prompt:
        specs = [{"id": args.id or "art", "prompt": args.prompt, "out": args.out,
                  "size": args.size, "refs": args.ref or []}]
    elif not args.list_models:
        ap.error("need --prompt or --batch")
    for spec in specs:
        for path in spec.get("refs") or []:
            if not os.path.isfile(path):
                ap.error("reference not found: %s" % path)

    # dry-run 不读 key、不发请求：只验请求怎么组装。
    key = "" if args.dry_run else load_key()
    if args.list_models:
        return list_models(key or load_key())

    if len(specs) > args.max_calls:
        print("batch has %d items > --max-calls %d; raise it explicitly if intended"
              % (len(specs), args.max_calls))
        return 2
    already = logged_requests(args.out_dir)
    if args.budget_total is not None and not args.dry_run:
        if already + len(specs) > args.budget_total:
            print("budget: %d requests already logged in %s + %d new > --budget-total %d; stop"
                  % (already, os.path.join(args.out_dir, "calls.jsonl"), len(specs), args.budget_total))
            return 2

    print("xindu: %s  model=%s  items=%d  logged-before=%d" % (BASE_URL, args.model, len(specs), already))
    done = 0
    sent = 0
    for spec in specs:
        for attempt in range(1 + max(0, args.retries)):
            if not args.dry_run:
                if sent >= args.max_calls or (
                        args.budget_total is not None and already + sent >= args.budget_total):
                    print("  %-28s budget reached (sent %d this run); stop" % (spec["id"], sent))
                    print("done: %d/%d" % (done, len(specs)))
                    return 0 if done else 1
                sent += 1
            try:
                run_one(key, args.model, spec, args.out_dir, args.timeout, args.dry_run)
                done += 1
                break
            except urllib.error.HTTPError as exc:
                text = exc.read().decode("utf-8", "replace")[:200]
                print("  %-28s HTTP %d %s" % (spec["id"], exc.code, text))
                if exc.code in (429, 500, 502, 503, 524) and attempt < args.retries:
                    time.sleep(10 * (attempt + 1))
                    continue
                break
            except Exception as exc:  # noqa: BLE001 - keep the batch going
                print("  %-28s ERR %s" % (spec["id"], str(exc)[:160]))
                break
        if not args.dry_run:
            time.sleep(2)
    print("done: %d/%d" % (done, len(specs)))
    return 0 if done else 1


if __name__ == "__main__":
    sys.exit(main())
