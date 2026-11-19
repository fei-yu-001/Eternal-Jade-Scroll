import os
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SOURCE = r"C:\Users\DDyu\Desktop\十二玉楼长生经_壁纸.png"
TEMP = r"C:\Users\DDyu\Desktop\_maple_render.mp4"
OUTPUT = r"C:\Users\DDyu\Desktop\十二玉楼长生经_动态主页演示_枫叶版.mp4"
FFMPEG = r"D:\userfulapp\ffmpeg\bin\ffmpeg.exe"


def make_leaf(seed, size):
    rng = np.random.default_rng(seed)
    w, h = 128, 144
    scale = 3
    mask = Image.new("L", (w * scale, h * scale), 0)
    draw = ImageDraw.Draw(mask)
    points = [(64, 132), (54, 96), (31, 118), (38, 85), (9, 91),
              (31, 67), (4, 51), (39, 51), (32, 22), (55, 40),
              (64, 5), (74, 40), (97, 22), (90, 51), (124, 51),
              (97, 68), (120, 91), (90, 85), (97, 118), (74, 96)]
    draw.polygon([(x * scale, y * scale) for x, y in points], fill=255)
    mask = mask.resize((w, h), Image.Resampling.LANCZOS)

    y = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    x = np.linspace(0, 1, w, dtype=np.float32)[None, :, None]
    top = np.array([178, 28, 24], dtype=np.float32)
    bottom = np.array([244, 115, 35], dtype=np.float32)
    color = top * (1 - y) + bottom * y
    edge = np.abs(x - 0.5) * 2
    color = color * (1 - 0.16 * edge)
    color = np.broadcast_to(color, (h, w, 3)).copy()
    rgba = np.dstack([color.astype(np.uint8), np.asarray(mask)])
    leaf = Image.fromarray(rgba, "RGBA")

    veins = Image.new("RGBA", (w * scale, h * scale), (0, 0, 0, 0))
    vd = ImageDraw.Draw(veins)
    center = (64 * scale, 83 * scale)
    tips = [(64, 8), (35, 24), (12, 52), (13, 88), (34, 115),
            (95, 24), (117, 52), (116, 88), (94, 115)]
    for tx, ty in tips:
        vd.line([center, (tx * scale, ty * scale)], fill=(255, 195, 104, 155), width=2 * scale)
    vd.line([(64 * scale, 82 * scale), (64 * scale, 139 * scale)],
            fill=(104, 48, 29, 210), width=3 * scale)
    veins = veins.resize((w, h), Image.Resampling.LANCZOS)
    leaf.alpha_composite(veins)
    return leaf.resize((size, int(size * h / w)), Image.Resampling.LANCZOS)


