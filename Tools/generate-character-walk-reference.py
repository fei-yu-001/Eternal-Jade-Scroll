# -*- coding: utf-8 -*-
"""带参考图的行走序列帧生成（V-03，2026-10-06 架构师）。

为什么重做：V-01/V-02 用**纯文字**描述角色生成，结果与主角形象不一致
（用户 2026-10-06 指出）。identity gate 实测 palette 交集只有 0.29~0.32，
而同一角色跨视图是 0.62~0.67 —— 确实是"换了个人"。

本脚本改用网关的 **/v1/images/edits**（模型 grok-imagine-image-edit），
把**现有立绘本体**以 data URI 喂进去当参考图，要求"同一个角色"再排成走序列。
data URI 形式是本仓网关已支持并测过的（service_test.go 里 ImageURLs 就是 data:image/png;base64,...）。

用法：
  python Tools/generate-character-walk-reference.py [characterId] [gender] [style] [view]
  默认 farmer male 0 front
"""
import base64
import io
import json
import os
import sys
import urllib.request
from urllib.request import ProxyHandler, build_opener

from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

BASE = "http://127.0.0.1:8000"
KEY_FILE = os.path.join(os.path.sep, "tmp", "ddyu-art-key.txt")
RES = r"D:\qiqi_me\Eternal Jade Scroll\GameClient\TwelveJade\Assets\Resources\Art"
RAW_DIR = r"D:\qiqi_me\Eternal Jade Scroll\GameClient\TwelveJade\Assets\ArtSource\originals\characters-rig"
PROMPT_DIR = r"D:\qiqi_me\Eternal Jade Scroll\GameClient\TwelveJade\Assets\ArtSource\prompts\character-rig"

BACKGROUND = (236, 228, 210)

PROMPT = (
    "Keep this SAME character exactly as in the reference image: identical face, identical hairstyle, "
    "identical clothing, identical fabric colours, identical palette and identical body proportions. "
    "Do not redesign, do not restyle, do not change the colours. "
    "Now draw this same character as a 2D game walk-cycle sprite sheet: exactly EIGHT frames arranged "
    "in ONE single horizontal row, evenly spaced, equal cell width, the same character in every frame at "
    "identical size and identical scale, fully inside its own cell, in the same straight front view, "
    "walking on the spot, with the feet of every frame resting on the SAME horizontal ground line. "
    "The eight frames form one complete walk cycle: contact, down, pass, up, then contact, down, pass, up "
    "with the opposite leg leading. Knees bend visibly, arms swing naturally opposite to the legs, the "
    "body bobs and shifts weight. Hand-painted fine ink linework with soft watercolour washes, "
    "muted natural colours, elegant historical Chinese indie game art, late Yuan dynasty. "
    "Leave a clear empty gap between adjacent figures. No grid lines, no dividing borders, no frame "
    "numbers, no text, no labels, no logo, no watermark. Uniform flat warm ivory background across the "
    "whole strip, no scene, no ground surface, no cast shadow outside the figures."
)


def read_key():
    value = open(KEY_FILE, encoding="utf-8").read().strip()
    if len(value) < 10:
        raise SystemExit("gateway key looks empty")
    return value


def reference_data_uri(path):
    im = Image.open(path).convert("RGBA")
    flat = Image.new("RGB", im.size, BACKGROUND)
    flat.paste(im, (0, 0), im)
    buf = io.BytesIO()
    flat.save(buf, format="PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii"), flat.size


def main():
    cid = sys.argv[1] if len(sys.argv) > 1 else "farmer"
    gender = sys.argv[2] if len(sys.argv) > 2 else "male"
    style = sys.argv[3] if len(sys.argv) > 3 else "0"
    view = sys.argv[4] if len(sys.argv) > 4 else "front"
    ref = os.path.join(RES, "%s-%s-%s-%s.png" % (cid, gender, style, view))
    if not os.path.exists(ref):
        raise SystemExit("reference not found: " + ref)
    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)

    data_uri, size = reference_data_uri(ref)
    print("reference %s  %dx%d  data-uri %d KB" % (os.path.basename(ref), size[0], size[1],
                                                   len(data_uri) // 1024))
    payload = {
        "model": "grok-imagine-image-edit",
        "prompt": PROMPT,
        "image": {"url": data_uri},
        "n": 1,
        "aspect_ratio": "16:9",
        "resolution": "1k",
        "response_format": "b64_json",
    }
    opener = build_opener(ProxyHandler({}))
    request = urllib.request.Request(
        BASE + "/v1/images/edits",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + read_key(), "Content-Type": "application/json"},
    )
    result = json.loads(opener.open(request, timeout=300).read())
    data = result.get("data") or []
    if not data:
        raise SystemExit("no image returned: " + json.dumps(result)[:400])
    first = data[0]
    if first.get("b64_json"):
        blob = base64.b64decode(first["b64_json"])
    elif first.get("url"):
        blob = opener.open(first["url"], timeout=180).read()
    else:
        raise SystemExit("unrecognised payload: " + json.dumps(first)[:300])

    slug = "%s-%s-%s" % (cid, gender, style)
    out = os.path.join(RAW_DIR, "%s-walksheet-ref-%s-v1.png" % (slug, view))
    with open(out, "wb") as fh:
        fh.write(blob)
    with open(os.path.join(PROMPT_DIR, "%s-walksheet-ref-%s-v1.txt" % (slug, view)), "w",
              encoding="utf-8") as fh:
        fh.write(PROMPT + os.linesep)
    im = Image.open(out)
    print("saved %s  %dx%d  %d KB" % (out, im.size[0], im.size[1], len(blob) // 1024))


if __name__ == "__main__":
    main()
