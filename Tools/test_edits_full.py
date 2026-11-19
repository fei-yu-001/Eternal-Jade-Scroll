import urllib.request
import urllib.error
import json
import uuid
import time
import os

key = "lupi_QIKhHLEWmO4kopdDnzsQ7vdOiDafnvXe0SUycvvg_ew"
url = "https://lupi.mlgb7.com/v1/images/edits"

def test_edits():
    boundary = uuid.uuid4().hex
    # Use our downloaded image
    with open("Tools/test-lupi-output.png", "rb") as f:
        img_data = f.read()

    # multipart payload
    parts = []
    
    # image field
    parts.append(f"--{boundary}\r\n".encode("utf-8"))
    parts.append(b'Content-Disposition: form-data; name="image"; filename="input.png"\r\n')
    parts.append(b"Content-Type: image/png\r\n\r\n")
    parts.append(img_data)
    parts.append(b"\r\n")

    # prompt field
    parts.append(f"--{boundary}\r\n".encode("utf-8"))
    parts.append(b'Content-Disposition: form-data; name="prompt"\r\n\r\n')
    parts.append("Transform this Taoist mountain into golden sunrise with glowing mist\r\n".encode("utf-8"))

    # model field
    parts.append(f"--{boundary}\r\n".encode("utf-8"))
    parts.append(b'Content-Disposition: form-data; name="model"\r\n\r\n')
    parts.append(b"gpt-image-2\r\n")

    # n field
    parts.append(f"--{boundary}\r\n".encode("utf-8"))
    parts.append(b'Content-Disposition: form-data; name="n"\r\n\r\n')
    parts.append(b"1\r\n")

    # size field
    parts.append(f"--{boundary}\r\n".encode("utf-8"))
    parts.append(b'Content-Disposition: form-data; name="size"\r\n\r\n')
    parts.append(b"1024x1024\r\n")

    parts.append(f"--{boundary}--\r\n".encode("utf-8"))
    payload = b"".join(parts)

    req = urllib.request.Request(
        url,
        data=payload,
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": f"multipart/form-data; boundary={boundary}"
        }
    )

    print("Sending /v1/images/edits request (timeout=120s)...")
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            elapsed = time.time() - start
            res = json.loads(r.read().decode())
            print(f"Edits SUCCESS in {elapsed:.1f}s!")
            if "data" in res and len(res["data"]) > 0:
                print("Image URL:", res["data"][0].get("url"))
            return res
    except urllib.error.HTTPError as e:
        elapsed = time.time() - start
        err = e.read().decode("utf-8", errors="ignore")
        print(f"HTTPError {e.code} after {elapsed:.1f}s: {err}")
    except Exception as e:
        elapsed = time.time() - start
        print(f"Error after {elapsed:.1f}s: {e}")
    return None

if __name__ == "__main__":
    test_edits()