def main():
    raw = np.fromfile(SOURCE, dtype=np.uint8)
    base = cv2.imdecode(raw, cv2.IMREAD_COLOR)
    if base is None:
        raise SystemExit(f"Cannot read source image: {SOURCE}")
    height, width = base.shape[:2]
    fps, seconds = 30, 8
    total = fps * seconds

    gray = cv2.cvtColor(base, cv2.COLOR_BGR2GRAY)
    _, bright = cv2.threshold(gray, 168, 255, cv2.THRESH_BINARY)
    bloom = cv2.GaussianBlur(base, (41, 41), 18)
    bloom_mask = cv2.GaussianBlur(bright, (41, 41), 18).astype(np.float32) / 255.0
    bloom_mask = bloom_mask[:, :, None]

    rng = np.random.default_rng(20261104)
    leaves = []
    for i in range(26):
        size = int(rng.integers(18, 59))
        sprite = make_leaf(i + 4, size)
        leaves.append({
            "sprite": sprite,
            "cx": float(rng.uniform(-40, width + 40)),
            "cy": float(rng.uniform(100, height + 40)),
            "rx": float(rng.uniform(28, 170)),
            "ry": float(rng.uniform(25, 112)),
            "phase": float(rng.uniform(0, 2 * np.pi)),
            "angle": float(rng.uniform(0, 360)),
            "turns": int(rng.choice([-2, -1, 1, 2])),
            "alpha": int(rng.uniform(135, 225)),
            "depth": float(rng.uniform(0.55, 1.25)),
            "blur": float(rng.choice([0, 0, 0, 0.7, 1.2])),
        })

    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    writer = cv2.VideoWriter(TEMP, fourcc, fps, (width, height))
    if not writer.isOpened():
        raise SystemExit("Could not open video writer")

    rows, cols = np.indices((height, width), dtype=np.float32)
    for frame_index in range(total):
        t = frame_index / total
        phase = 2 * np.pi * t

        # Gentle cyclic cloud drift in the lower half, plus slow camera breathing.
        cloud_weight = np.exp(-((rows - height * 0.68) / (height * 0.27)) ** 2)
        displacement = (4.2 * np.sin(phase + rows / 125.0) * cloud_weight).astype(np.float32)
        moved = cv2.remap(base, cols + displacement, rows, cv2.INTER_LINEAR,
                          borderMode=cv2.BORDER_REFLECT)
        zoom = 1.0 + 0.035 * (1 - np.cos(phase)) / 2
        focus = (width * 0.52, height * 0.40)
        matrix = cv2.getRotationMatrix2D(focus, 0, zoom)
        matrix[0, 2] += 4.0 * np.sin(phase)
        matrix[1, 2] += 2.5 * np.sin(phase * 2)
        frame = cv2.warpAffine(moved, matrix, (width, height), borderMode=cv2.BORDER_REFLECT)

        glow = cv2.warpAffine(bloom, matrix, (width, height), borderMode=cv2.BORDER_REFLECT)
        mask = cv2.warpAffine(bloom_mask, matrix, (width, height), borderMode=cv2.BORDER_REFLECT)
        if mask.ndim == 2:
            mask = mask[:, :, None]
        pulse = 0.5 + 0.5 * np.sin(phase * 2)
        frame = np.clip(frame.astype(np.float32) + glow.astype(np.float32) * mask * (0.12 + 0.10 * pulse), 0, 255).astype(np.uint8)

        canvas = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)).convert("RGBA")
        for leaf in leaves:
            a = phase + leaf["phase"]
            x = leaf["cx"] + leaf["rx"] * np.cos(a)
            y = leaf["cy"] + leaf["ry"] * np.sin(a) + 8 * np.sin(2 * a)
            angle = leaf["angle"] + 360 * leaf["turns"] * t + 12 * np.sin(2 * a)
            sprite = leaf["sprite"].rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
            if leaf["blur"]:
                sprite = sprite.filter(ImageFilter.GaussianBlur(leaf["blur"]))
            alpha = sprite.getchannel("A").point(lambda value, m=leaf["alpha"]: value * m // 255)
            sprite.putalpha(alpha)
            px, py = int(x - sprite.width / 2), int(y - sprite.height / 2)
            canvas.alpha_composite(sprite, (px, py))

        bgr = cv2.cvtColor(np.asarray(canvas.convert("RGB")), cv2.COLOR_RGB2BGR)
        writer.write(bgr)
        if (frame_index + 1) % 60 == 0:
            print(f"Rendered {frame_index + 1}/{total} frames")

    writer.release()
    command = [FFMPEG, "-y", "-i", TEMP, "-c:v", "libx264", "-preset", "medium",
               "-crf", "19", "-pix_fmt", "yuv420p", "-movflags", "+faststart", OUTPUT]
    result = os.spawnv(os.P_WAIT, FFMPEG, command)
    if result != 0:
        raise SystemExit(f"ffmpeg failed with exit code {result}")
    os.remove(TEMP)
    print(f"Saved: {OUTPUT}")


if __name__ == "__main__":
    main()
