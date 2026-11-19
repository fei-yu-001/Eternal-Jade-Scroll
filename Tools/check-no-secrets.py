# -*- coding: utf-8 -*-
"""明文密钥门禁：工作树 / 未推送历史里不许出现明文 API key。

━━ 为什么有这道闸（2026-10-31 青岩落地，主架构师七七派工）━━
流影发现 `Tools/generate-walk-rig-miaosf.py` 硬编码了 miaosf 的 key；青岩全盘扫描又查出
oaifree 网关的 `ah-` key 明文出现在 4 个文件里，且**已进本地 git 历史**：
  - 4bb363c（2026-10-14）Tools/generate-building-art.py
  - f919f89（2026-10-14）Tools/logs/map-21/gen-map21-walksheets.py
两笔都还没推（本地 feature/front-end 领先 origin 330 笔）。工作树已清，key 改从环境变量
`OAIFREE_API_KEY` / `MIAOSF_API_KEY` 读（miaosf 另认 pi 扩展 miaosf-images.ts）。

━━ 禁止 push（主架构师七七裁定，2026-10-31）━━
历史**不改写**（裁决走 (b)：由斐哥在网关后台作废那把 `ah-` key）。
**在斐哥确认作废之前，包含这两笔历史的旧分支禁止 push。**
2026-11-19 斐哥明确要求恢复 GitHub 备份：允许将通过扫描的当前文件创建为
独立 backup/verified 根提交（不携带旧历史、不改写旧分支、不登记密钥豁免），
后续在该干净分支正常提交推送。旧 feature/front-end 仍保持禁止直接推送。 作废确认之后，`--history` 仍会因那两笔变红——
那时的红表示"历史里有一把**已作废**的 key"，应由主架构师显式登记豁免（写进下方
HISTORY_WAIVED 并注明作废日期与确认人），不许为了变绿去改判据或改写历史。

━━ 三种模式 ━━
  python Tools/check-no-secrets.py              # 工作树：已跟踪 + 未跟踪但未被 .gitignore 忽略
  python Tools/check-no-secrets.py --history    # 未推送提交：origin/feature/front-end..HEAD 的新增行
  python Tools/check-no-secrets.py --selftest   # 假 key 夹具（带引号 / 裸写 / md 里）必须点火
  可选：--range <A..B> 覆盖 --history 的提交范围。

━━ 判据 ━━
  S1 裸前缀 token（**不依赖引号**——第一版扫描器就因为要求引号漏掉了两份 .md）：
       sk-ms-<≥20>、sk-<≥20>、ah-<≥40>（token 由 [A-Za-z0-9_-] 组成，左侧不得紧贴字母数字）
     长度下限用来避免误伤普通连字符单词（如 "sk-" 开头的短 id、"ah-ha"）。
  S2 赋值形态：api_key / apikey / api-key / API_KEY 后接 = 或 :，以及 `Bearer <token>`；
     值必须"像 key"才报：≥20 字符、同时含字母与数字、不是 env 引用/占位符。
     这样 `API_KEY = os.environ.get(...)`、`"Bearer " + key` 这类正常代码不会被误报。

━━ 输出纪律 ━━
命中值在被发现的那一刻就打码（前 5 位 + "…" + 后 4 位 + 长度），完整值不进任何列表、
不进任何输出、不写日志。宁可多打码。

━━ 退出码 ━━
  0 = PASS；1 = FAIL（有命中）；2 = 环境错误（不是 git 仓库、范围无效等）——**2 不是绿**。
"""
import os
import re
import subprocess
import sys
import tempfile

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_RANGE = "origin/feature/front-end..HEAD"

# 主架构师显式登记的历史豁免（提交短 sha -> 理由）。**默认空**：作废确认前一条都不许加。
HISTORY_WAIVED = {}

MAX_BYTES = 2_000_000
BINARY_EXT = {
    ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".psd", ".tga", ".exr", ".ico",
    ".npy", ".npz", ".pkl", ".dll", ".so", ".dylib", ".exe", ".pdb", ".mdb", ".zip",
    ".7z", ".gz", ".tar", ".jar", ".class", ".wav", ".mp3", ".ogg", ".mp4", ".ttf",
    ".otf", ".fbx", ".bytes", ".bin", ".unitypackage",
}

