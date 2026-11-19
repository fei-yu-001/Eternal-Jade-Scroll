# -*- coding: utf-8 -*-
"""对话半身立绘生成器（5 位 NPC）—— 走**本机 grok 网关**（文生图，不占豆包额度）。

为什么归 grok：这 5 张是**独立角色**，不像主角那样需要"保持同一个人"
⇒ **不触发身份一致性闸门**，纯文生图即可；而网关就是 OpenAI 兼容接口，直连即用。

合约（与 `FrontEndTrade.cs` 的槽位常量对齐，**不要改代码去适配图**）：
  · 目标尺寸 **512×768（2:3 竖幅）**，人物居中，半身（到胸口/腰），
    暖象牙纸底，**不许纯黑剪影、不许只有轮廓线**；
  · 落点：原图 `Assets/ArtSource/originals/portraits/<id>.png`
    ＋ 提示词 `Assets/ArtSource/prompts/portraits/<id>.txt`；
  · `--install` 时再复制进 `Assets/Resources/Art/Portraits/<id>.png`
    （按 ADR-025，落进去就自动生效）。

用法：
  python Tools/generate-portraits.py --dry-run        # 只打印提示词，不联网
  python Tools/generate-portraits.py                  # 生成 5 张（走网关）
  python Tools/generate-portraits.py --only huolang   # 只生成一张
  python Tools/generate-portraits.py --install        # 生成后直接装进 Resources
  python Tools/generate-portraits.py --selftest       # 离线自检（提示词/尺寸/落点）
"""
import base64
import io
import json
import os
import sys
import urllib.request
from urllib.parse import urlsplit
from urllib.request import ProxyHandler, build_opener

from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

BASE = "http://127.0.0.1:8000"
KEY_FILE = os.path.join(os.path.sep, "tmp", "ddyu-art-key.txt")
ROOT = r"GameClient/TwelveJade/Assets"
RAW_DIR = os.path.join(ROOT, "ArtSource", "originals", "portraits")
PROMPT_DIR = os.path.join(ROOT, "ArtSource", "prompts", "portraits")
INSTALL_DIR = os.path.join(ROOT, "Resources", "Art", "Portraits")
TARGET = (512, 768)

# ── 画风段：**照抄现有立绘的合约**（`generate-character-rig-art.py` 的 HEADER/LIGHT 口径），
#    保证 NPC 与主角是同一套画风。改这段要谨慎：它决定"像不像同一个游戏"。
STYLE = (
    "Asset type: a single NPC half-body portrait (bust) for a hand-painted 2D game, "
    "fine ink linework with soft watercolour washes on warm ivory paper, muted natural colours, "
    "elegant historical Chinese indie game art set in the late Yuan / early Ming dynasty. "
    "Lighting: one soft key light from the upper-left at about 45 degrees with gentle ambient fill, "
    "painted shading, so the figure reads as three-dimensional carved volume rather than a flat cut-out. "
    "Framing: bust shot from the top of the head to the chest or waist, centred, the figure occupying "
    "80 to 90 percent of the image height with a little empty margin left and right; "
    "uniform flat warm ivory background, no scene, no ground, no border, no frame, no text, "
    "no labels, no logo, no watermark, no modern elements. "
)

# ── 【可改】5 位 NPC 的中文设定：**只改这一处**，其余不用动。
#    我按 id 字面推断写的第一版；若与《十二玉楼长生经》的设定不符，直接改这一行即可。
SUBJECTS = {
    "huolang": "货郎老崔：五十岁上下的走街汉子，背着一副货箱扁担，圆脸、笑起来眼角有褶子，"
               "头上裹一块褪色的灰布巾，穿靛蓝色粗布短褂，腰间挂钱袋与一杆小秤，眼神精明但不凶",
    "npc-shenhuai-an": "沈怀安：三十出头的书生，身形清瘦，束发戴一方旧儒巾，穿月白色交领长衫、"
                       "外罩一件洗得发白的青灰褙子，眉目温和而略带忧色，手里可以捧一卷书",
    "npc-shitou": "石头：二十出头的壮实后生，皮肤晒得黑红，短打布衣卷起袖口露出手臂，"
                  "头上随便束着发，肩上搭一条汗巾，憨厚地笑，手掌粗大",
    "npc-yao-lao": "药老：六十开外的老药师，白发挽成小髻、插一根木簪，长须花白，"
                   "穿土黄褐色粗布长袍，腰系药囊，袖口有药渍，眼神清亮，手边可以挂着几串干草药",
    "npc-zhao-da": "赵大：四十来岁的魁梧汉子，国字脸、浓眉短须，裹深色头巾，"
                   "穿深褐色短打，斜挎一条革带，双臂结实，看起来像镇上的护卫或苦力头目",
}

BAD_WORDS = ("silhouette", "black and white", "monochrome", "line art only")


