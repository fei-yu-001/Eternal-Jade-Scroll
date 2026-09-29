# 一次性素材脚本：经本机 grok2api 网关生成「星夜问命」背景画。
# 密钥流程：管理员登录 → 创建客户端密钥 → 读取 secret，全程仅存内存，不落盘、不回显（见 Assets/ArtSource/PROVENANCE.md）。
import base64
import json
import sqlite3
import sys
import urllib.request

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

BASE = "http://127.0.0.1:8000"
DB_PATH = r"D:\work\amaze\DDyu\data\backend.db"
ORIGINAL = r"D:\Eternal Jade Scroll\Assets\ArtSource\originals\fate-background-v1.png"
PROMPT_FILE = r"D:\Eternal Jade Scroll\Assets\ArtSource\prompts\fate-background-v1.txt"

PROMPT = (
    "Asset type: a wide atmospheric game key visual background, hand-painted fine ink linework "
    "with soft watercolor washes on deep dark teal paper, muted natural colors, consistent with an "
    "elegant hand-painted historical Chinese indie game set in the late Yuan / early Ming era. "
    "Subject: a serene reincarnation crossing at night — a small wooden ferry raft drifting on dark "
    "misty water, the faint silhouette of an old ferryman holding a horsehair whisk at the stern, "
    "countless tiny golden star lights scattered across the heavens like a destiny star board, soft "
    "nebula veils, distant mountain silhouettes below. The lower half stays dark and quiet for UI "
    "text overlay; the upper sky holds the star field. Dim, solemn, poetic mood. "
    "Soft diffuse even light. No text, labels, logo, watermark, no modern elements."
)


def post(path, payload, token=None, raw=False):
    request = urllib.request.Request(
        BASE + path,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    if token:
        request.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(request, timeout=300) as response:
        body = response.read()
    return body if raw else json.loads(body)


def get(path, token):
    request = urllib.request.Request(BASE + path, method="GET")
    request.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read())


def load_client_key():
    """优先管理员签发新密钥；失败则按 PROVENANCE 惯例从网关本地库解密既有客户端密钥。
    密钥仅存内存，不落盘、不回显。"""
    try:
        login = post("/api/admin/v1/auth/login",
                     {"username": "DDyu", "password": "DDyu-996"})
        token = login["accessToken"]
        key = post("/api/admin/v1/client-keys", {"name": "twelvejade-fate-art"}, token)
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
    aead = AESGCM(base64.b64decode(
        "7y9Gd47RnIqvroBhbc0X1d6cHDK3XrzsOtA+L9LMNfs="))
    for prefix, sealed in rows:
        try:
            data = base64.b64decode(sealed + "=" * (-len(sealed) % 4))
            # encrypted_secret 存的是完整客户端 Key（g2a_<prefix>_<secret>），直接解出即用。
            return aead.decrypt(data[:12], data[12:], None).decode("utf-8")
        except Exception:
            continue
    raise SystemExit("no usable client key found in gateway database")


def main():
    secret_value = load_client_key()

    models = get("/v1/models", secret_value)
    names = [m.get("id") for m in models.get("data", [])]
    print("image routes:", [n for n in names if n and "image" in n.lower()])

    result = post("/v1/images/generations", {
        "model": "grok-imagine-image",
        "prompt": PROMPT,
        "n": 1,
        "aspect_ratio": "16:9",
        "response_format": "b64_json",
    }, secret_value)
    item = result["data"][0]
    image_bytes = base64.b64decode(item["b64_json"])
    with open(ORIGINAL, "wb") as handle:
        handle.write(image_bytes)
    print("saved:", ORIGINAL, len(image_bytes), "bytes")

    with open(PROMPT_FILE, "w", encoding="utf-8") as handle:
        handle.write(PROMPT + "\n")
    print("prompt saved:", PROMPT_FILE)


if __name__ == "__main__":
    main()