TOKEN_CHARS = r"[A-Za-z0-9_\-]"
S1 = [
    ("sk-ms-", re.compile(r"(?<![A-Za-z0-9])sk-ms-" + TOKEN_CHARS + r"{20,}")),
    ("sk-", re.compile(r"(?<![A-Za-z0-9])sk-(?!ms-)" + TOKEN_CHARS + r"{20,}")),
    ("ah-", re.compile(r"(?<![A-Za-z0-9])ah-" + TOKEN_CHARS + r"{40,}")),
]
S2 = [
    ("api_key=", re.compile(
        r"""(?i)\bapi[_\-]?key\b["']?\s*[:=]\s*["']?(?P<v>[^\s"'`,;)}\]]+)""")),
    ("Bearer", re.compile(r"\bBearer\s+(?P<v>" + TOKEN_CHARS + r"[A-Za-z0-9_\-\.]*)")),
]
NOT_A_VALUE = re.compile(
    r"(?i)^(os\.environ|os\.getenv|process\.env|environ|getenv|env\b|\$|\{|<|%|"
    r"your|xxx|placeholder|example|dummy|changeme|none|null|undefined|true|false)")


def mask(value):
    n = len(value)
    if n <= 12:
        return value[:2] + "…(%d)" % n
    return "%s…%s(%d)" % (value[:5], value[-4:], n)


def looks_like_key(value):
    if len(value) < 20 or NOT_A_VALUE.match(value):
        return False
    if not (re.search(r"[A-Za-z]", value) and re.search(r"[0-9]", value)):
        return False
    return re.fullmatch(r"[A-Za-z0-9_\-\.]+", value) is not None


def scan_line(line):
    """返回 [(判据, 打码值)]。完整值只在本函数内存在，出函数前已打码。"""
    hits = []
    taken = []
    for name, rx in S1:
        for m in rx.finditer(line):
            hits.append(("S1 " + name, mask(m.group(0))))
            taken.append((m.start(), m.end()))
    for name, rx in S2:
        for m in rx.finditer(line):
            v = m.group("v")
            st, en = m.start("v"), m.end("v")
            if any(a <= st < b or a < en <= b for a, b in taken):
                continue          # 同一个值已被 S1 报过，不重复计
            if looks_like_key(v):
                hits.append(("S2 " + name, mask(v)))
    return hits


def scan_text(text):
    out = []
    for i, line in enumerate(text.splitlines(), 1):
        for crit, masked in scan_line(line):
            out.append((i, crit, masked))
    return out


def git(*args, check=True):
    r = subprocess.run(["git", "-c", "core.quotepath=false"] + list(args), cwd=ROOT,
                       capture_output=True)
    if check and r.returncode != 0:
        raise RuntimeError("git %s 失败 rc=%d：%s" % (
            " ".join(args), r.returncode, r.stderr.decode("utf-8", "replace").strip()[:300]))
    return r.stdout


def worktree_files(root):
    raw = git("-C", root, "ls-files", "-z", "--cached", "--others", "--exclude-standard")
    seen = set()
    for p in raw.decode("utf-8", "replace").split("\0"):
        if p and p not in seen:
            seen.add(p)
            yield p


def read_text(path):
    if os.path.splitext(path)[1].lower() in BINARY_EXT:
        return None
    try:
        if not os.path.isfile(path) or os.path.getsize(path) > MAX_BYTES:
            return None
        with open(path, "rb") as fh:
            data = fh.read()
    except OSError:
        return None
    if b"\0" in data[:8192]:
        return None
    return data.decode("utf-8", "replace")


def run_worktree(root=ROOT, label="工作树"):
    files = list(worktree_files(root))
    if not files:
        print("RESULT: FAIL 工作树文件清单为空（git ls-files 没返回任何文件）——空输入不是绿")
        return 1
    findings = []
    scanned = 0
    for rel in files:
        text = read_text(os.path.join(root, rel))
        if text is None:
            continue
        scanned += 1
        for line_no, crit, masked in scan_text(text):
            findings.append("%s:%d [%s] %s" % (rel, line_no, crit, masked))
    print("=== 明文密钥门禁（%s）：清单 %d 个文件，实际扫描文本 %d 个 ===" % (label, len(files), scanned))
    if scanned == 0:
        print("RESULT: FAIL 一个文本文件都没扫到——空输入不是绿")
        return 1
    for f in findings:
        print("FAIL: " + f)
    if findings:
        print("RESULT: FAIL %s 发现 %d 处明文密钥（已打码）" % (label, len(findings)))
        return 1
    print("RESULT: PASS %s 零明文密钥（扫描 %d 个文本文件）" % (label, scanned))
    return 0


