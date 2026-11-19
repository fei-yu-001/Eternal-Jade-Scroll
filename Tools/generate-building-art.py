#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAP-14 建筑专属立绘生图管线：gpt-image 生图 -> cutout 抠底 -> 归一化 -> 量化验收。

链路（架构师 2026-10-14 复工令指定，生图后端已从 grok 换成 hub.oaifree.com 的 gpt-image-2.5）：
  1. POST https://hub.oaifree.com/v1/images/generations（model=gpt-image-2.5，
     size=1024x1536 竖幅 / 1024x1024 方幅，background=transparent）出宣纸底立绘；
     网关透传 background 失败时仍返 RGB，交抠底管线，不影响下游；
  2. 复用 Tools/cutout-props.py 的抠底算法（四角 8px 均值取底色 + 边缘连通泛洪 + 羽化），
     不复制它的代码，直接 importlib 载入调用，保证与仓内既有抠图口径逐字一致；
  3. 归一到「画布宽高比 == 目标 plot 宽高比」，内容 bbox(alpha>128) 占画布高 88%±3%
     —— 这是 MAP-16 定标前提：h_over_player = 0.88 * plot.height / 170；
  4. 量化验收：四角 alpha==0 / 内容占高 / 画布宽高比 / h_over_player。

为什么不用 /v1/images/edits 锁相机：实测该通道对 prompt 与参考图都不响应（10 个语义完全
不同的 prompt 落在 5 个固定构图上；同一 payload 连跑 3 次得到 3 张不同的图；架构师亲跑的
Tools/logs/map-13/edit-test.png 与本脚本用「纯绿色方块」当参考图的输出逐像素完全相同）。
证据见 Tools/logs/map-14/channel-edits-broken.md。所以本轮改用「文生图 + 强构图约束 +
确定性裁剪/缩放」把 88% 占高钉死，不靠生图碰运气。

用法：
  python Tools/generate-building-art.py --help
  python Tools/generate-building-art.py                      # 6 件，每件 2 张候选
  python Tools/generate-building-art.py teahouse             # 只跑一件
  python Tools/generate-building-art.py teahouse --from-raw  # 只重跑抠底/归一，不重新生图
  python Tools/generate-building-art.py --verify             # 只对已有候选做验收，不出图
