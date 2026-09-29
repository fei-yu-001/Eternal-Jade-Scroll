# 一次性素材脚本：经本机 grok2api 网关生成青石镇道具立绘（9 件，1:1，宣纸底待抠）。
# 密钥流程同 generate-fate-art.py：仅存内存，不落盘、不回显（见 Assets/ArtSource/PROVENANCE.md）。
import base64
import json
import sqlite3
import urllib.request

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

BASE = "http://127.0.0.1:8000"
DB_PATH = r"D:\work\amaze\DDyu\data\backend.db"
ORIGINAL_DIR = r"D:\Eternal Jade Scroll\Assets\ArtSource\originals\props"
PROMPT_DIR = r"D:\Eternal Jade Scroll\Assets\ArtSource\prompts"

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


def main():
    token = load_client_key()
    for slug, cn_name, subject in PROPS:
        prompt = HEADER + subject + FOOTER
        result = post("/v1/images/generations", {
            "model": "grok-imagine-image",
            "prompt": prompt,
            "n": 1,
            "aspect_ratio": "1:1",
            "response_format": "b64_json",
        }, token)
        image = base64.b64decode(result["data"][0]["b64_json"])
        with open(ORIGINAL_DIR + "\\" + slug + "-v1.png", "wb") as handle:
            handle.write(image)
        with open(PROMPT_DIR + "\\" + slug + "-prop-v1.txt", "w", encoding="utf-8") as handle:
            handle.write(prompt + "\n")
        print("saved:", cn_name, slug, len(image), "bytes")


if __name__ == "__main__":
    main()
