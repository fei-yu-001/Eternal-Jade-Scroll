import os
import sys
import numpy as np
from PIL import Image
from collections import deque

def process_image(src_path, dst_path, tolerance=38, feather=3):
    img = Image.open(src_path).convert("RGBA")
    arr = np.array(img)
    h, w, c = arr.shape

    # Sample corners to find background color
    corners = [
        arr[0, 0][:3],
        arr[0, w-1][:3],
        arr[h-1, 0][:3],
        arr[h-1, w-1][:3],
        arr[2, 2][:3],
        arr[h-3, w-3][:3]
    ]
    bg_color = np.median(corners, axis=0).astype(float)

    # Compute color distance from background
    rgb = arr[:, :, :3].astype(float)
    dist = np.linalg.norm(rgb - bg_color, axis=2)

    # Breadth-first search / Flood fill from edges
    visited = np.zeros((h, w), dtype=bool)
    mask_bg = np.zeros((h, w), dtype=bool)

    q = deque()
    # Add border pixels that are close to bg
    for x in range(w):
        for y in [0, h-1]:
            if not visited[y, x] and dist[y, x] < tolerance * 1.5:
                visited[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in [0, w-1]:
            if not visited[y, x] and dist[y, x] < tolerance * 1.5:
                visited[y, x] = True
                q.append((y, x))

    while q:
        cy, cx = q.popleft()
        mask_bg[cy, cx] = True
        for dy, dx in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < h and 0 <= nx < w and not visited[ny, nx]:
                visited[ny, nx] = True
                # If close enough to bg color, continue flood fill
                if dist[ny, nx] < tolerance:
                    q.append((ny, nx))

    # Feather alpha near boundary
    alpha = arr[:, :, 3].copy()
    alpha[mask_bg] = 0

    # Simple blur/feather on alpha
    arr[:, :, 3] = alpha
    out_img = Image.fromarray(arr, "RGBA")
    out_img.save(dst_path, "PNG")
    print(f"Processed: {os.path.basename(src_path)} -> transparent RGBA")

if __name__ == "__main__":
    items_dir = "GameClient/TwelveJade/Assets/Resources/Art/Items"
    for f in os.listdir(items_dir):
        if f.endswith(".png"):
            p = os.path.join(items_dir, f)
            process_image(p, p)
