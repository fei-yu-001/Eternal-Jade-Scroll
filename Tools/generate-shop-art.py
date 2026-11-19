#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAT-12 商铺立面变体生图：gpt-image 生图 -> cutout 抠底 -> 归一化 -> 量化验收。

与 MAP-14 的 Tools/generate-building-art.py 是同一套算法、同一套口径，只换了清单：

  1. 纯复用，不复制代码：importlib 载入 generate-building-art.py，直接调用它的
     generate() / do_cutout() / normalize() / judge() / nearest_size()。任何口径
     （CONTENT_H_RATIO=0.88、BOTTOM_MARGIN_PCT=1.0、ALPHA_T=128、PLAYER_HEIGHT=170）
     改动只需改一份，不存在两处漂移。
  2. 为什么另开文件而不往 generate-building-art.py 里加清单：那个脚本是 MAP-14 的
     验收留痕（它的 BUILDINGS 与 Tools/logs/map-14/ 的证据一一对应），往里面掺
     MAT-12 的清单会让两轮的验收记录混在一份 metrics 里。故清单分开、算法同源。
  3. 为什么不查 building-plots.json：这 6 张商铺是 C2「南街铺面连排」的**前置素材**，
     目标 plot 尚未入库，所以宽高比按 MAT-12 任务书直接写死在这里（SHOPS 表的
     expect_wh），不去读一个还不存在的 plot。

背景（MAT-12 任务书）：全城只有 shop-front / shop-front-v2 两张铺面，四家铺子排一起
会像复制粘贴。斐哥：「你要做商贩肯定不是这里只有一个商贩的」。

用法：
  python Tools/generate-shop-art.py --list
  python Tools/generate-shop-art.py cloth-shop                    # 只出布庄
  python Tools/generate-shop-art.py wine-tavern herbal-shop       # 出一张也可以给多张
  python Tools/generate-shop-art.py all --attempts 2              # 6 张，每张最多 2 次生图
  python Tools/generate-shop-art.py all --from-raw                # 只重跑抠底/归一，不重新生图
  python Tools/generate-shop-art.py all --verify                  # 只验收 candidates/ 里已有图

一次生图调用只出一张，通道串行；给多张名字时按表内顺序逐张串行跑，不等不并发。

