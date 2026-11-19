# -*- coding: utf-8 -*-
"""Gate C0-A：世界管线唯一路径根。

背景：历史上出现过两套 ArtSource——
    REPO_ROOT/Assets/ArtSource/...                      （旧，仓库根）
    REPO_ROOT/GameClient/TwelveJade/Assets/ArtSource/...（正式，Unity 工程内）
两套并存会产生"两个真相"。本模块把正式根固定为 Unity 工程内那一套，
旧根只允许只读 fallback，并且读写到旧路径时必须告警。

约定：
    REPO_ROOT              = 含 GameClient / Tools / Documents 的仓库根
    UNITY_PROJECT_ROOT     = REPO_ROOT/GameClient/TwelveJade
    ART_SOURCE_ROOT        = UNITY_PROJECT_ROOT/Assets/ArtSource
    CONFIG_ROOT            = UNITY_PROJECT_ROOT/Assets/Resources/Config
    TOOLS_LOG_ROOT         = REPO_ROOT/Tools/logs
    TOOLS_SCREENSHOT_ROOT  = REPO_ROOT/Tools/screenshots

所有读写美术源文件 / 配置 / 日志的脚本都必须经本模块解析路径，不准各自拼字符串。
"""
import os
import sys

# 健壮性（夜1 G-3，2026-10-06）：zh-CN Windows 的 GBK 控制台下 print 中文/箭头会
# UnicodeEncodeError 崩在出结论之前——**会崩的脚本比会红的脚本危险得多**。
# 故统一强制 stdout/stderr 为 UTF-8；先例见 SaveChecks.csproj 的 <CodePage>65001</CodePage>。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

_MARKERS = ('GameClient', 'Tools', 'Documents')


def repo_root():
    """仓库根：从本文件位置向上找到同时含 GameClient/Tools/Documents 的那一层。"""
    here = os.path.dirname(os.path.abspath(__file__))
    cur = here
    for _ in range(8):
        if all(os.path.isdir(os.path.join(cur, m)) for m in _MARKERS):
            return cur
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    # 兜底：Tools/ 的上一级
    return os.path.dirname(here)


def unity_project_root():
    return os.path.join(repo_root(), 'GameClient', 'TwelveJade')


def mask_path(filename):
    """世界 mask 的正式路径（Unity 工程 ArtSource/world/masks/）。"""
    return os.path.join(art_source_root(), 'world', 'masks', filename)


def art_source_root():
    """正式美术源根（Unity 工程内）。所有新产物必须写到这里。"""
    return os.path.join(unity_project_root(), 'Assets', 'ArtSource')


def config_root():
    return os.path.join(unity_project_root(), 'Assets', 'Resources', 'Config')


def tools_log_root():
    return os.path.join(repo_root(), 'Tools', 'logs')


def tools_screenshot_root():
    return os.path.join(repo_root(), 'Tools', 'screenshots')


def legacy_art_source_root():
    """旧仓库根 ArtSource。只读 fallback，禁止写入。"""
    return os.path.join(repo_root(), 'Assets', 'ArtSource')


# ---------------------------------------------------------------- 路径解析
def _norm(p):
    return os.path.normcase(os.path.abspath(p))


def _is_under(child, parent):
    c, p = _norm(child), _norm(parent)
    return c == p or c.startswith(p + os.sep)


def is_legacy_path(path):
    """path 是否落在旧仓库根 ArtSource 下。"""
    return _is_under(path, legacy_art_source_root())


def canonical_path(path):
    """把旧仓库根 ArtSource 下的路径映射到正式根下的对应路径；其余原样返回。"""
    if not is_legacy_path(path):
        return path
    rel = os.path.relpath(os.path.abspath(path), legacy_art_source_root())
    return os.path.join(art_source_root(), rel)


def warn_if_legacy_art_source(path, action='read'):
    """命中旧根时打印 WARNING（读取时使用）。返回 True 表示命中。"""
    if not is_legacy_path(path):
        return False
    sys.stderr.write(
        'WARNING: legacy repo-root ArtSource detected: %s. Canonical path is %s. Do not write legacy path.\n'
        % (os.path.relpath(os.path.abspath(path), repo_root()).replace('\\', '/'),
           os.path.relpath(canonical_path(path), repo_root()).replace('\\', '/')))
    return True


def assert_canonical_output_path(path):
    """写入前断言：输出路径不得落在旧仓库根 ArtSource 下。"""
    if is_legacy_path(path):
        raise AssertionError(
            'Refusing to write outside canonical ArtSource root: %s.\n'
            'Canonical: %s.\n'
            'This is the T001 migration hazard: do not recreate repo-root Assets/ArtSource.'
            % (path, canonical_path(path)))
    return os.path.abspath(path)


def resolve_repo_path(path, allow_missing=False):
    """把配置里的路径解析成绝对路径。

    配置统一写"仓库相对路径"（以 GameClient/ 开头）。为兼容历史配置，
    同时接受 Unity 工程相对路径（以 Assets/ 开头）与绝对路径。

    allow_missing=False 时，若路径不存在会依次尝试旧仓库根 fallback；
    命中旧根会输出 WARNING 并把调用方引导到正式路径。
    """
    if not path:
        return path
    if os.path.isabs(path):
        return path
    rel = path.replace('/', os.sep).replace('\\', os.sep)
    cand = os.path.join(repo_root(), rel)
    if os.path.exists(cand) or allow_missing:
        return cand
    # 兼容：以 Assets/ 开头 = Unity 工程相对路径
    if rel.startswith('Assets' + os.sep):
        alt = os.path.join(unity_project_root(), rel)
        if os.path.exists(alt) or allow_missing:
            return alt
    # 旧仓库根 fallback（只读）
    legacy = os.path.join(legacy_art_source_root(), rel)
    if os.path.exists(legacy):
        warn_if_legacy_art_source(legacy)
        return legacy
    return cand


def rel_repo(path):
    """转成仓库相对路径（正斜杠），用于报告与配置。"""
    return os.path.relpath(os.path.abspath(path), repo_root()).replace('\\', '/')


def ensure_dir(path):
    """创建目录并断言不是旧根写入。"""
    assert_canonical_output_path(path)
    os.makedirs(path, exist_ok=True)
    return path


if __name__ == '__main__':
    print('repo_root              ', repo_root())
    print('unity_project_root     ', unity_project_root())
    print('art_source_root        ', art_source_root())
    print('config_root            ', config_root())
    print('tools_log_root         ', tools_log_root())
    print('tools_screenshot_root  ', tools_screenshot_root())
    print('legacy_art_source_root ', legacy_art_source_root(),
          '(exists)' if os.path.isdir(legacy_art_source_root()) else '(absent)')