def run_history(rng):
    try:
        git("rev-parse", "--verify", "--quiet", rng.split("..")[0] + "^{commit}")
        shas = git("rev-list", "--reverse", rng).decode().split()
    except RuntimeError as exc:
        print("RESULT: FAIL 提交范围 %s 无效：%s" % (rng, exc))
        return 2
    print("=== 明文密钥门禁（未推送历史 %s）：%d 笔提交 ===" % (rng, len(shas)))
    if not shas:
        print("RESULT: PASS 范围内没有未推送提交（%s 为空）" % rng)
        return 0
    findings = []
    waived = []
    for sha in shas:
        short = sha[:7]
        patch = git("show", "--format=", "--no-color", "--unified=0", "--no-ext-diff",
                    "--diff-filter=AMRC", sha).decode("utf-8", "replace")
        path = None
        for line in patch.splitlines():
            if line.startswith("+++ "):
                path = line[6:] if line.startswith("+++ b/") else line[4:]
                continue
            if not line.startswith("+") or line.startswith("+++"):
                continue
            for crit, masked in scan_line(line[1:]):
                rec = "%s %s [%s] %s" % (short, path, crit, masked)
                (waived if short in HISTORY_WAIVED else findings).append(rec)
    for w in waived:
        print("WAIVED: %s（豁免理由：%s）" % (w, HISTORY_WAIVED[w.split()[0]]))
    for f in findings:
        print("FAIL: " + f)
    if findings:
        commits = sorted({f.split()[0] for f in findings})
        print("RESULT: FAIL 未推送历史里有 %d 处明文密钥，涉及提交 %s——**禁止 push**"
              % (len(findings), ", ".join(commits)))
        return 1
    print("RESULT: PASS 未推送历史 %d 笔提交零明文密钥" % len(shas))
    return 0


# ---------------------------------------------------------------- selftest
def _fake(prefix, n):
    """运行期拼出假 key，源码里不出现任何会被本闸命中的字面串。"""
    body = ("Q7x" * 40)[:n - 4] + "9z8Y"
    return prefix + body


