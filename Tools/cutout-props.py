# 道具立绘抠底：宣纸底色从四角连通清除 + 边缘羽化，输出 RGBA PNG 到 Resources/Art/Props/。
# 用法：python Tools/cutout-props.py  （处理 originals/props/*-v1.png）
import sys
from collections import deque
from pathlib import Path

from PIL import Image

ROOT = Path(r"D:\Eternal Jade Scroll")
SOURCE_DIR = ROOT / "Assets" / "ArtSource" / "originals" / "props"
TARGET_DIR = ROOT / "GameClient" / "TwelveJade" / "Assets" / "Resources" / "Art" / "Props"


def key_color(pixel):
    return pixel[0], pixel[1], pixel[2]


def cutout(source: Path, target: Path, tolerance: int = 26):
    image = Image.open(source).convert("RGBA")
    width, height = image.size
    pixels = image.load()

    # 参考底色：四角 8px 均值（道具画为均匀宣纸底 #EDE6D6 系）。
    corners = [(0, 0), (width - 8, 0), (0, height - 8), (width - 8, height - 8)]
    samples = [pixels[x + dx, y + dy]
               for x, y in corners for dx in range(8) for dy in range(8)]
    reference = tuple(sum(p[i] for p in samples) // len(samples) for i in range(3))

    def is_background(px):
        r, g, b = key_color(px)
        return abs(r - reference[0]) <= tolerance and \
               abs(g - reference[1]) <= tolerance and \
               abs(b - reference[2]) <= tolerance

    # 自四边向内的连通泛洪：只清除与边缘相连的底色，道具内部同色区域保留。
    visited = bytearray(width * height)
    queue = deque()
    for x in range(width):
        for y in (0, height - 1):
            if is_background(pixels[x, y]) and not visited[y * width + x]:
                visited[y * width + x] = 1
                queue.append((x, y))
    for y in range(height):
        for x in (0, width - 1):
            if is_background(pixels[x, y]) and not visited[y * width + x]:
                visited[y * width + x] = 1
                queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        pixels[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height and not visited[ny * width + nx] \
                    and is_background(pixels[nx, ny]):
                visited[ny * width + nx] = 1
                queue.append((nx, ny))

    # 边缘羽化：与透明相邻的不透明像素 alpha 减半一次，弱化硬边。
    soften = []
    for y in range(height):
        for x in range(width):
            if pixels[x, y][3] == 0:
                continue
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if 0 <= nx < width and 0 <= ny < height and pixels[nx, ny][3] == 0:
                    soften.append((x, y))
                    break
    for x, y in soften:
        r, g, b, a = pixels[x, y]
        pixels[x, y] = (r, g, b, a // 2)

    image.save(target)
    kept = sum(1 for y in range(height) for x in range(width) if pixels[x, y][3] > 0)
    print(f"{source.name} -> {target.name}  保留像素 {kept * 100 // (width * height)}%")


def main():
    TARGET_DIR.mkdir(parents=True, exist_ok=True)
    for source in sorted(SOURCE_DIR.glob("*-v1.png")):
        target = TARGET_DIR / (source.name.replace("-v1.png", ".png"))
        cutout(source, target)


if __name__ == "__main__":
    sys.exit(main())
