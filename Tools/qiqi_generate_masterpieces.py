import os
import sys
import json
import urllib.request
import urllib.error
import time

API_KEY = os.environ["LUPI_API_KEY"]
BASE_URL = "https://lupi.mlgb7.com/v1/images/generations"

def generate(prompt, filename, model="gpt-image-2.5", size="1024x1024"):
    payload = {
        "model": model,
        "prompt": prompt,
        "n": 1,
        "size": size
    }
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        BASE_URL,
        data=data,
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json"
        }
    )
    print(f"Generating '{filename}' with {model}...")
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            elapsed = time.time() - start
            res = json.loads(resp.read().decode())
            if "data" in res and len(res["data"]) > 0:
                url = res["data"][0].get("url")
                print(f"Success in {elapsed:.1f}s! URL: {url}")
                # Download to path
                out_path = os.path.join("Tools", filename)
                urllib.request.urlretrieve(url, out_path)
                print(f"Saved to {out_path}")
                return out_path, url
    except Exception as e:
        print(f"Failed to generate {filename}: {e}")
    return None, None

if __name__ == "__main__":
    # Asset 1: Meridian Daoist Celestial Body
    prompt_meridian = (
        "Production game background art for Chinese Xianxia cultivation game: "
        "A handsome, noble Taoist celestial master in elegant pale blue and white robes meditating serenely atop a jade lotus throne on a majestic mountain peak above a sea of clouds. "
        "Pure golden meridian qi streams and gentle warm golden aura glowing around his body, subtle celestial stars, sunrise mist. "
        "Atmosphere is sacred, peaceful, open, and majestic, traditional Song dynasty fine brushwork with modern Xianxia concept art, cinematic masterpiece, 8k resolution, clean composition."
    )
    generate(prompt_meridian, "meridian-daoist-masterpiece.png")

    # Asset 2: Qingshi Town Ink-wash Landscape Map
    prompt_map = (
        "Production game map art: Chinese Song dynasty traditional ink-wash and blue-green landscape panorama (Qianli Jiangshan tu style). "
        "A tranquil ancient riverside market town nestled among lush emerald mountains, gentle cyan river winding through ancient stone bridges and pagodas. "
        "Hand-painted on aged silk scroll, mist swirling around mountain peaks, delicate line art, authentic ancient Chinese cartography and imperial handscroll aesthetic, clean, majestic."
    )
    generate(prompt_map, "qingshi-shanhe-scroll.png")