def selftest():
    fake_sk = _fake("sk" + "-", 32)
    fake_ms = _fake("sk" + "-ms-", 34)
    fake_ah = _fake("ah" + "-", 64)
    fake_plain = _fake("", 40)                          # 无前缀，只能靠 S2 抓
    q = '"'
    must_red = [
        ("1_带引号_python赋值", "x.py", "API_KEY = " + q + fake_ms + q + "\n", "S1 sk-ms-"),
        ("2_裸写_ts常量", "ext.ts", "const k = " + fake_sk + ";\n", "S1 sk-"),
        ("3_md里裸写_冒号", "brief.md", "- 密钥:" + fake_ah + "\n", "S1 ah-"),
        ("4_md里api_key=无引号", "notes.md", "api_key=" + fake_plain + "\n", "S2 api_key="),
        ("5_Bearer头", "curl.sh", "curl -H 'Authorization: Bearer " + fake_plain + "'\n", "S2 Bearer"),
        ("6_json冒号带引号", "cfg.json", '{"apiKey": "' + fake_plain + '"}\n', "S2 api_key="),
    ]
    must_green = [
        ("7_环境变量读取", "ok.py", 'API_KEY = os.environ.get("X_API_KEY")\nKEY_ENV = "MIAOSF_API_KEY"\n'),
        ("8_Bearer拼接变量", "ok2.py", 'headers={"Authorization": "Bearer " + key}\n'),
        ("9_普通连字符词", "ok.md", "sk-ms-short ah-ha ask-me task-done risk-free sk-learn\n"),
        ("10_占位符", "ok3.md", "api_key=<YOUR_KEY_HERE_0000000000> API_KEY=${MIAOSF_API_KEY}\n"),
    ]
    fired = 0
    total = 0
    bad = []
    for name, _fn, text, want in must_red:
        total += 1
        hits = scan_text(text)
        crits = [c for _l, c, _m in hits]
        leaked = any(fake_sk in m or fake_ms in m or fake_ah in m or fake_plain in m for _l, _c, m in hits)
        if want in crits and not leaked:
            fired += 1
            print("  [PASS] %-24s -> 红，判据 %s，打码 %s" % (name, want, hits[0][2]))
        else:
            bad.append(name)
            print("  [FAIL] %-24s -> 期望 %s，实得 %s%s" % (name, want, crits or "无命中",
                                                         "（且泄漏了完整值！）" if leaked else ""))
    for name, _fn, text in must_green:
        total += 1
        hits = scan_text(text)
        if not hits:
            fired += 1
            print("  [PASS] %-24s -> 绿（不误报）" % name)
        else:
            bad.append(name)
            print("  [FAIL] %-24s -> 误报 %s" % (name, [(c, m) for _l, c, m in hits]))

    # 端到端：真起一个临时 git 仓库，走与主跑完全相同的 run_worktree 路径
    total += 1
    with tempfile.TemporaryDirectory(prefix="no-secrets-selftest-") as tmp:
        subprocess.run(["git", "init", "-q", tmp], check=True)
        with open(os.path.join(tmp, ".gitignore"), "w", encoding="utf-8") as fh:
            fh.write("ignored.md\n")
        for _n, fn, text, _w in must_red:
            with open(os.path.join(tmp, fn), "w", encoding="utf-8") as fh:
                fh.write(text)
        with open(os.path.join(tmp, "ignored.md"), "w", encoding="utf-8") as fh:
            fh.write("- 密钥:" + fake_ah + "\n")
        import io
        import contextlib
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = run_worktree(tmp, "夹具仓库")
        out = buf.getvalue()
        fail_lines = [l for l in out.splitlines() if l.startswith("FAIL: ")]
        leaked = any(v in out for v in (fake_sk, fake_ms, fake_ah, fake_plain))
        ignored_hit = any(l.startswith("FAIL: ignored.md") for l in fail_lines)
        if rc == 1 and len(fail_lines) == len(must_red) and not leaked and not ignored_hit:
            fired += 1
            print("  [PASS] %-24s -> rc=1，%d 处命中全打码，.gitignore 忽略的文件不扫" % (
                "11_端到端临时仓库", len(fail_lines)))
        else:
            bad.append("11_端到端临时仓库")
            print("  [FAIL] 11_端到端临时仓库 -> rc=%d 命中 %d（期望 %d）泄漏=%s 扫了忽略文件=%s"
                  % (rc, len(fail_lines), len(must_red), leaked, ignored_hit))

    # 空输入必须红（防"读空数组所以 PASS"）
    total += 1
    with tempfile.TemporaryDirectory(prefix="no-secrets-empty-") as tmp:
        subprocess.run(["git", "init", "-q", tmp], check=True)
        import io
        import contextlib
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = run_worktree(tmp, "空仓库")
        if rc == 1:
            fired += 1
            print("  [PASS] %-24s -> rc=1（空输入不是绿）" % "12_空仓库必红")
        else:
            bad.append("12_空仓库必红")
            print("  [FAIL] 12_空仓库必红 -> rc=%d，空输入被当成绿" % rc)

    if bad:
        print("SELFTEST: FAIL %d/%d 条点火，失败：%s" % (fired, total, ", ".join(bad)))
        return 1
    print("SELFTEST: PASS %d/%d 条用例全点火（6 红：带引号/裸写/md 裸写/md api_key=/Bearer/json；"
          "4 绿：env 读取/Bearer 拼接/连字符词/占位符；端到端临时仓库；空仓库必红）" % (fired, total))
    return 0


def main(argv):
    if "-h" in argv or "--help" in argv:
        print(__doc__)
        return 0
    if "--selftest" in argv:
        return selftest()
    try:
        git("rev-parse", "--is-inside-work-tree")
    except RuntimeError as exc:
        print("RESULT: FAIL 不在 git 工作树里：%s" % exc)
        return 2
    if "--history" in argv:
        rng = DEFAULT_RANGE
        if "--range" in argv:
            i = argv.index("--range")
            if i + 1 >= len(argv):
                print("RESULT: FAIL --range 缺参数")
                return 2
            rng = argv[i + 1]
        return run_history(rng)
    return run_worktree()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