def read_key():
    if not os.path.exists(KEY_FILE):
        raise SystemExit("gateway key missing at " + KEY_FILE)
    v = open(KEY_FILE, encoding="utf-8").read().strip()
    if len(v) < 10:
        raise SystemExit("gateway key looks empty")
    return v


def prompt_for(npc_id):
    return STYLE + "Character: " + SUBJECTS[npc_id] + ". "


def generate(prompt):
    opener = build_opener(ProxyHandler({}))
    payload = {"model": "grok-imagine-image", "prompt": prompt, "n": 1,
               "aspect_ratio": "3:4", "response_format": "b64_json"}
    req = urllib.request.Request(BASE + "/v1/images/generations",
                                data=json.dumps(payload).encode("utf-8"),
                                headers={"Authorization": "Bearer " + read_key(),
                                         "Content-Type": "application/json"})
    res = json.loads(opener.open(req, timeout=300).read())
    data = res.get("data") or []
    if not data:
        raise RuntimeError("no image: " + json.dumps(res)[:200])
    first = data[0]
    if first.get("b64_json"):
        return base64.b64decode(first["b64_json"])
    if first.get("url"):
        return opener.open(first["url"], timeout=180).read()
    raise RuntimeError("unrecognised payload: " + json.dumps(first)[:200])


def fit_slot(im):
    """中心裁成 2:3 再缩到 512×768 —— 槽位用 preserveAspect，给出精确尺寸最稳。"""
    im = im.convert("RGB")
    tw, th = TARGET
    want = tw / float(th)
    have = im.width / float(im.height)
    if have > want:                      # 太宽：裁两侧（我们本来就要求左右留白）
        w = int(round(im.height * want))
        x = (im.width - w) // 2
        im = im.crop((x, 0, x + w, im.height))
    else:                                # 太高：裁上下（保头部，向下少裁）
        h = int(round(im.width / want))
        y = max(0, (im.height - h) // 3)
        im = im.crop((0, y, im.width, y + h))
    return im.resize(TARGET, Image.LANCZOS)


def selftest():
    problems = 0

    def check(ok, name, detail=""):
        nonlocal problems
        print(("  ok   " if ok else "  FAIL ") + name + ("  [" + detail + "]" if detail else ""))
        if not ok:
            problems += 1

    check(len(SUBJECTS) == 5, "5 位 NPC 都有设定", str(len(SUBJECTS)) + " 位")
    for npc_id, desc in SUBJECTS.items():
        p = prompt_for(npc_id)
        check(len(p) > 300, npc_id + "：提示词足够长（画风＋角色）", str(len(p)) + " 字符")
        check(not any(w in p.lower() for w in BAD_WORDS),
              npc_id + "：提示词里没有'剪影/单色/只要线稿'字样（这是本轮的病根）")
        check("bust" in p and "80 to 90 percent" in p and "warm ivory" in p,
              npc_id + "：半身/占比/暖纸底三条合约都在")
    # 尺寸换算：3:4 的图裁成 2:3 再缩到 512×768
    im = Image.new("RGB", (768, 1024), (237, 230, 210))
    out = fit_slot(im)
    check(out.size == TARGET, "fit_slot 产出精确 512×768", str(out.size))
    print()
    print("suite finished, " + str(problems) + " problem(s)")
    return problems == 0


def main():
    argv = sys.argv[1:]
    if "--selftest" in argv:
        sys.exit(0 if selftest() else 1)
    only = argv[argv.index("--only") + 1] if "--only" in argv else None
    install = "--install" in argv
    dry = "--dry-run" in argv
    ids = [only] if only else list(SUBJECTS)
    if only and only not in SUBJECTS:
        raise SystemExit("unknown id: " + only + "（可选：" + ", ".join(SUBJECTS) + "）")
    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)
    if install:
        os.makedirs(INSTALL_DIR, exist_ok=True)
    for npc_id in ids:
        prompt = prompt_for(npc_id)
        with open(os.path.join(PROMPT_DIR, npc_id + ".txt"), "w", encoding="utf-8") as fh:
            fh.write(prompt + os.linesep)
        if dry:
            print("=== " + npc_id + " ===\n" + prompt)
            continue
        try:
            blob = generate(prompt)
        except Exception as err:
            print("GEN-FAIL %-18s %s %s" % (npc_id, type(err).__name__, str(err)[:160]))
            continue
        raw = Image.open(io.BytesIO(blob))
        out = fit_slot(raw)
        dst = os.path.join(RAW_DIR, npc_id + ".png")
        out.save(dst)
        print("ok %-18s 源 %s → %s  %d KB" % (npc_id, raw.size, TARGET, len(blob) // 1024))
        if install:
            out.save(os.path.join(INSTALL_DIR, npc_id + ".png"))
            print("    已装进 " + os.path.join(INSTALL_DIR, npc_id + ".png"))


if __name__ == "__main__":
    main()
