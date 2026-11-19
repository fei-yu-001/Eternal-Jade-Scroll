# -*- coding: utf-8 -*-
"""角色 rig 三视图生成器（V-01 pilot，2026-10-06 架构师）。

目的：现在角色的走路是"一张完整立绘横切成 4 条色带在转"，所以又僵又贴纸。
     要换成真骨骼动画，前提是**素材能被切成部件**：手臂必须与躯干有可见背景间隙。

本脚本做三件事：
  1. 按"rig 友好"合约生成 front / side / back 三视图（自有 grok2api 网关）；
  2. 自动验收：四边留白 >=8%；正面/背面在手臂高度必须检出"手-躯干-手"三段前景
     （即手臂与躯干之间真的有背景缝），否则判 FAIL；
  3. 原图存 ArtSource/originals/characters-rig/，提示词存 prompts/character-rig/，判定进日志。

用法：python Tools/generate-character-rig-art.py [slug] [subject中文说明]
"""
import base64
import json
import os
import sys
import urllib.request
from urllib.request import ProxyHandler, build_opener

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

BASE = "http://127.0.0.1:8000"
KEY_FILE = os.path.join(os.path.sep, "tmp", "ddyu-art-key.txt")
ROOT = r"D:\qiqi_me\Eternal Jade Scroll\GameClient\TwelveJade\Assets\ArtSource"
RAW_DIR = os.path.join(ROOT, "originals", "characters-rig")
PROMPT_DIR = os.path.join(ROOT, "prompts", "character-rig")

HEADER = (
    "Asset type: a single game character full-body illustration for a hand-painted 2D game, "
    "fine ink linework with soft watercolour washes on warm ivory paper, muted natural colours, "
    "elegant historical Chinese indie game art set in the late Yuan / early Ming dynasty. "
)

# rig 合约的核心：手臂必须离开躯干，否则切不出部件。
POSE = (
    "Pose: relaxed upright standing, arms held clearly AWAY from the body at about 35 degrees "
    "from the torso, elbows almost straight, hands open and empty; both arms and sleeves must be "
    "completely outside the torso silhouette with a clearly visible strip of empty background "
    "between each arm and the ribs; legs slightly apart with a visible gap between the two legs; "
    "feet flat on an implied ground line, weight even. "
)

LIGHT = (
    "Lighting: one soft key light from the upper-left at about 45 degrees and gentle ambient "
    "fill from the lower-right; painted shading, soft cast shadows inside the folds of the cloth "
    "and a subtle darker core shadow along the lower-right side of every limb, so the figure reads "
    "as three-dimensional carved volume rather than a flat paper cut-out. "
)

FRAME = (
    "Framing: full body from the top of the head to the soles of the feet, centred, the whole "
    "figure inside the frame with at least 8 percent empty margin on all four sides; uniform flat "
    "warm ivory background, no scene, no ground, no surface, no gradient, no cast shadow outside "
    "the figure, no text, no labels, no logo, no watermark, no modern elements. "
)

VIEWS = {
    "front": "View: straight front view, the character faces the viewer. ",
    "side": "View: pure side profile view, the character faces the right side of the image, "
            "only one arm and one leg visible in profile. ",
    "back": "View: straight back view, the character is seen from behind. ",
}

DEFAULT_SUBJECT = (
    "Character: a young male farmer of a riverside town in the late Yuan dynasty, short black hair "
    "tied in a simple cloth wrap, plain undyed linen short jacket over a grey inner robe with rolled "
    "sleeves, wide dark trousers, straw sandals, a plain cloth belt, weathered friendly face. "
)


def read_key():
    if not os.path.exists(KEY_FILE):
        raise SystemExit("missing gateway key at " + KEY_FILE)
    value = open(KEY_FILE, encoding="utf-8").read().strip()
    if len(value) < 10:
        raise SystemExit("gateway key looks empty")
    return value


