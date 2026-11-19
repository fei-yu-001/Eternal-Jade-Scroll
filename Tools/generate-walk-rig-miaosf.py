# -*- coding: utf-8 -*-
"""WALK-RIG-2 ｜ 用 miaosf gpt-image-2.5 生成走帧素材（2026-10-29）。

━━ 为什么重做 ━━
盘上 farmer-male-0 三向是**三次独立生成**：肩宽 32.5%/27.9%/34.0%、剪影高跨向差 7%、
侧面有背篓而正面没有。`check-walk-sheet-motion.py` 只判单张内部有没有动，**没有跨向判据**，
所以三张全 PASS 而人眼看着不是同一个角色。

━━ 本脚本的两条硬规矩 ━━
1. **先锚点后帧，全程 edits**：side/back 一律以正面锚点为参考图；每一帧也以锚点为参考图。
   身份由参考图锁死，不靠文字再描述一遍（再描述就是再创作一次）。
2. **姿势由骨架图给，不由文字描述**：`make-walk-pose-guide.py` 用 PIL 算出 8 相位的骨架，
   作为第 2 张参考图喂进去。文字描述姿势命中率不稳（实测 pass 相被画成推离相），
   一次跑偏 1 积分；骨架图不花钱，改参数随便重算。

━━ 底与去底 ━━
沿用米色 (245,234,212)：`walk-asset-debg.py` / `extract-walk-sheet.py` 的去底是按实测底色
容差 flood fill 实现的，换底色等于把去底链打断。

用法：
    # 锚点（正面，文生图）
    python Tools/generate-walk-rig-miaosf.py --view front --name anchor-front-v1
    # 锚点（侧面/背面，以正面为参考）
    python Tools/generate-walk-rig-miaosf.py --view side --ref <front.png> --name anchor-side-v1
    # 走帧（锚点 + 骨架两张参考）
    python Tools/generate-walk-rig-miaosf.py --view side --pose-frame 0 \
        --ref <anchor-side.png> --ref <guides-side/guide-00.png> --name side-f00-v1
"""
import argparse
import base64
import importlib.util
import json
import os
import re
import sys
import urllib.request

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)

BASE = "https://miaosf.com/v1"
KEY_ENV = "MIAOSF_API_KEY"
EXTENSION = os.path.join(os.path.expanduser("~"), ".pi", "agent", "extensions",
                         "miaosf-images.ts")
MODEL = "gpt-image-2.5"
SIZE = "1024x1365"          # 3:4 = 0.750，与 816x1088 帧格完全同比例
# Cloudflare 前置：默认 urllib UA 会被 403（与 geili 同一个坑）
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36")

OUT_DIR = os.path.join(REPO, "GameClient", "TwelveJade", "Assets", "ArtSource",
                       "candidates", "walk-rig-20261029")
PROMPT_DIR = os.path.join(REPO, "GameClient", "TwelveJade", "Assets", "ArtSource",
                          "prompts", "walk-rig")


def load_key():
    """miaosf key：环境变量 MIAOSF_API_KEY → pi 扩展 miaosf-images.ts → 报错退出。

    与 generate-xindu-art.py 的 load_key() 同款。key 从不打印、从不写日志、从不写进 manifest。
    （2026-10-31 青岩：原先明文硬编码在本文件，已移除。）
    """
    key = os.environ.get(KEY_ENV)
    if key:
        return key.strip()
    if os.path.exists(EXTENSION):
        with open(EXTENSION, encoding="utf-8") as fh:
            text = fh.read()
        match = re.search(r'\bAPI_KEY\s*=\s*"([^"]+)"', text)
        if match:
            return match.group(1).strip()
    raise SystemExit("no miaosf key: set %s or keep the pi extension at %s" % (KEY_ENV, EXTENSION))


