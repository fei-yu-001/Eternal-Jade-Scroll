# -*- coding: utf-8 -*-
"""按"实测配色"生成走序列帧（V-04 过渡方案，2026-10-06 架构师）。

为什么：/v1/images/edits 需要一张上游能抓到的公网图，而本机网关 publicApiBaseURL 为空
（实测 https 图返回 400「下载地址返回 404」、data URI 返回 502）⇒ 参考图路线暂时不通。
过渡办法：把**现有立绘实测出来的分区配色与比例**写进提示词，再出序列帧，
最后仍要过 check-character-identity.py 的 palette 交集判据 —— **不许用"我觉得像"过关**。

用法：python Tools/generate-character-walk-palette.py [cid] [gender] [style] [view]
"""
import base64
import json
import os
import sys
import urllib.request
from collections import Counter
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


def region_palette(im, y0f, y1f, top=4):
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    box = im.split()[3].getbbox()
    if not box:
        return []
    bx0, by0, bx1, by1 = box
    bh = by1 - by0
    counters = Counter()
    for y in range(by0 + int(bh * y0f), by0 + int(bh * y1f)):
        for x in range(bx0, bx1):
            r, g, b, a = px[x, y]
            if a > 128:
                counters[(r // 16, g // 16, b // 16)] += 1
    total = float(sum(counters.values())) or 1.0
    return [("#%02X%02X%02X" % (k[0] * 16 + 8, k[1] * 16 + 8, k[2] * 16 + 8), v / total)
            for k, v in counters.most_common(top)]


def main():
    cid = sys.argv[1] if len(sys.argv) > 1 else "farmer"
    gender = sys.argv[2] if len(sys.argv) > 2 else "male"
    style = sys.argv[3] if len(sys.argv) > 3 else "0"
    view = sys.argv[4] if len(sys.argv) > 4 else "front"
    ref = os.path.join(RES, "%s-%s-%s-%s.png" % (cid, gender, style, view))
    im = Image.open(ref).convert("RGBA")
    box = im.split()[3].getbbox()
    bw = box[2] - box[0]
    bh = box[3] - box[1]
    aspect = bh / float(bw)
    head = region_palette(im, 0.0, 0.20)
    upper = region_palette(im, 0.20, 0.58)
    lower = region_palette(im, 0.58, 1.0)
    print("reference %s  bbox=%dx%d  h/w=%.2f" % (os.path.basename(ref), bw, bh, aspect))
    for label, pal in (("head", head), ("upper", upper), ("lower", lower)):
        print("  %-5s %s" % (label, ", ".join("%s %.0f%%" % (c, 100 * p) for c, p in pal)))

    def fmt(pal, n=3):
        return ", ".join(c for c, _ in pal[:n])

    subject = (
        "Character: the SAME existing game hero, a %s of a riverside town in the late Yuan dynasty. "
        "Head: %s (hair and headwear). Upper body: a plain undyed linen short jacket over an inner robe, "
        "dominant colours %s. Lower body: wide trousers and footwear, dominant colours %s. "
        "Body proportions: total height divided by shoulder width is about %.2f, head is about %.0f%% of "
        "total height. Keep these exact colours and proportions in every frame."
    ) % ("male" if gender == "male" else "female", fmt(head), fmt(upper), fmt(lower),
         aspect, 100.0 * 0.20)
    print("subject prompt: " + subject)

    prompt = (
        "A 2D game character walk-cycle sprite sheet, one single image. Hand-painted fine ink linework "
        "with soft watercolour washes on warm ivory paper, muted natural colours, elegant historical "
        "Chinese indie game art, late Yuan / early Ming dynasty. " + subject +
        " Layout: exactly EIGHT frames arranged in ONE single horizontal row, evenly spaced, equal cell "
        "width, the same character in every frame, identical size and identical scale, fully inside its "
        "own cell, in the same straight front view, walking on the spot; the feet of every frame rest on "
        "the SAME horizontal ground line. The eight frames together form one complete walk cycle: "
        "contact, down, pass, up, then contact, down, pass, up with the opposite leg leading. Arms swing "
        "naturally opposite to the legs, knees bend visibly, weight shifts. Leave a clear empty gap "
        "between adjacent figures. No grid lines, no dividing borders, no frame numbers, no text, no "
        "labels, no logo, no watermark. Uniform flat warm ivory background across the whole strip, no "
        "scene, no ground surface, no cast shadow outside the figures."
    )

    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)
    payload = {"model": "grok-imagine-image", "prompt": prompt, "n": 1,
               "aspect_ratio": "16:9", "response_format": "b64_json"}
    key = open(KEY_FILE, encoding="utf-8").read().strip()
    opener = build_opener(ProxyHandler({}))
    request = urllib.request.Request(
        BASE + "/v1/images/generations",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    result = json.loads(opener.open(request, timeout=300).read())
    blob = base64.b64decode(result["data"][0]["b64_json"])
    slug = "%s-%s-%s" % (cid, gender, style)
    out = os.path.join(RAW_DIR, "%s-walksheet-pal-%s-v1.png" % (slug, view))
    with open(out, "wb") as fh:
        fh.write(blob)
    with open(os.path.join(PROMPT_DIR, "%s-walksheet-pal-%s-v1.txt" % (slug, view)), "w",
              encoding="utf-8") as fh:
        fh.write(prompt + os.linesep)
    im2 = Image.open(out)
    print("saved %s  %dx%d  %d KB" % (out, im2.size[0], im2.size[1], len(blob) // 1024))


if __name__ == "__main__":
    main()
