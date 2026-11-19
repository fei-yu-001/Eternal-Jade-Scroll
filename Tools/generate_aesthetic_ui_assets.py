import os
import sys
import json
import urllib.request
import urllib.error
import time

API_KEY = os.environ["LUPI_API_KEY"]
BASE_URL = "https://lupi.mlgb7.com/v1"

def generate_and_save(prompt, out_path, model="gpt-image-2.5", size="1024x1024"):
    url = f"{BASE_URL}/images/generations"
    payload = {
        "model": model,
        "prompt": prompt,
        "n": 1,
        "size": size
    }
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json"
        }
    )
    print(f"Generating for: {os.path.basename(out_path)}...")
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            elapsed = time.time() - start
            res = json.loads(resp.read().decode())
            if "data" in res and len(res["data"]) > 0:
                img_url = res["data"][0].get("url")
                if img_url:
                    print(f"  Success in {elapsed:.1f}s! Downloading from {img_url[:60]}...")
                    urllib.request.urlretrieve(img_url, out_path)
                    print(f"  Saved to {out_path} ({os.path.getsize(out_path)} bytes)")
                    return True
            print("  Unexpected response:", res)
    except Exception as e:
        print(f"  Error: {e}")
    return False

if __name__ == "__main__":
    tasks = [
        {
            "name": "wuxing-lingpan-compass.png",
            "prompt": "Chinese xianxia game UI element, Five Elements Spiritual Roots Compass (Wu Xing: Metal, Wood, Water, Fire, Earth), exquisite circular jade and bronze celestial compass, five glowing elemental orbs in a ring: golden white (Metal), emerald green (Wood), azure blue (Water), vermilion crimson (Fire), earthy amber yellow (Earth). Ancient Taoist bagua talisman engravings, subtle ethereal mystic glow, isolated dark background, intricate oriental UI design, crisp 8k digital art, game asset."
        },
        {
            "name": "tianji-scroll-fairyland.png",
            "prompt": "Chinese xianxia game quest banner UI, ethereal floating heavenly jade scroll (Tianji Scroll), elegant pale celadon green and ivory silk banner unfurled horizontally, subtle flowing gold silk trim, delicate jade scroll roller rods at both ends, faint cloud and crane watermark patterns, mystical Taoist sigil seal, soft celestial luminescent rim light, clean isolated background, game asset, elegant, graceful, poetic, not royal imperial edict, not gaudy red gold."
        },
        {
            "name": "shenshi-doll-pondering.png",
            "prompt": "Chibi cute anime style Chinese xianxia character, mental consciousness spirit avatar (Shenshi doll) of the young Taoist protagonist. Black hair topknot, cyan-white cloud-pattern Taoist robe. Sitting cross-legged on a soft glowing jade cushion, scratching his head with one hand while holding an ancient open bamboo scripture book in front of him, humorous puzzled concentrating expression with comical sweat drop, cute cartoon 2D game asset, isolated solid white background, clean vector illustration style, sticker art."
        },
        {
            "name": "shenshi-doll-enlightened.png",
            "prompt": "Chibi cute anime style Chinese xianxia character, mental consciousness spirit avatar (Shenshi doll) of the young Taoist protagonist. Black hair topknot, cyan-white cloud-pattern Taoist robe. Sitting cross-legged, clapping hands in excitement with a glowing sparkling golden Taoist rune above his head, cheerful enlightened smile, ancient bamboo book floating beside, cute cartoon 2D game asset, isolated solid white background, clean vector illustration style, sticker art."
        }
    ]

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "aesthetic_assets")
    os.makedirs(out_dir, exist_ok=True)

    for t in tasks:
        target = os.path.join(out_dir, t["name"])
        if not os.path.exists(target):
            generate_and_save(t["prompt"], target)
        else:
            print(f"Skipping existing: {t['name']}")
