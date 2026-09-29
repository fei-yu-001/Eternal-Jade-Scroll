#!/usr/bin/env python3
"""分层纸偶切线测量：给 Core/PuppetActor 找六关节的切线与"手臂是否与身体粘连"。

背景：PuppetActor 早期把切线写死成常量（腿 x 0/0.5、躯干 0.15–0.85、手臂 0–0.18/0.82–1）。
宽袖长袍的立绘里，袖子与躯干本就连成一片，按 0.18 去切会切下一块几乎全空的矩形，
一摆动就露出"袖子被搬走"的破洞——战斗页 230px 下尤其明显。

用法：
    python Tools/split-puppet-layers.py                       # 测全部正面立绘
    python Tools/split-puppet-layers.py farmer-female-1-front # 测指定图
    python Tools/split-puppet-layers.py --preview farmer-female-1-front
                                                          # 出一张切线预览图到 Tools/screenshots/

输出是每行的 alpha 轮廓宽度、建议切线（腰/胯/腋），以及每条切线处手臂与身体之间
是否存在 alpha 空隙（gap）。没有空隙就说明手臂与躯干粘连，应走"少切"策略。
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART_DIR = ROOT / "GameClient" / "TwelveJade" / "Assets" / "Resources" / "Art"
OUT_DIR = ROOT / "Tools" / "screenshots"
ALPHA_MIN = 40          # 算作"实心"的 alpha 阈值
GAP_MIN = 3             # 判定手臂与身体分离所需的最小空隙（像素）


def silhouette_rows(image: Image.Image, step: int = 4) -> list[tuple[int, int, int, int]]:
    """返回 (y, 最小x, 最大x, 实心像素数)，只取含实心像素的行。"""
    alpha = image.getchannel("A")
    width, height = image.size
    rows = []
    for y in range(0, height, step):
        xs = [x for x in range(width) if alpha.getpixel((x, y)) > ALPHA_MIN]
        if xs:
            rows.append((y, min(xs), max(xs), len(xs)))
    return rows


def find_gap(alpha: Image.Image, y: int, span: tuple[int, int]) -> int | None:
    """在 y 行的 span 内找最宽的透明空隙；没有就返回 None（说明这一行是连成一片的）。"""
    left, right = span
    best = 0
    best_start = None
    run = 0
    run_start = None
    for x in range(left, right + 1):
        if alpha.getpixel((x, y)) <= ALPHA_MIN:
            if run == 0:
                run_start = x
            run += 1
            if run > best:
                best = run
                best_start = run_start
        else:
            run = 0
            run_start = None
    return best_start if best >= GAP_MIN else None


def analyse(path: Path) -> dict:
    image = Image.open(path).convert("RGBA")
    alpha = image.getchannel("A")
    width, height = image.size
    rows = silhouette_rows(image)
    if not rows:
        return {"file": path.name, "empty": True}

    top, bottom = rows[0][0], rows[-1][0]
    span_h = max(1, bottom - top)
    box = (min(r[1] for r in rows), top, max(r[2] for r in rows), bottom)
    box_w = max(1, box[2] - box[0])

    def width_at(frac: float) -> int:
        y = int(top + span_h * frac)
        y = min(y, height - 1)
        xs = [x for x in range(width) if alpha.getpixel((x, y)) > ALPHA_MIN]
        return len(xs)

    # 关键比例：脖子最窄处（头/躯干界）、腰、胯。
    head_base = 0.20     # 头顶往下 20%
    waist = 0.52
    hip = 0.62
    arms_fused = True
    gap_report = []
    for frac, label in ((head_base, "腋下"), (waist, "腰"), (hip, "胯")):
        y = min(int(top + span_h * frac), height - 1)
        xs = [x for x in range(width) if alpha.getpixel((x, y)) > ALPHA_MIN]
        gap = find_gap(alpha, y, (min(xs), max(xs))) if xs else None
        gap_report.append((label, y, gap))
        if label in ("腋下", "腰") and gap is not None:
            arms_fused = False

    return {
        "file": path.name,
        "size": image.size,
        "box": box,
        "head_base": head_base,
        "waist": waist,
        "hip": hip,
        "widths": {label: width_at(frac) for label, frac in
                   (("头", 0.10), ("肩", head_base), ("腰", waist), ("胯", hip), ("膝", 0.78))},
        "gaps": gap_report,
        "arms_fused": arms_fused,
        "leg_split": find_gap(alpha, min(int(top + span_h * 0.85), height - 1), (box[0], box[2])),
    }


def draw_preview(path: Path, info: dict) -> Path:
    image = Image.open(path).convert("RGBA")
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    from PIL import ImageDraw
    draw = ImageDraw.Draw(overlay)
    left, top, right, bottom = info["box"]
    span_h = bottom - top
    colors = {"head": (200, 60, 60, 255), "torso": (60, 140, 200, 255),
              "legL": (60, 180, 90, 255), "legR": (200, 160, 40, 255)}
    lines = [
        (info["head_base"], "head", True),
        (info["waist"], "torso", True),
        (info["hip"], "legL", True),
    ]
    for frac, name, horizontal in lines:
        y = int(top + span_h * frac)
        draw.line([(left - 8, y), (right + 8, y)], fill=colors[name], width=2)
    leg_y = int(top + span_h * info["hip"])
    draw.line([((left + right) // 2, leg_y), ((left + right) // 2, bottom + 8)],
              fill=colors["legR"], width=2)
    for frac in (info["waist"], info["hip"]):
        y = int(top + span_h * frac)
        draw.line([(left - 8, y), (right + 8, y)], fill=colors["torso"], width=1)
    out = Image.alpha_composite(image, overlay)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    target = OUT_DIR / f"split-preview-{path.stem}.png"
    out.save(target)
    return target


def main(argv: list[str]) -> int:
    args = [a for a in argv if not a.startswith("--")]
    preview = "--preview" in argv
    if args:
        targets = [ART_DIR / f"{a}.png" for a in args]
    else:
        targets = sorted(ART_DIR.glob("*-front.png"))
    if not targets:
        print("没找到立绘", file=sys.stderr)
        return 1
    for target in targets:
        if not target.exists():
            print(f"缺少 {target}", file=sys.stderr)
            return 1
        info = analyse(target)
        if info.get("empty"):
            print(f"{info['file']}: 空图")
            continue
        print(f"{info['file']}  {info['size'][0]}×{info['size'][1]}  轮廓框 {info['box']}")
        print(f"  各处宽度：" + "  ".join(f"{k}={v}px" for k, v in info["widths"].items()))
        for label, y, gap in info["gaps"]:
            print(f"  {label:>2} y={y:<5} 手臂间隙：{'x=' + str(gap) if gap is not None else '与身体相连'}")
        print(f"  腿缝：{'有' if info['leg_split'] else '无（下摆盖住腿）'}")
        print(f"  结论：{'手臂与躯干粘连 → 应少切（不做手臂独立关节）' if info['arms_fused'] else '手臂可独立'}")
        if preview:
            print("  预览：" + str(draw_preview(target, info).name))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
