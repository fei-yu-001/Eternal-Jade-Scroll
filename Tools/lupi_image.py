import os
import sys
import json
import urllib.request
import urllib.error
import time

API_KEY = os.environ["LUPI_API_KEY"]
BASE_URL = "https://lupi.mlgb7.com/v1"

def test_models():
    url = f"{BASE_URL}/models"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {API_KEY}"})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read().decode())
            print("Available models:")
            for m in data.get("data", []):
                print("  -", m.get("id"))
    except Exception as e:
        print("Failed to list models:", e)

def generate_image(prompt, model="gpt-image-2.5", size="1024x1024", timeout=90):
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
    print(f"Requesting image with model='{model}', prompt='{prompt[:40]}...' (timeout={timeout}s)...")
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            elapsed = time.time() - start
            res = json.loads(resp.read().decode())
            print(f"Success in {elapsed:.1f}s!")
            if "data" in res and len(res["data"]) > 0:
                item = res["data"][0]
                url = item.get("url")
                b64 = item.get("b64_json")
                if url:
                    print("URL:", url)
                    return url
                elif b64:
                    print("Received b64 data (length:", len(b64), ")")
                    return b64
            print("Response:", res)
            return res
    except urllib.error.HTTPError as e:
        elapsed = time.time() - start
        err_msg = e.read().decode("utf-8", errors="replace")
        print(f"HTTPError {e.code} after {elapsed:.1f}s: {err_msg}")
    except Exception as e:
        elapsed = time.time() - start
        print(f"Error after {elapsed:.1f}s: {e}")
    return None

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "list":
        test_models()
    else:
        model = sys.argv[1] if len(sys.argv) > 1 else "gpt-image-2.5"
        prompt = sys.argv[2] if len(sys.argv) > 2 else "Song dynasty Chinese ink wash landscape, misty mountains, green river, minimal elegant"
        generate_image(prompt, model=model, timeout=90)
