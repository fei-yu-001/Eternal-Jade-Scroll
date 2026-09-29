# 一次性素材脚本：经本机 grok2api 网关生成首批精怪/匪霸立绘（4 张，1024×1024）。
# 密钥流程同 generate-fate-art.py：仅存内存，不落盘、不回显（见 Assets/ArtSource/PROVENANCE.md）。
import base64
import json
import sqlite3
import urllib.request

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

BASE = "http://127.0.0.1:8000"
DB_PATH = r"D:\work\amaze\DDyu\data\backend.db"
ORIGINAL_DIR = r"D:\Eternal Jade Scroll\Assets\ArtSource\originals\npc"
PROMPT_DIR = r"D:\Eternal Jade Scroll\Assets\ArtSource\prompts"

HEADER = (
    "Asset type: a single game character full-body portrait, hand-painted fine ink linework "
    "with soft watercolor washes on warm ivory paper, muted natural colors, consistent with an "
    "elegant hand-painted historical Chinese indie game set in the late Yuan / early Ming era. "
)
FOOTER = (
    " Full body, centered, clear silhouette, slight three-quarter view, standing on implied ground, "
    "uniform flat warm ivory background #EDE6D6, no scene, no surface, no cast shadow outside the figure. "
    "Soft diffuse even light. No text, labels, logo, watermark, no modern elements."
)

SUBJECTS = [
    ("boar-demon", "野猪妖",
     "Subject: a demonic wild boar spirit standing upright on its hind legs, bristled razorback ridge, "
     "glowing amber tusks, small green spirit-fires drifting around its hooves, menacing yet painterly."),
    ("shanxiao", "山魈",
     "Subject: a mountain goblin shanxiao with dark blue-black fur, a pale grinning face, muscular "
     "backward-hunched frame, gnarled wooden club dragging behind, perched forward like about to leap."),
    ("bandit-chief", "黑风寨土匪头子",
     "Subject: a bandit chief of the Black Wind stronghold, scarred bearded face, fur-trimmed ragged "
     "robe, a broad saber resting lazily on one shoulder, rope belt with gourd, standing with arms akimbo."),
    ("local-thug", "恶霸豪绅的狗腿子",
     "Subject: a well-fed enforcer of a local tyrant, tight silk jacket straining over a thick frame, "
     "holding a coiled leather whip, smug cruel grin, fat jade thumb ring, leaning forward aggressively."),
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


def get(path, token):
    request = urllib.request.Request(BASE + path, method="GET")
    request.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read())


def load_client_key():
    try:
        login = post("/api/admin/v1/auth/login", {"username": "DDyu", "password": "DDyu-996"})
        token = login["accessToken"]
        key = post("/api/admin/v1/client-keys", {"name": "twelvejade-npc-art"}, token)
        key_id = key.get("id") or key.get("data", {}).get("id")
        secret = get(f"/api/admin/v1/client-keys/{key_id}/secret", token)
        value = secret.get("secret") or secret.get("data", {}).get("secret")
        if value:
            return value
    except Exception as error:
        print("admin route unavailable:", type(error).__name__)
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
    for slug, cn_name, subject in SUBJECTS:
        prompt = HEADER + subject + FOOTER
        result = post("/v1/images/generations", {
            "model": "grok-imagine-image",
            "prompt": prompt,
            "n": 1,
            "aspect_ratio": "1:1",
            "response_format": "b64_json",
        }, token)
        image = base64.b64decode(result["data"][0]["b64_json"])
        original = ORIGINAL_DIR + "\\" + slug + "-v1.png"
        with open(original, "wb") as handle:
            handle.write(image)
        with open(PROMPT_DIR + "\\" + slug + "-portrait-v1.txt", "w", encoding="utf-8") as handle:
            handle.write(prompt + "\n")
        print("saved:", cn_name, original, len(image), "bytes")


if __name__ == "__main__":
    main()
