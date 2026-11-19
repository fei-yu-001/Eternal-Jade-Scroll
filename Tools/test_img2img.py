import urllib.request
import urllib.error
import json
import uuid
import sys
import time

key = "lupi_QIKhHLEWmO4kopdDnzsQ7vdOiDafnvXe0SUycvvg_ew"
base = "https://lupi.mlgb7.com/v1"

def test_variations():
    url = f"{base}/images/variations"
    boundary = uuid.uuid4().hex
    with open("Tools/test-lupi-output.png", "rb") as f:
        img_data = f.read()

    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="image"; filename="input.png"\r\n'
        f"Content-Type: image/png\r\n\r\n"
    ).encode("utf-8") + img_data + (
        f"\r\n--{boundary}\r\n"
        f'Content-Disposition: form-data; name="n"\r\n\r\n'
        f"1\r\n"
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="size"\r\n\r\n'
        f"1024x1024\r\n"
        f"--{boundary}--\r\n"
    ).encode("utf-8")

    req = urllib.request.Request(
        url,
        data=body,
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": f"multipart/form-data; boundary={boundary}"
        }
    )
    print("Testing /images/variations...")
    try:
        with urllib.request.urlopen(req, timeout=40) as r:
            res = json.loads(r.read().decode())
            print("Variations response:", res)
            return res
    except urllib.error.HTTPError as e:
        print("Variations HTTPError:", e.code, e.read().decode("utf-8", errors="ignore"))
    except Exception as e:
        print("Variations Error:", e)
    return None

def test_edits():
    url = f"{base}/images/edits"
    boundary = uuid.uuid4().hex
    with open("Tools/test-lupi-output.png", "rb") as f:
        img_data = f.read()

    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="image"; filename="input.png"\r\n'
        f"Content-Type: image/png\r\n\r\n"
    ).encode("utf-8") + img_data + (
        f"\r\n--{boundary}\r\n"
        f'Content-Disposition: form-data; name="prompt"\r\n\r\n'
        f"Add golden morning sunrays and floating mist, keep Chinese ink painting style\r\n"
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="n"\r\n\r\n'
        f"1\r\n"
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="size"\r\n\r\n'
        f"1024x1024\r\n"
        f"--{boundary}--\r\n"
    ).encode("utf-8")

    req = urllib.request.Request(
        url,
        data=body,
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": f"multipart/form-data; boundary={boundary}"
        }
    )
    print("Testing /images/edits...")
    try:
        with urllib.request.urlopen(req, timeout=40) as r:
            res = json.loads(r.read().decode())
            print("Edits response:", res)
            return res
    except urllib.error.HTTPError as e:
        print("Edits HTTPError:", e.code, e.read().decode("utf-8", errors="ignore"))
    except Exception as e:
        print("Edits Error:", e)
    return None

if __name__ == "__main__":
    test_edits()
    test_variations()
