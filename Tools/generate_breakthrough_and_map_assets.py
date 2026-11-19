import os
import sys
import json
import urllib.request
import urllib.error
import time

API_KEY = os.environ["LUPI_API_KEY"]
BASE_URL = "https://lupi.mlgb7.com/v1"

def generate_and_save(prompt, out_path, models=["gpt-image-2.5-flare", "gpt-image-2.5"], size="1024x1024", timeout=180):
    url = f"{BASE_URL}/images/generations"
    for model in models:
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
        print(f"Generating with model='{model}' for: {os.path.basename(out_path)}...", flush=True)
        start = time.time()
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                elapsed = time.time() - start
                res = json.loads(resp.read().decode())
                if "data" in res and len(res["data"]) > 0:
                    img_url = res["data"][0].get("url")
                    if img_url:
                        print(f"  Success in {elapsed:.1f}s! Downloading...", flush=True)
                        urllib.request.urlretrieve(img_url, out_path)
                        print(f"  Saved to {out_path} ({os.path.getsize(out_path)} bytes)", flush=True)
                        return True
                print("  Unexpected response:", res, flush=True)
        except Exception as e:
            elapsed = time.time() - start
            print(f"  Failed with model='{model}' after {elapsed:.1f}s: {e}", flush=True)
            time.sleep(2)
    return False

if __name__ == "__main__":
    tasks = [
        {
            "name": "guigu-style-world-map.png",
            "prompt": "Exquisite ancient Chinese xianxia world map, Song Dynasty hand-drawn gongbi ink wash and blue-green mineral landscape style, Tale of Immortal (Gui Gu Ba Huang) style map. Vast picturesque realm with winding azure rivers, misty verdant mountain ridges, bamboo forests, Qingyun Taoist sect pavilions on misty crags, riverside fishing ferry, Linxi ancient village, peach blossom woods, ancient stone bridge, bustling street market. Delicate traditional cloud corner borders, ancient fengshui compass in top corner, parchment silk scroll texture, clean bird-eye perspective, masterpiece game concept art, 8k resolution."
        },
        {
            "name": "meridian-inner-vision-doll.png",
            "prompt": "Traditional Chinese medicine and Taoist meditation inner vision (Neishi) acupuncture meridian chart, glowing translucent celestial jade figure silhouette of meditating Taoist cultivator in seated lotus posture. Clear glowing meridian channels (Ren and Du meridians) with luminous acupuncture points (Huiyin, Shenque, Yutang, Xuanji, Lianquan) connected by flowing golden Qi energy threads, ancient Daoist talismanic diagrams, subtle floating ethereal smoke, dark clean isolated background, xianxia game breakthrough UI popup asset, crisp elegant vector and ink style."
        },
        {
            "name": "ancient-jade-command-plate.png",
            "prompt": "Chinese xianxia game UI button, ancient warm white mutton-fat nephrite jade rectangular plaque, carved with delicate antique gold bronze cloud and thunder (Leiwen) filigree border, subtle inner jade luminescence, dignified, elegant, timeless oriental aesthetic, not cheap plastic, not gaudy neon, isolated on clean dark background, 8k game UI asset."
        }
    ]

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "aesthetic_assets_v2")
    os.makedirs(out_dir, exist_ok=True)

    for t in tasks:
        target = os.path.join(out_dir, t["name"])
        if not os.path.exists(target):
            generate_and_save(t["prompt"], target)
        else:
            print(f"Skipping existing: {t['name']}", flush=True)