def generate(prompt):
    opener = build_opener(ProxyHandler({}))
    payload = {
        "model": "grok-imagine-image",
        "prompt": prompt,
        "n": 1,
        "aspect_ratio": "3:4",
        "response_format": "b64_json",
    }
    request = urllib.request.Request(
        BASE + "/v1/images/generations",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + read_key(), "Content-Type": "application/json"},
    )
    result = json.loads(opener.open(request, timeout=300).read())
    data = result.get("data") or []
    if not data:
        raise RuntimeError("gateway returned no image: " + json.dumps(result)[:300])
    first = data[0]
    if first.get("b64_json"):
        return base64.b64decode(first["b64_json"])
    if first.get("url"):
        return opener.open(first["url"], timeout=180).read()
    raise RuntimeError("unrecognised image payload: " + json.dumps(first)[:200])


def inspect(path, want_arm_gap):
    """返回 (ok, 说明列表)。判据：四边留白 >=8%；正面/背面在手臂高度要检出三段前景。"""
    from PIL import Image
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    bg = px[2, 2]

    def is_bg(c):
        return abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) < 70

    min_x, min_y, max_x, max_y = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if not is_bg(px[x, y]):
                if x < min_x: min_x = x
                if x > max_x: max_x = x
                if y < min_y: min_y = y
                if y > max_y: max_y = y
    notes = ["size %dx%d" % (w, h)]
    if max_x < 0:
        return False, notes + ["FAIL: 整张图都是背景色"]
    margin = {
        "left": min_x / w, "right": (w - 1 - max_x) / w,
        "top": min_y / h, "bottom": (h - 1 - max_y) / h,
    }
    notes.append("margins L%.1f%% R%.1f%% T%.1f%% B%.1f%%" % (
        margin["left"] * 100, margin["right"] * 100, margin["top"] * 100, margin["bottom"] * 100))
    tight = [k for k, v in margin.items() if v < 0.08]
    ok = not tight
    if tight:
        notes.append("FAIL: 留白不足 " + ",".join(tight))

    if want_arm_gap:
        torso_top = min_y + int((max_y - min_y) * 0.30)
        torso_bot = min_y + int((max_y - min_y) * 0.55)
        rows_with_three = 0
        rows_checked = 0
        for y in range(torso_top, max(torso_top + 1, torso_bot)):
            rows_checked += 1
            runs = 0
            inside = False
            for x in range(min_x, max_x + 1):
                fg = not is_bg(px[x, y])
                if fg and not inside:
                    runs += 1
                inside = fg
            if runs >= 3:
                rows_with_three += 1
        ratio = rows_with_three / max(1, rows_checked)
        notes.append("arm-gap rows %d/%d (%.0f%%)" % (rows_with_three, rows_checked, ratio * 100))
        if ratio < 0.35:
            ok = False
            notes.append("FAIL: 手臂与躯干没分开（切不出独立手臂）")
    return ok, notes


def main():
    slug = sys.argv[1] if len(sys.argv) > 1 else "farmer-male-0"
    subject = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_SUBJECT
    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(PROMPT_DIR, exist_ok=True)
    for view, view_line in VIEWS.items():
        prompt = HEADER + subject + view_line + POSE + LIGHT + FRAME
        target = os.path.join(RAW_DIR, "%s-%s-v1.png" % (slug, view))
        with open(os.path.join(PROMPT_DIR, "%s-%s-v1.txt" % (slug, view)), "w", encoding="utf-8") as fh:
            fh.write(prompt + os.linesep)
        try:
            blob = generate(prompt)
        except Exception as error:
            print("GEN-FAIL", view, type(error).__name__, str(error)[:200])
            continue
        with open(target, "wb") as fh:
            fh.write(blob)
        ok, notes = inspect(target, want_arm_gap=(view != "side"))
        print(("PASS " if ok else "FAIL ") + view, target, len(blob) // 1024, "KB")
        for note in notes:
            print("      " + note)


if __name__ == "__main__":
    main()