"""
import argparse
import base64
import io
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from urllib.request import ProxyHandler, build_opener

from PIL import Image

# Windows GBK 控制台下 print 中文会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "Tools"
LOG_DIR = TOOLS / "logs" / "map-14"
RAW_DIR = LOG_DIR / "raw"
CAND_DIR = LOG_DIR / "candidates"
PROMPT_DIR = LOG_DIR / "prompts"
PLOTS_JSON = REPO / "GameClient" / "TwelveJade" / "Assets" / "Resources" / "Config" / "building-plots.json"

GATEWAY = "https://hub.oaifree.com"
# 网关密钥只从环境变量读（2026-10-31 青岩：原先明文硬编码在此，已移除；
# 该 key 已进本地 git 历史 4bb363c，按主架构师裁决走"网关后台作废"，见 Tools/check-no-secrets.py）。
KEY_ENV = "OAIFREE_API_KEY"
MODEL = "gpt-image-2.5"

CANVAS_H = 1024          # 归一化画布高（像素），宽按 plot 宽高比推
CONTENT_H_RATIO = 0.88   # 内容 bbox(alpha>128) 占画布高
RATIO_TOLERANCE = 0.03   # 88% ± 3%
BOTTOM_MARGIN_PCT = 1.0  # 竖直贴底：底边只留 1% 安全边（MAP-23，对齐 Art/Props 旧资产 95.9%~99.2% 口径）
BOTTOM_TOLERANCE = 1.0   # bottom_pct 目标 99%，±1%
ALPHA_T = 128
PLAYER_HEIGHT = 170.0    # FrontEndWorld.TravelerHeight

# 三段式 prompt 的第一段：逐字沿用 ArtSource/prompts/shop-front-prop-v1.txt 的 Asset type 锚点。
STYLE = ("Asset type: a single hand-painted game prop illustration, fine ink linework with soft "
         "watercolor washes on warm ivory paper, muted natural colors, consistent with an elegant "
         "hand-painted historical Chinese indie game set in the late Yuan / early Ming era. Subject: ")

# 第三段：相机语言逐字沿用现有 *-prop-v1.txt（不许另发明角度）。
CAM = ("The structure stands fully visible, front three-quarter view, centered, complete silhouette "
       "with nothing cropped, flat uniform warm ivory background #EDE6D6, no scene, no ground shadow. "
       "Soft diffuse light. No text, labels, logo, watermark, no modern elements.")

# 构图画布约束：夹在 Subject 与 CAM 之间。目的是让模型把主体画成目标画布的形状，
# 从而在「按 88% 占高缩放」时不必横向裁掉建筑本体。
FRAME_PORTRAIT = ("Vertical portrait composition: the structure is clearly TALLER than it is wide and "
                  "fills the frame from the very top edge to the very bottom edge, the ridge of the roof "
                  "nearly touching the top of the image and the base resting on the bottom of the image. ")
FRAME_PORTRAIT_NARROW = ("Vertical portrait composition: the structure is MUCH taller than it is wide, "
                         "roughly twice as tall as wide, and fills the frame from the very top edge to "
                         "the very bottom edge, the ridge of the roof nearly touching the top of the "
                         "image and the base resting on the bottom of the image. ")
FRAME_COMPACT = ("Compact composition: the structure fills the frame from the left edge to the right edge "
                 "and from the top edge to the bottom edge, its outermost parts reaching close to all "
                 "four edges of the image. ")

BUILDINGS = [
    {
        "key": "herbal-garden",
        "plot": "plot-herbal-garden",
        "expect_wh": (300, 290),
        "target_h_over_player": 1.50,
        "frame": FRAME_PORTRAIT,
        "subjects": [
            "a Chinese herbal medicine courtyard: a low white plastered garden wall with dark timber "
            "framing enclosing a yard of wooden drying racks hung with bundled herbs, stacked herb "
            "plaques and shallow sieves, stone steps up to a small tiled gatehouse.",
            "a walled Chinese herb-drying yard: a grey tiled gatehouse with upturned eaves at one side, "
            "drying racks and hanging herb bundles filling the yard, wicker baskets and clay jars of "
            "medicinal roots lined along the wall.",
        ],
    },
    {
        "key": "stone-house",
        "plot": "plot-stone-house",
        "expect_wh": (260, 309),
        "target_h_over_player": 1.60,
        "frame": FRAME_PORTRAIT,
        "subjects": [
            "a tall Chinese stone masonry house: grey rough-hewn stone walls, a double-pitched grey "
            "tiled roof with upturned eaves, a high stone door opening taller than a person, one small "
            "barred window.",
            "a sturdy two-storey Chinese stone dwelling: coursed grey stone blocks on the lower floor, "
            "white plastered upper floor with dark timber framing, grey tiled gable roof, wooden "
            "shutters and a deep stone threshold.",
        ],
    },
    {
        "key": "blacksmith",
        "plot": "plot-blacksmith",
        "expect_wh": (300, 348),
        "target_h_over_player": 1.80,
        "frame": FRAME_PORTRAIT,
        "subjects": [
            "a Chinese blacksmith's workshop: an open forge glowing inside, a tall soot-blackened brick "
            "chimney rising past the roof, a heavy iron anvil on a stump and a quench trough in front, "
            "eaves darkened by smoke.",
            "a village iron-smithy: a timber-framed open front with a wide tiled roof, a stone forge "
            "hearth with bellows, racks of tongs and hammered iron tools on the wall, a tall chimney at "
            "the gable end, barrels of water for quenching.",
        ],
    },
    {
        "key": "teahouse",
        "plot": "plot-teahouse",
        "expect_wh": (240, 386),
        "target_h_over_player": 2.00,
        "frame": FRAME_PORTRAIT_NARROW,
        "subjects": [
            "a tall narrow two-storey Chinese teahouse: a slender vertical facade with many open "
            "lattice windows on both floors, a cloth tea banner hanging from a pole under the eaves, "
            "wooden balconies and long benches at the door. The building must be clearly TALLER than a "
            "standing adult: the doorway alone reaches about one third of the whole building height, "
            "and the ridge sits well above a person's head.",
            "a two-storey riverside Chinese tea house: stacked tiled roofs with upturned eaves, a "
            "galleries balcony running across the upper floor, a hanging tea banner and a lantern by "
            "the door, tea tables and stools at the street front. The building must be clearly TALLER "
            "than a standing adult: the ridge sits well above a person's head.",
        ],
    },
    {
        "key": "temple-gate",
        "plot": "plot-temple-gate",
        "expect_wh": (300, 415),
        "target_h_over_player": 2.15,
        "frame": FRAME_PORTRAIT,
        "subjects": [
            "a Chinese temple mountain gate shanmen: a tall red plastered gate wall pierced by a "
            "studded timber doorway, a deep dark grey tiled roof with strongly upturned eaves, painted "
            "bracket sets under the eaves, a small bronze incense burner on the steps. The gate is "
            "clearly TALLER than a standing adult: the ridge sits well above a person's head.",
            "a tall Buddhist temple gate: thick red walls with a high stone plinth, three studded "
            "wooden door leaves, a broad tiled roof with upturned corners and painted beams, two stone "
            "guardian posts flanking the steps, a bronze censer in front. The gate is clearly TALLER "
            "than a standing adult: the ridge sits well above a person's head.",
        ],
    },
    {
        "key": "paifang",
        "plot": "plot-paifang",
        "expect_wh": (340, 541),
        "target_h_over_player": 2.80,
        "frame": FRAME_PORTRAIT_NARROW,
        "subjects": [
            "a tall narrow Chinese paifang memorial archway with three openings: very tall slender "
            "stone pillars carrying tiered green-grey tiled roofs with upturned eaves, carved stone "
            "plinths at the bases, painted bracket sets. The archway is much TALLER than a standing "
            "adult: the highest roof sits far above a person's head.",
            "a slender ceremonial Chinese stone archway: two very tall carved stone columns with a "
            "central opening and two narrow side openings, three tiers of tiled roofs with upturned "
            "eaves, a carved stone lintel and drum stones at the feet. The archway is much TALLER than "
            "a standing person.",
        ],
    },
]

BY_KEY = {b["key"]: b for b in BUILDINGS}


# ---------------------------------------------------------------- 基础设施

def read_key():
    """oaifree key：只认环境变量 OAIFREE_API_KEY，没有就报错退出。key 从不打印、从不写日志。"""
    key = (os.environ.get(KEY_ENV) or "").strip()
    if len(key) < 10:
        raise SystemExit("no oaifree key: set %s" % KEY_ENV)
    return key


def load_cutout():
    """载入仓内既有抠图模块（文件名带连字符，只能 importlib 载入，不改它）。"""
    import importlib.util
    spec = importlib.util.spec_from_file_location("cutout_props", TOOLS / "cutout-props.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_plot_size(plot_id):
    data = json.loads(PLOTS_JSON.read_text(encoding="utf-8"))
    for plot in data["plots"]:
        if plot.get("id") == plot_id:
            return int(plot["width"]), int(plot["height"])
    raise SystemExit("plot not found in building-plots.json: %s" % plot_id)


def nearest_size(canvas_aspect):
    """新通道只有两个尺寸：方幅 1024x1024、竖幅 1024x1536。宽高比 >=1 用方幅，<1 用竖幅。"""
    return "1024x1024" if canvas_aspect >= 1.0 else "1024x1536"


def generate(prompt, size, key, attempts=6):
    payload = {"model": MODEL, "prompt": prompt, "n": 1,
               "size": size, "background": "transparent", "output_format": "png"}
    body = json.dumps(payload).encode("utf-8")
    opener = build_opener(ProxyHandler({}))   # 必须绕代理，否则连不上本机网关
    last = None
    for attempt in range(attempts):
        request = urllib.request.Request(
            GATEWAY + "/v1/images/generations", data=body,
            headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
        try:
            raw = opener.open(request, timeout=300).read()
        except urllib.error.HTTPError as err:
            last = "HTTP %d %s" % (err.code, err.read()[:200].decode("utf-8", "replace"))
            # 429 限流与 5xx（上游偶发 502）都是瞬时的，等 30s 重试；4xx 是请求本身的问题，直接抛。
            if err.code == 429 or err.code >= 500:
                print("      网关 %d，等 30s 重试（第 %d/%d 次）" % (err.code, attempt + 1, attempts))
                time.sleep(30)
                continue
            raise SystemExit("generate failed: " + last)
        item = json.loads(raw)["data"][0]
        return base64.b64decode(item["b64_json"])
    raise SystemExit("generate exhausted retries: " + str(last))


def bbox_of_alpha(image):
    """内容包围盒：alpha > ALPHA_T。返回 (x0, y0, x1, y1)（闭区间像素坐标）。"""
    alpha = image.getchannel("A")
    mask = alpha.point(lambda v: 255 if v > ALPHA_T else 0)
    box = mask.getbbox()
    if box is None:
        raise SystemExit("cutout produced an empty image (no pixel above alpha %d)" % ALPHA_T)
    return box[0], box[1], box[2] - 1, box[3] - 1


def corners_alpha(image):
    width, height = image.size
    return [image.getpixel(p)[3] for p in
            ((0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1))]


def border_alpha_max(image, ring=2):
    """最外 ring 圈像素的最大 alpha：抠底干净则应为 0。"""
    alpha = image.getchannel("A")
    width, height = image.size
    top = alpha.crop((0, 0, width, ring))
    bottom = alpha.crop((0, height - ring, width, height))
    left = alpha.crop((0, 0, ring, height))
    right = alpha.crop((width - ring, 0, width, height))
    return max(band.getextrema()[1] for band in (top, bottom, left, right))


# ---------------------------------------------------------------- 抠底 + 归一

def do_cutout(raw_path, cut_path):
    """调用仓内既有抠图模块。"""
    load_cutout().cutout(Path(raw_path), Path(cut_path))


def content_box(cut_path):
    cut = Image.open(cut_path).convert("RGBA")
    return cut, bbox_of_alpha(cut)


def edge_touching(cut_path):
    """生图构图是否溢出画布：抠底图最外 ring 圈仍有不透明像素即视为被裁。

    MAT-12 抓到的漏判：布庄第一次生图右边缘残留 44px 高的深棕色内容（建筑本体，
    非宣纸底色），cut_border_alpha_max=255、右净边 0px，而旧 judge() 只看四角与
    占高，判它 PASS。MAP-14 那 12 张存量 border_alpha_max 全为 0、净边最少 8px，
    所以加这条判据不会让任何既有候选变红——是补漏，不是放宽。
    """
    return border_alpha_max(Image.open(cut_path).convert("RGBA")) > 0


def crop_fraction(cut_path, canvas_w, canvas_h):
    """按 88% 占高缩放后，内容宽度超出画布的比例（0 表示不用横裁）。

    与 normalize() 里的横裁量同一口径：裁掉的是内容宽度的这个比例。
    """
    cut, (x0, y0, x1, y1) = content_box(cut_path)
    content_w, content_h = x1 - x0 + 1, y1 - y0 + 1
    scaled_w = content_w * (CONTENT_H_RATIO * canvas_h) / content_h
    return max(0.0, 1.0 - canvas_w / scaled_w)


def normalize(cut_path, out_path, canvas_w, canvas_h):
    """抠底图 -> 按 88% 占高缩放 -> 贴到目标宽高比画布（竖直贴底，留 1% 安全边）。

    为什么贴底而不是居中：PlaceInWorld 用 pivot=(0.5,1) 且调用方传 foot.y - height，
    画布底边就是地面（footY）；而 collisionDepth = bottom_pct × plot.height，内容的
    竖直位置直接决定物件落在 plot 里的位置。竖直居中会让建筑浮空 bottom_margin ×
    plot.height（茶馆 386 浮 23.5px、牌坊 541 浮 33px，玩家才 170px 高）。
    Art/Props 里 15 件旧资产的 bottom 全在 95.9%~99.2%（贴底），这里对齐同一口径。
    """
    cut, (x0, y0, x1, y1) = content_box(cut_path)
    content = cut.crop((x0, y0, x1 + 1, y1 + 1))
    src_w, src_h = content.size
    original_w = src_w

    target_h = int(round(CONTENT_H_RATIO * canvas_h))
    scale = target_h / src_h
    scaled_w = int(round(src_w * scale))

    cropped_px = 0
    if scaled_w > canvas_w:
        # 内容比画布宽：按目标画布宽反推可保留的源宽度，居中裁掉两侧。
        keep_src_w = canvas_w / scale
        left = x0 + (src_w - keep_src_w) / 2.0
        content = cut.crop((int(round(left)), y0, int(round(left + keep_src_w)), y1 + 1))
        cropped_px = src_w - content.size[0]
        src_w, src_h = content.size
        scale = target_h / src_h
        scaled_w = min(canvas_w, int(round(src_w * scale)))

    scaled_h = int(round(src_h * scale))
    resized = content.resize((scaled_w, scaled_h), Image.LANCZOS)

    # 竖直贴底：底边对齐画布底边，只留 1% 安全边（MAP-23 修浮空）。
    margin_y = int(round(0.01 * canvas_h))
    canvas = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))
    canvas.paste(resized, ((canvas_w - scaled_w) // 2, canvas_h - scaled_h - margin_y), resized)
    canvas.save(out_path)

    bx0, by0, bx1, by1 = bbox_of_alpha(canvas)
    return {
        "cut_size": list(cut.size),
        "cut_bbox": [x0, y0, x1, y1],
        "cut_bbox_w_pct": round((x1 - x0 + 1) / cut.size[0] * 100, 1),
        "cut_bbox_h_pct": round((y1 - y0 + 1) / cut.size[1] * 100, 1),
        "cut_border_alpha_max": border_alpha_max(cut),
        "cut_corners_alpha": corners_alpha(cut),
        "content_cropped_px": cropped_px,
        "content_cropped_pct": round(cropped_px / original_w * 100, 1) if cropped_px else 0.0,
        "canvas": [canvas_w, canvas_h],
        "final_bbox": [bx0, by0, bx1, by1],
        "final_bbox_w_pct": round((bx1 - bx0 + 1) / canvas_w * 100, 1),
        "final_bbox_h_pct": round((by1 - by0 + 1) / canvas_h * 100, 1),
        "final_top_pct": round(by0 / canvas_h * 100, 1),
        "final_bottom_pct": round((by1 + 1) / canvas_h * 100, 1),
        "corners_alpha": corners_alpha(canvas),
    }


def judge(record, plot_w, plot_h, target_h_over_player):
    canvas_w, canvas_h = record["canvas"]
    aspect_err = abs((canvas_w / canvas_h) / (plot_w / plot_h) - 1.0)
    h_pct = record["final_bbox_h_pct"] / 100.0
    h_over_player = h_pct * plot_h / PLAYER_HEIGHT
    checks = {
        "corners_alpha_zero": all(a == 0 for a in record["corners_alpha"]),
        "cutout_corners_zero": all(a == 0 for a in record["cut_corners_alpha"]),
        # 生图构图不许溢出画布：内容贴到抠底图最外圈 = 建筑本体被裁（"nothing cropped"）。
        "cutout_border_clean": record.get("cut_border_alpha_max", 0) == 0,
        "height_ratio_ok": abs(h_pct - CONTENT_H_RATIO) <= RATIO_TOLERANCE,
        "bottom_ratio_ok": abs(record["final_bottom_pct"] - 100.0 + BOTTOM_MARGIN_PCT) <= BOTTOM_TOLERANCE,
        "canvas_aspect_ok": aspect_err <= 0.005,
    }
    record["canvas_aspect"] = round(canvas_w / canvas_h, 4)
    record["plot_aspect"] = round(plot_w / plot_h, 4)
    record["h_over_player"] = round(h_over_player, 3)
    record["target_h_over_player"] = target_h_over_player
    record["collisionTop_proposal"] = int(round(record["final_top_pct"] / 100.0 * plot_h))
    record["collisionDepth_proposal"] = int(round(record["final_bottom_pct"] / 100.0 * plot_h))
    record["checks"] = checks
    record["pass"] = all(checks.values())
    return record


def measure_candidate(tag, plot, path, plot_w, plot_h, target_h_over_player, cut_path=None):
    """对已落盘的候选图做验收测量（--verify 用，不重新出图）。"""
    canvas = Image.open(path).convert("RGBA")
    bx0, by0, bx1, by1 = bbox_of_alpha(canvas)
    width, height = canvas.size
    cut = Image.open(cut_path).convert("RGBA") if cut_path and Path(cut_path).exists() else None
    record = {
        "id": tag, "plot": plot, "canvas": [width, height],
        "final_bbox": [bx0, by0, bx1, by1],
        "final_bbox_w_pct": round((bx1 - bx0 + 1) / width * 100, 1),
        "final_bbox_h_pct": round((by1 - by0 + 1) / height * 100, 1),
        "final_top_pct": round(by0 / height * 100, 1),
        "final_bottom_pct": round((by1 + 1) / height * 100, 1),
        "corners_alpha": corners_alpha(canvas),
        "cut_border_alpha_max": border_alpha_max(cut) if cut else 0,
        "content_cropped_px": 0, "content_cropped_pct": 0.0,
        "cut_size": list(cut.size) if cut else [width, height],
        "cut_bbox": list(bbox_of_alpha(cut)) if cut else [bx0, by0, bx1, by1],
        "cut_bbox_w_pct": round((bbox_of_alpha(cut)[2] - bbox_of_alpha(cut)[0] + 1) / cut.size[0] * 100, 1) if cut else 0.0,
        "cut_bbox_h_pct": round((bbox_of_alpha(cut)[3] - bbox_of_alpha(cut)[1] + 1) / cut.size[1] * 100, 1) if cut else 0.0,
        "cut_corners_alpha": corners_alpha(cut) if cut else [0, 0, 0, 0],
        "raw_size": [0, 0], "gen_size": nearest_size(plot_w / plot_h), "prompt": "",
    }
    return judge(record, plot_w, plot_h, target_h_over_player)


def verify_only(targets, records):
    """--verify：只对已落盘的候选做验收，不出图。"""
    for building in targets:
        plot_w, plot_h = read_plot_size(building["plot"])
        for variant in range(len(building["subjects"])):
            tag = "%s-v%d" % (building["key"], variant + 1)
            path = CAND_DIR / (tag + ".png")
            if not path.exists():
                print("  %-16s 缺候选图 %s" % (tag, path))
                continue
            record = measure_candidate(tag, building["plot"], path, plot_w, plot_h,
                                       building["target_h_over_player"], RAW_DIR / (tag + "-cut.png"))
            records.append(record)
            print("  %-16s 画布 %s  最终 bbox 占高 %s%%  h_over_player %.3f (目标 %.2f)  %s" % (
                tag, record["canvas"], record["final_bbox_h_pct"], record["h_over_player"],
                building["target_h_over_player"], "PASS" if record["pass"] else "FAIL"))
    return records


# ---------------------------------------------------------------- 主流程

def build_prompt(building, variant):
    return STYLE + building["subjects"][variant] + " " + building["frame"] + CAM


def render_variant(building, variant, key, canvas_w, canvas_h, size, attempts, from_raw, plot_w, plot_h):
    """一件建筑的一个候选：生图（必要时有界重试取最窄构图）-> 抠底 -> 归一。"""
    tag = "%s-v%d" % (building["key"], variant + 1)
    raw_path = RAW_DIR / (tag + "-raw.png")
    cut_path = RAW_DIR / (tag + "-cut.png")
    out_path = CAND_DIR / (tag + ".png")
    prompt = build_prompt(building, variant)
    (PROMPT_DIR / (tag + ".txt")).write_text(prompt, encoding="utf-8")

    if from_raw:
        if not raw_path.exists():
            print("  %-16s 跳过（--from-raw 但缺 %s）" % (tag, raw_path.name))
            return None
        do_cutout(raw_path, cut_path)
        used = 0
    else:
        # 生图非确定性：同一 prompt 每次构图都不同。构图比画布宽就必须横裁建筑本体，
        # 所以重试到出现「不用横裁」的构图为止（有界，最多 attempts 次），取横裁比例最小的那次。
        best = None
        for attempt in range(1, attempts + 1):
            blob = generate(prompt, size, key)
            tmp_raw = RAW_DIR / ("%s-attempt%d-raw.png" % (tag, attempt))
            tmp_cut = RAW_DIR / ("%s-attempt%d-cut.png" % (tag, attempt))
            image = Image.open(io.BytesIO(blob))
            if image.mode != "RGB":
                image = image.convert("RGB")
            image.save(tmp_raw)       # 上游可能透传失败仍返 RGB，无损重存为 PNG，交抳底管线
            do_cutout(tmp_raw, tmp_cut)
            loss = crop_fraction(tmp_cut, canvas_w, canvas_h)
            touched = edge_touching(tmp_cut)
            print("  %-16s 第 %d 次生图(%s) %s  %d bytes  横裁 %s%%%s" % (
                tag, attempt, size, image.size, len(blob), round(loss * 100, 1),
                "  构图溢出画布（内容贴边）" if touched else ""))
            # 排序：先要不贴边的构图，再要横裁少的。贴边 = 建筑被裁，比横裁更致命。
            rank = (1 if touched else 0, loss)
            if best is None or rank < best[0]:
                best = (rank, attempt, tmp_raw, tmp_cut, list(image.size), size)
            if not touched and loss == 0.0:
                break
        rank, used, tmp_raw, tmp_cut, raw_size, used_size = best  # noqa: F841  (rank 仅用于排序)
        for attempt in range(1, attempts + 1):
            if attempt == used:
                continue
            for suffix in ("-raw.png", "-cut.png"):
                (RAW_DIR / ("%s-attempt%d%s" % (tag, attempt, suffix))).unlink(missing_ok=True)
        tmp_raw.replace(raw_path)
        tmp_cut.replace(cut_path)
        print("  %-16s 采用第 %d 次（%s，横裁 %s%%）" % (tag, used, used_size, round(rank[1] * 100, 1)))

    record = normalize(cut_path, out_path, canvas_w, canvas_h)
    record["id"] = tag
    record["plot"] = building["plot"]
    record["raw_size"] = list(Image.open(raw_path).size)
    record["gen_size"] = used_size if from_raw is False else size
    record["attempts_used"] = used
    record["prompt"] = prompt
    judge(record, plot_w, plot_h, building["target_h_over_player"])
    print("  %-16s 抠底内容 %s%%x%s%% -> 最终占高 %s%%  横裁 %s%%  %s" % (
        tag, record["cut_bbox_w_pct"], record["cut_bbox_h_pct"],
        record["final_bbox_h_pct"], record["content_cropped_pct"],
        "PASS" if record["pass"] else "FAIL " + str([k for k, v in record["checks"].items() if not v])))
    return record


def run_one(building, key, from_raw, attempts, out):
    plot_w, plot_h = read_plot_size(building["plot"])
    if (plot_w, plot_h) != building["expect_wh"]:
        print("  !! %s 的 plot 尺寸是 %dx%d，规格表期望 %s" %
              (building["plot"], plot_w, plot_h, building["expect_wh"]))
    canvas_h = CANVAS_H
    canvas_w = int(round(canvas_h * plot_w / plot_h))
    size = nearest_size(plot_w / plot_h)
    print("\n== %s  plot=%s %dx%d  画布宽高比=%.3f  -> 画布 %dx%d  生图尺寸=%s" %
          (building["key"], building["plot"], plot_w, plot_h, plot_w / plot_h, canvas_w, canvas_h, size))

    for variant in range(len(building["subjects"])):
        record = render_variant(building, variant, key, canvas_w, canvas_h,
                                size, attempts, from_raw, plot_w, plot_h)
        if record:
            out.append(record)
    return out


def main():
    parser = argparse.ArgumentParser(
        description="MAP-14 建筑立绘管线：gpt-image 生图 -> cutout 抠底 -> 归一化(88% 占高) -> 量化验收",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="可跑的建筑：%s" % ", ".join(b["key"] for b in BUILDINGS))
    parser.add_argument("building", nargs="?", default="all",
                        help="建筑短名（%s）或 all" % "/".join(b["key"] for b in BUILDINGS))
    parser.add_argument("--from-raw", action="store_true", help="跳过生图，只用 raw/ 里已有的原图重跑抠底与归一")
    parser.add_argument("--attempts", type=int, default=3, help="每个候选最多生图几次（取横裁最少的一次，默认 3）")
    parser.add_argument("--verify", action="store_true", help="只对 candidates/ 里已有候选做验收，不生成")
    parser.add_argument("--metrics", default=str(LOG_DIR / "map-14-metrics.json"), help="量化记录落盘路径")
    args = parser.parse_args()

    for directory in (RAW_DIR, CAND_DIR, PROMPT_DIR):
        directory.mkdir(parents=True, exist_ok=True)

    if args.verify:
        targets = BUILDINGS if args.building == "all" else [BY_KEY[args.building]]
        records = verify_only(targets, [])
    else:
        if args.building != "all" and args.building not in BY_KEY:
            raise SystemExit("unknown building %r; 可选：%s" % (
                args.building, ", ".join(b["key"] for b in BUILDINGS)))
        key = read_key()
        targets = BUILDINGS if args.building == "all" else [BY_KEY[args.building]]
        records = []
        for building in targets:
            run_one(building, key, args.from_raw, args.attempts, records)

    if records:
        Path(args.metrics).write_text(
            json.dumps({"content_h_ratio": CONTENT_H_RATIO, "canvas_h": CANVAS_H,
                        "alpha_threshold": ALPHA_T, "player_height": PLAYER_HEIGHT,
                        "candidates": records}, ensure_ascii=False, indent=2), encoding="utf-8")
        print("\n量化记录 -> %s" % args.metrics)

    failed = [r["id"] for r in records if not r["pass"]]
    print("\n候选 %d 张，PASS %d，FAIL %d %s" %
          (len(records), len(records) - len(failed), len(failed), failed if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