def load_rig_prompt_parts():
    """复用既有 rig 合约（成套美术语言），不新造第二套。"""
    path = os.path.join(HERE, "generate-character-rig-art.py")
    spec = importlib.util.spec_from_file_location("rig_art", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def subject_note(from_reference):
    same = ("the same young male farmer as the reference image"
            if from_reference else "a young male farmer")
    return (
        "Character: " + same + " — short black hair in a plain cloth wrap, undyed linen short "
        "jacket over a grey inner robe with rolled sleeves, wide dark trousers with cloth leg "
        "wraps, straw sandals, plain cloth belt, weathered friendly face. "
        "No basket, no bag, no hat, no weapon, nothing carried in the hands. "
    )


# 走帧用视角：侧身迈步时两条腿都看得见（一前一后），不能沿用"only one leg visible"的站姿口径。
VIEWS_POSE = {
    "side": "View: pure side profile view, the character faces the right side of the image; both "
            "legs are visible, one forward and one behind, and both arms are visible. ",
    "front": "View: straight front view, the character faces the viewer. ",
    "back": "View: straight back view, the character is seen from behind. ",
}


def build_prompt(parts, view, ref_count, standing):
    # 自生成时只用一份角色描述；走 edits 时角色由参考图定义，文字只留姿势/光/取景/视角。
    head = "" if ref_count else parts.HEADER + subject_note(False)
    view_line = parts.VIEWS[view] if standing else VIEWS_POSE[view]
    body = (parts.POSE if standing else "") + parts.LIGHT + parts.FRAME + view_line
    tail = ("" if ref_count else
            "Consistency: identical body height, identical head size, identical shoulder width and "
            "identical costume in every view; the figure must be the same person, drawn once, "
            "seen from another angle. ")
    prompt = " ".join(x for x in (head, body, tail) if x)
    if ref_count >= 2:
        prompt += (" Pose reference: reference image 2 is a red skeleton guide drawn on the same "
                   "ivory background. Copy its limb positions exactly — hip, knee, ankle, foot and "
                   "arm angles — and paint the character from reference image 1 into that pose. "
                   "Keep both feet placed on the green ground line shown in the guide. "
                   "Do not draw the red lines, the green line, the joints or any part of the guide. ")
    return prompt


def post_json(url, payload, key):
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json",
                 "User-Agent": UA, "Accept": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.loads(response.read())


def post_edit(prompt, ref_paths, key, size=None):
    """ref_paths 有序：第 1 张=身份锚点，第 2 张=姿势骨架。顺序与提示词里的编号一致。"""
    boundary = "----walkrig2boundary"
    if isinstance(ref_paths, str):
        ref_paths = [ref_paths]

    def part(name, value):
        return (("--" + boundary + "\r\n").encode() +
                ('Content-Disposition: form-data; name="%s"\r\n\r\n' % name).encode() +
                value.encode("utf-8") + b"\r\n")

    payload = b""
    payload += part("model", MODEL)
    payload += part("prompt", prompt)
    payload += part("size", size or SIZE)
    payload += part("n", "1")
    for slot, ref_path in enumerate(ref_paths, start=1):
        with open(ref_path, "rb") as handle:
            ref_bytes = handle.read()
        payload += (("--" + boundary + "\r\n").encode() +
                    ('Content-Disposition: form-data; name="image"; filename="reference-%d.png"\r\n'
                     % slot).encode() +
                    b"Content-Type: image/png\r\n\r\n" + ref_bytes + b"\r\n")
    payload += ("--" + boundary + "--\r\n").encode()

    request = urllib.request.Request(
        BASE + "/images/edits",
        data=payload,
        headers={"Authorization": "Bearer " + key,
                 "Content-Type": "multipart/form-data; boundary=" + boundary,
                 "User-Agent": UA, "Accept": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.loads(response.read())


def save_result(result, out_path):
    items = result.get("data") or []
    if not items:
        raise RuntimeError("gateway returned no image: " + json.dumps(result)[:300])
    first = items[0]
    if not first.get("b64_json"):
        raise RuntimeError("response carried no b64_json: " + json.dumps(first)[:200])
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as handle:
        handle.write(base64.b64decode(first["b64_json"]))
    return len(first["b64_json"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--view", required=True, choices=["front", "side", "back"])
    parser.add_argument("--ref", action="append", default=None,
                        help="参考图，可重复：第1张=身份锚点，第2张=姿势骨架")
    parser.add_argument("--name", default=None, help="输出文件名（不含扩展名）")
    parser.add_argument("--pose-frame", type=int, default=None,
                        help="走帧相位 0..7；给了就不写站立姿势描述")
    parser.add_argument("--dry-run", action="store_true", help="只打印提示词，不调接口、不花积分")
    parser.add_argument("--out-dir", default=None,
                        help="输出目录（图 + manifest.json + prompts/）；默认仍是 ArtSource/candidates/walk-rig-20261029")
    parser.add_argument("--size", default=SIZE, help="出图尺寸，默认 %s；参考图画布不同时要跟着改" % SIZE)
    parser.add_argument("--extra-prompt", default="", help="追加在合约提示词末尾的一段（不改合约本身）")
    parser.add_argument("--budget-total", type=int, default=None,
                        help="本输出目录累计调用上限：manifest.json 里已记的调用（含失败）计入，超了不发")
    args = parser.parse_args()

    out_dir = os.path.abspath(args.out_dir) if args.out_dir else OUT_DIR
    # --out-dir 给了：提示词也落在 out_dir/prompts，不再写 Assets/ArtSource/prompts
    prompt_dir = os.path.join(out_dir, "prompts") if args.out_dir else PROMPT_DIR
    manifest_path = os.path.join(out_dir, "manifest.json")

    parts = load_rig_prompt_parts()
    refs = args.ref or []
    prompt = build_prompt(parts, args.view, len(refs), args.pose_frame is None)
    if args.extra_prompt:
        prompt = prompt.rstrip() + " " + args.extra_prompt.strip()
    name = args.name or ("anchor-%s-v1" % args.view)
    out_path = os.path.join(out_dir, name + ".png")

    manifest = []
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8") as handle:
            manifest = json.load(handle)
    if args.budget_total is not None and not args.dry_run and len(manifest) >= args.budget_total:
        print("=== budget: %d calls already in %s >= --budget-total %d; not sending"
              % (len(manifest), manifest_path, args.budget_total))
        return 2

    print("=== prompt (%d chars, %d refs) ===" % (len(prompt), len(refs)))
    print(prompt)
    print("=== output:", out_path)
    for slot, path in enumerate(refs, start=1):
        print("=== ref %d: %s (%d bytes)" % (slot, path, os.path.getsize(path)))

    if args.dry_run:
        # dry-run 不写任何文件（旧版这里也会写 PROMPT_DIR，那在 Assets 下）
        print("=== dry run: skipped the API call, 0 credits spent, nothing written")
        return 0

    os.makedirs(prompt_dir, exist_ok=True)
    with open(os.path.join(prompt_dir, name + ".txt"), "w", encoding="utf-8") as handle:
        handle.write(prompt + "\n")

    def record(**extra):
        entry = {
            "file": name + ".png",
            "view": args.view,
            "poseFrame": args.pose_frame,
            "model": MODEL,
            "size": args.size,
            "references": [os.path.basename(x) for x in refs],
            "promptFile": name + ".txt",
        }
        entry.update(extra)
        manifest.append(entry)
        os.makedirs(out_dir, exist_ok=True)
        with open(manifest_path, "w", encoding="utf-8") as handle:
            json.dump(manifest, handle, ensure_ascii=False, indent=2)

    key = load_key()
    mode = "edits" if refs else "generations"
    try:
        if refs:
            result = post_edit(prompt, refs, key, args.size)
        else:
            result = post_json(BASE + "/images/generations",
                               {"model": MODEL, "prompt": prompt, "size": args.size, "n": 1}, key)
    except Exception as exc:
        # 失败也可能计费：照样记进 manifest，--budget-total 才数得准
        record(mode=mode, status="error", error=str(exc)[:200])
        raise

    size = save_result(result, out_path)
    usage = result.get("usage") or {}
    record(mode=mode, status="ok", bytes=size,
           creditsCharged=usage.get("credits_charged"),
           creditsRemaining=usage.get("credits_remaining"))

    print("=== saved %s (%d bytes)" % (out_path, size))
    print("=== credits charged %s / remaining %s" %
          (usage.get("credits_charged"), usage.get("credits_remaining")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
