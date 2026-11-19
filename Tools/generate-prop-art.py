# 一次性素材脚本：经本机 grok2api 网关生成青石镇道具立绘（9 件，1:1，宣纸底待抠）。
# 密钥流程同 generate-fate-art.py：仅存内存，不落盘、不回显（见 Assets/ArtSource/PROVENANCE.md）。
import base64
import io
import json
import os
import sqlite3
import sys
import urllib.request

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# 健壮性（夜1 G-3，2026-10-06）：zh-CN Windows 的 GBK 控制台下 print 中文/箭头会
# UnicodeEncodeError 崩在出结论之前——**会崩的脚本比会红的脚本危险得多**。
# 故统一强制 stdout/stderr 为 UTF-8；先例见 SaveChecks.csproj 的 <CodePage>65001</CodePage>。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

BASE = "http://127.0.0.1:8000"
DB_PATH = r"D:\work\amaze\DDyu\data\backend.db"
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
import world_paths as wp
ORIGINAL_DIR = os.path.join(wp.art_source_root(), 'originals', 'props')
PROMPT_DIR = os.path.join(wp.art_source_root(), 'prompts')

HEADER = (
    "Asset type: a single hand-painted game prop illustration, fine ink linework with soft "
    "watercolor washes on warm ivory paper, muted natural colors, consistent with an elegant "
    "hand-painted historical Chinese indie game set in the late Yuan / early Ming era. "
)
FOOTER = (
    " The structure stands fully visible, front three-quarter view, centered, complete silhouette "
    "with nothing cropped, flat uniform warm ivory background #EDE6D6, no scene, no ground shadow. "
    "Soft diffuse light. No text, labels, logo, watermark, no modern elements."
)

PROPS = [
    ("paifang", "牌坊",
     "Subject: a tall ceremonial stone-and-timber paifang archway with three openings, green-grey "
     "tiled roofs with upturned eaves, painted bracket sets, stone plinths at the bases."),
    ("market-stall", "市集货摊",
     "Subject: a street market stall with a draped blue-grey cloth awning on bamboo poles, wooden "
     "counter stacked with vegetables, a rice sack and a clay teapot."),
    ("lantern-pole", "灯笼杆",
     "Subject: a tall wooden lantern pole with two red paper lanterns hanging from a crossbar, "
     "tassels, a carved stone base."),
    ("shop-front", "街边商铺",
     "Subject: a single-storey street shop, grey tiled roof with upturned eaves, open wooden plank "
     "counter front in the traditional Chinese storefront style, a cloth wine banner hanging from a "
     "pole, wine jars by the door."),
    ("residence", "民居小院",
     "Subject: a small courtyard residence house, grey tiled gable roof with upturned eaves, white "
     "plastered walls with dark timber framing, wooden lattice windows, double wooden doors."),
    ("temple-gate", "寺庙山门",
     "Subject: a temple mountain gate shanmen, red plastered walls, dark grey tiled roof with "
     "upturned eaves, studded wooden doors, a small bronze incense burner at the steps."),
    ("willow", "老柳树",
     "Subject: a large old willow tree, drooping branches with slender leaves, gnarled weathered "
     "trunk with a small hollow."),
    ("bamboo", "竹丛",
     "Subject: a dense bamboo cluster, slender green stalks with fine leaves, a few weathered rocks "
     "at the roots."),
    ("stone-lion", "石狮",
     "Subject: a single carved stone guardian lion on a plinth, weathered mossy, playful fierce pose."),
]


def post(path, payload, token=None):
    request = urllib.request.Request(
        BASE + path,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    if token:
        request.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.loads(response.read())


def load_client_key():
    connection = sqlite3.connect(DB_PATH)
    try:
        rows = connection.execute(
            "SELECT prefix, encrypted_secret FROM client_keys WHERE enabled = 1").fetchall()
    finally:
        connection.close()
    aead = AESGCM(base64.b64decode("7y9Gd47RnIqvroBhbc0X1d6cHDK3XrzsOtA+L9LMNfs="))
    for prefix, sealed in rows:
        try:
            data = base64.b64decode(sealed + "=" * (-len(sealed) % 4))
            return aead.decrypt(data[:12], data[12:], None).decode("utf-8")
        except Exception:
            continue
    raise SystemExit("no usable client key found in gateway database")


def ensure_true_png(raw):
    """网关可能返回 JPEG 字节：原样写进 .png 就是假扩展名（架构文档 §18.3）。
    后续抠图与四边检查都按 PNG 读，假 PNG 会让"检查通过"变成检查了个错误格式。"""
    if raw[:8] == b"\x89PNG\r\n\x1a\n":
        return raw
    from PIL import Image
    image = Image.open(io.BytesIO(raw)).convert("RGB")
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    print("  转码：%s -> 真 PNG" % Image.open(io.BytesIO(raw)).format)
    return buffer.getvalue()


def main():
    import argparse
    parser = argparse.ArgumentParser(description="生成道具立绘（默认全部，可用 --only 指定）")
    parser.add_argument("--only", nargs="*", default=None,
                        help="只生成指定 slug，例如 --only shop-front willow bamboo")
    args = parser.parse_args()

    wanted = set(args.only) if args.only else None
    for slug, _, _ in PROPS:
        if wanted is not None and slug not in wanted:
            continue
    if wanted is not None:
        unknown = wanted - {slug for slug, _, _ in PROPS}
        if unknown:
            raise SystemExit("未知 slug：" + ", ".join(sorted(unknown)))

    os.makedirs(ORIGINAL_DIR, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)
    token = load_client_key()
    for slug, cn_name, subject in PROPS:
        if wanted is not None and slug not in wanted:
            continue
        prompt = HEADER + subject + FOOTER
        result = post("/v1/images/generations", {
            "model": "grok-imagine-image",
            "prompt": prompt,
            "n": 1,
            "aspect_ratio": "1:1",
            "response_format": "b64_json",
        }, token)
        image = ensure_true_png(base64.b64decode(result["data"][0]["b64_json"]))
        with open(os.path.join(ORIGINAL_DIR, slug + "-v1.png"), "wb") as handle:
            handle.write(image)
        with open(os.path.join(PROMPT_DIR, slug + "-prop-v1.txt"), "w", encoding="utf-8") as handle:
            handle.write(prompt + "\n")
        print("saved:", cn_name, slug, len(image), "bytes")


if __name__ == "__main__":
    main()