纪律（斐哥铁律，2026-10-14）：生图通道串行，一次一张，不并发；每张生成后立即量化
验收，不合格才重试，不盲目多出图。
"""
import argparse
import importlib.util
import io
import json
import sys
from pathlib import Path

from PIL import Image

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "Tools"
LOG_DIR = TOOLS / "logs" / "mat-12"
RAW_DIR = LOG_DIR / "raw"
CAND_DIR = LOG_DIR / "candidates"
PROMPT_DIR = LOG_DIR / "prompts"


def load_pipeline():
    """载入 MAP-14 生图管线作为算法库（文件名带连字符，只能 importlib 载入）。"""
    spec = importlib.util.spec_from_file_location(
        "generate_building_art", TOOLS / "generate-building-art.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


G = load_pipeline()

# MAT-12 任务书加严的第三段（相机 + 背景 + 无字）。相对 G.CAM 的两处加严：
#   - "no ground shadow" -> "no scene, no ground surface and no cast shadow"（任务书明文）
#   - 补一句「一切幡/匾全空白」，因为本项目所有立绘的牌匾幌子一律不许带字，
#     而 SHOP 类主体（布幡/酒旗/药幌）比建筑更容易画出字来。
CAM = ("The structure stands fully visible, front three-quarter view, centered, complete silhouette "
       "with nothing cropped, flat uniform warm ivory background #EDE6D6 with no scene, no ground "
       "surface and no cast shadow. Soft diffuse light. Every hanging banner and every sign board is "
       "completely blank: plain cloth and bare wood with no characters, no writing and no lettering "
       "of any kind. No text, labels, logo, watermark, no modern elements.")

# C2「南街铺面连排」要用到的铺面变体。expect_wh 来自 MAT-12 任务书 / art-wishlist.md 的
# 「目标 plot」列；h_over_player 目标 = 0.88 * plot_h / 170（与 MAP-14 同一算式）。
SHOPS = [
    {
        "key": "cloth-shop",
        "plot": "plot-cloth-shop",
        "expect_wh": (300, 348),
        "subject": (
            "a single-storey Chinese cloth shop on a market street, a draper's shop selling woven "
            "cloth: a grey tiled roof with gently upturned eaves, an open timber post-and-beam shop "
            "front, a long dark wooden plank counter running across the whole width of the shop, bolts "
            "of freshly woven cloth rolled and stacked upright on the counter and on shelves behind it "
            "in indigo blue, madder red and undyed beige with faint woven stripes, a plain bamboo "
            "measuring rule and a folded length of fabric lying on the counter, a bare wooden rack "
            "holding sample swatches standing beside the door, a long blank undyed cloth banner hanging "
            "from a pole under the eaves. The shop is clearly taller than a standing adult: the doorway "
            "alone reaches about half of the whole building height and the roof ridge sits well above a "
            "person's head."),
    },
    {
        "key": "wine-tavern",
        "plot": "plot-wine-tavern",
        "expect_wh": (300, 348),
        "subject": (
            "a single-storey Chinese wine tavern on a market street: a grey tiled roof with gently "
            "upturned eaves, an open timber shop front with a low railing, rows of glazed and unglazed "
            "clay wine jars of different sizes standing on the ground and on stepped wooden shelves "
            "beside the door, a small round wine flag of plain undyed cloth hanging from a bamboo pole "
            "under the eaves, rough wooden tables and long benches set out in front, a shallow ceramic "
            "wine bowl and a bamboo ladle on the counter. The tavern is clearly taller than a standing "
            "adult: the doorway alone reaches about half of the whole building height and the roof "
            "ridge sits well above a person's head."),
    },
    {
        "key": "herbal-shop",
        "plot": "plot-herbal-shop",
        "expect_wh": (300, 348),
        "subject": (
            "a single-storey Chinese herbal medicine shop on a market street: a grey tiled roof with "
            "gently upturned eaves, an open timber shop front, a tall wall of small many-drawered "
            "apothecary cabinets visible behind the counter, wicker baskets and shallow trays of dried "
            "roots, bark and leaves lining the counter and the shelves, bundled herbs hanging in rows "
            "from a wooden rail under the eaves, a stone mortar and pestle on a low stool beside the "
            "door, a plain blank cloth awning sign hanging from a pole. The shop is clearly taller than "
            "a standing adult: the doorway alone reaches about half of the whole building height and "
            "the roof ridge sits well above a person's head."),
    },
    {
        "key": "general-store",
        "plot": "plot-general-store",
        "expect_wh": (300, 348),
        "subject": (
            "a single-storey Chinese general store on a market street, a village sundries shop: a grey "
            "tiled roof with gently upturned eaves, a wide open timber shop front, stacked bamboo "
            "baskets and woven hampers of many sizes piled on the ground and on shelves beside the "
            "door, a row of glazed clay jars and crocks along the counter, coils of hemp rope and "
            "bundles of straw cord hanging from the beam, a wooden rack of small everyday wares, a "
            "blank bare wooden sign board above the doorway. The shop is clearly taller than a standing "
            "adult: the doorway alone reaches about half of the whole building height and the roof "
            "ridge sits well above a person's head."),
    },
    {
        "key": "bun-shop",
        "plot": "plot-bun-shop",
        "expect_wh": (300, 348),
        "subject": (
            "a single-storey Chinese steamed bun shop and cooked food stall on a market street: a grey "
            "tiled roof with gently upturned eaves, an open timber shop front, a brick cooking stove "
            "with a round iron wok built into the shop front, tall bamboo steamers stacked in tiers on "
            "the stove with faint wisps of steam rising, freshly made buns arranged on a wide shallow "
            "tray, a wooden rack of bamboo steamer lids and a bundle of firewood under the counter, a "
            "plain blank cloth awning hanging from a pole. The shop is clearly taller than a standing "
            "adult: the doorway alone reaches about half of the whole building height and the roof "
            "ridge sits well above a person's head."),
    },
    {
        "key": "country-inn",
        "plot": "plot-country-inn",
        "expect_wh": (340, 420),
        "subject": (
            "a two-storey Chinese country inn on a market street: stacked grey tiled roofs with "
            "upturned eaves, a timber balcony with a railing running across the upper floor with "
            "lattice windows behind it, a deep open ground floor with thick timber posts, a courtyard "
            "gateway at one side leading to an inner yard, a plain blank cloth inn banner and two plain "
            "red lanterns hanging under the eaves, wooden tables and benches at the street front. The "
            "inn is clearly TALLER than a standing adult: the ground-floor doorway alone reaches about "
            "two thirds of the height of a person and the lower roof sits well above a person's head, "
            "with the upper roof ridge far above everything."),
    },
]

BY_KEY = {s["key"]: s for s in SHOPS}


def h_over_player_target(plot_w, plot_h):
    return round(0.88 * plot_h / G.PLAYER_HEIGHT, 3)


def build_prompt(shop):
    return G.STYLE + shop["subject"] + " " + G.FRAME_PORTRAIT + CAM


def render(shop, key, from_raw, attempts):
    plot_w, plot_h = shop["expect_wh"]
    canvas_h = G.CANVAS_H
    canvas_w = int(round(canvas_h * plot_w / plot_h))
    size = G.nearest_size(plot_w / plot_h)
    target = h_over_player_target(plot_w, plot_h)
    tag = shop["key"]
    raw_path = RAW_DIR / (tag + "-raw.png")
    cut_path = RAW_DIR / (tag + "-cut.png")
    out_path = CAND_DIR / (tag + ".png")
    prompt = build_prompt(shop)
    PROMPT_DIR.mkdir(parents=True, exist_ok=True)
    (PROMPT_DIR / (tag + ".txt")).write_text(prompt, encoding="utf-8")

    print("\n== %s  plot=%dx%d  宽高比=%.4f  -> 画布 %dx%d  生图尺寸=%s  h/player 目标 %.3f" % (
        tag, plot_w, plot_h, plot_w / plot_h, canvas_w, canvas_h, size, target))

    if from_raw:
        if not raw_path.exists():
            print("  跳过（--from-raw 但缺 %s）" % raw_path.name)
            return None
        G.do_cutout(raw_path, cut_path)
        used_size = None
        used = 0
    else:
        # 生图非确定性：同一 prompt 每次构图都不同。构图比画布宽就必须横裁建筑本体，
        # 所以重试到出现「不用横裁」的构图为止（有界），取横裁比例最小的那次。
        best = None
        for attempt in range(1, attempts + 1):
            blob = G.generate(prompt, size, key)
            tmp_raw = RAW_DIR / ("%s-attempt%d-raw.png" % (tag, attempt))
            tmp_cut = RAW_DIR / ("%s-attempt%d-cut.png" % (tag, attempt))
            image = Image.open(io.BytesIO(blob))
            if image.mode != "RGB":
                image = image.convert("RGB")
            image.save(tmp_raw)
            G.do_cutout(tmp_raw, tmp_cut)
            loss = G.crop_fraction(tmp_cut, canvas_w, canvas_h)
            touched = G.edge_touching(tmp_cut)
            print("  第 %d 次生图(%s) %s  %d bytes  横裁 %s%%%s" % (
                attempt, size, image.size, len(blob), round(loss * 100, 1),
                "  构图溢出画布（内容贴边）" if touched else ""))
            rank = (1 if touched else 0, loss)
            if best is None or rank < best[0]:
                best = (rank, attempt, tmp_raw, tmp_cut, list(image.size), size)
            if not touched and loss == 0.0:
                break
        rank, used, tmp_raw, tmp_cut, raw_size, used_size = best
        for attempt in range(1, attempts + 1):
            if attempt == used:
                continue
            for suffix in ("-raw.png", "-cut.png"):
                (RAW_DIR / ("%s-attempt%d%s" % (tag, attempt, suffix))).unlink(missing_ok=True)
        tmp_raw.replace(raw_path)
        tmp_cut.replace(cut_path)
        print("  采用第 %d 次（%s，横裁 %s%%）" % (used, used_size, round(rank[1] * 100, 1)))

    record = G.normalize(cut_path, out_path, canvas_w, canvas_h)
    record["id"] = tag
    record["plot"] = shop["plot"]
    record["raw_size"] = list(Image.open(raw_path).size)
    record["gen_size"] = used_size if not from_raw else size
    record["attempts_used"] = used
    record["prompt"] = prompt
    G.judge(record, plot_w, plot_h, target)
    print("  抠底内容 %s%%x%s%% -> 最终占高 %s%%  贴底 %s%%  横裁 %s%%  %s" % (
        record["cut_bbox_w_pct"], record["cut_bbox_h_pct"], record["final_bbox_h_pct"],
        record["final_bottom_pct"], record["content_cropped_pct"],
        "PASS" if record["pass"] else "FAIL " + str([k for k, v in record["checks"].items() if not v])))
    return record


def verify(shop):
    plot_w, plot_h = shop["expect_wh"]
    path = CAND_DIR / (shop["key"] + ".png")
    if not path.exists():
        print("  缺候选图 %s" % path)
        return None
    canvas = Image.open(path).convert("RGBA")
    bx0, by0, bx1, by1 = G.bbox_of_alpha(canvas)
    width, height = canvas.size
    cut_path = RAW_DIR / (shop["key"] + "-cut.png")
    cut = Image.open(cut_path).convert("RGBA") if cut_path.exists() else None
    record = {
        "id": shop["key"], "plot": shop["plot"], "canvas": [width, height],
        "final_bbox": [bx0, by0, bx1, by1],
        "final_bbox_w_pct": round((bx1 - bx0 + 1) / width * 100, 1),
        "final_bbox_h_pct": round((by1 - by0 + 1) / height * 100, 1),
        "final_top_pct": round(by0 / height * 100, 1),
        "final_bottom_pct": round((by1 + 1) / height * 100, 1),
        "corners_alpha": G.corners_alpha(canvas),
        "cut_border_alpha_max": G.border_alpha_max(cut) if cut else 0,
        "content_cropped_px": 0, "content_cropped_pct": 0.0,
        "cut_size": list(cut.size) if cut else [width, height],
        "cut_bbox": list(G.bbox_of_alpha(cut)) if cut else [bx0, by0, bx1, by1],
        "cut_bbox_w_pct": round((G.bbox_of_alpha(cut)[2] - G.bbox_of_alpha(cut)[0] + 1) / cut.size[0] * 100, 1) if cut else 0.0,
        "cut_bbox_h_pct": round((G.bbox_of_alpha(cut)[3] - G.bbox_of_alpha(cut)[1] + 1) / cut.size[1] * 100, 1) if cut else 0.0,
        "cut_corners_alpha": G.corners_alpha(cut) if cut else [0, 0, 0, 0],
        "raw_size": [0, 0], "gen_size": G.nearest_size(plot_w / plot_h),
        "prompt": "", "attempts_used": 0,
    }
    G.judge(record, plot_w, plot_h, h_over_player_target(plot_w, plot_h))
    print("  %-16s 画布 %s  占高 %s%%  贴底 %s%%  h/player %.3f  %s" % (
        shop["key"], record["canvas"], record["final_bbox_h_pct"], record["final_bottom_pct"],
        record["h_over_player"], "PASS" if record["pass"] else "FAIL"))
    return record


def main():
    parser = argparse.ArgumentParser(
        description="MAT-12 商铺立面生图管线（算法同源于 MAP-14 generate-building-art.py）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="可出的商铺：%s" % ", ".join(s["key"] for s in SHOPS))
    parser.add_argument("shops", nargs="*", default=None,
                        help="商铺短名，可给多个（%s）；留空或 all 表示全部" % "/".join(s["key"] for s in SHOPS))
    parser.add_argument("--from-raw", action="store_true", help="跳过生图，只用 raw/ 里已有的原图重跑抠底与归一")
    parser.add_argument("--attempts", type=int, default=3, help="每张最多生图几次（取横裁最少的一次，默认 3）")
    parser.add_argument("--verify", action="store_true", help="只对 candidates/ 里已有图做验收，不生成")
    parser.add_argument("--list", action="store_true", help="只列出清单与目标尺寸，不动通道")
    parser.add_argument("--metrics", default=str(LOG_DIR / "mat-12-metrics.json"), help="量化记录落盘路径")
    args = parser.parse_args()

    if args.list:
        for s in SHOPS:
            w, h = s["expect_wh"]
            print("%-16s %s  %dx%d  h/player %.3f" % (s["key"], s["plot"], w, h, h_over_player_target(w, h)))
        return 0

    names = args.shops or ["all"]
    if names == ["all"]:
        targets = SHOPS
    else:
        unknown = [n for n in names if n not in BY_KEY]
        if unknown:
            raise SystemExit("unknown shop %s; 可选：%s" % (
                ", ".join(unknown), ", ".join(s["key"] for s in SHOPS)))
        targets = [BY_KEY[n] for n in names]

    for directory in (RAW_DIR, CAND_DIR, PROMPT_DIR):
        directory.mkdir(parents=True, exist_ok=True)

    records = []
    if args.verify:
        for shop in targets:
            record = verify(shop)
            if record:
                records.append(record)
    else:
        key = G.read_key()
        for shop in targets:
            record = render(shop, key, args.from_raw, args.attempts)
            if record:
                records.append(record)

    if records:
        Path(args.metrics).write_text(
            json.dumps({"content_h_ratio": G.CONTENT_H_RATIO, "canvas_h": G.CANVAS_H,
                        "alpha_threshold": G.ALPHA_T, "player_height": G.PLAYER_HEIGHT,
                        "source": "MAT-12 商铺立面变体（算法同源 Tools/generate-building-art.py）",
                        "candidates": records}, ensure_ascii=False, indent=2), encoding="utf-8")
        print("\n量化记录 -> %s" % args.metrics)

    failed = [r["id"] for r in records if not r["pass"]]
    print("\n候选 %d 张，PASS %d，FAIL %d %s" % (
        len(records), len(records) - len(failed), len(failed), failed if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
