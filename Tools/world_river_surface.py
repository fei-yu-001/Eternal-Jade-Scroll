# -*- coding: utf-8 -*-
"""河道表层：世界坐标的浅滩、湿岸与断续岸缘。

问题（2026-11 截图，机位 x1422 / y2788 已锁定）：water.png 以 periodWorldUnits=1024
原尺寸等比方采样，一张 1024² 里的墨团在世界里正好是 300–400 单位的大墨块，铺满
250 宽的河道；decorate_terrain 又沿 waterHalf+4..+20 刷一条 16 单位、每 46 单位打
一道均匀接缝的笔直横带 —— 两条都被当成"结构"读，而不是"水"。

做法：
  - 水按 **横纵 ≈4:1** 的粗细分采样：垂直采样步长是水平的 4 倍，墨团被压成顺岸的长
    细纹，闭合墨团轮廓随之散开；
  - 浓墨只往上抬、不整体提亮：暗部按亮度做软膝，往材质均色靠，淡墨（纸白）留给浅滩；
  - 岸缘不再是一条常量偏移的线：湿岸是一道跨岸线对称的平滑洗（水里渐淡到浅滩，
    岸上收成湿泥），岸笔再压一条自有扰动相位的断续墨缘，外带碎石短划与零星短草；
  - 全部是绝对世界坐标的纯函数，块与块之间同一采样，不会在 chunk 切口露馅；
  - mask（玩法几何）一位不动：扰动只在既有水面边缘的几个单位内重画观感边，
    桥与渡口按原条件整段让开。

调用方：compose-world-chunks.py（取样八度）与 world_elevation_art.decorate_terrain
（表层）。两者都不产生逐块状态。
"""
import numpy as np
from PIL import Image

# 三个八度：同一张水墨照片缩到更短周期后互相错位，杀平铺重复。
# 元组 = (相对配置 period 的横向系数, 横向错位, 纵向错位, 权重)，权重合计 1。
OCTAVE_PLAN = ((0.30, 0.0, 0.0, 0.40), (0.46, 613.0, 271.0, 0.34), (0.68, 311.0, 907.0, 0.26))
ANISOTROPY = 4.0          # 纵向采样步长 / 横向采样步长；越大岸纹越长、墨团越扁
MIN_OCTAVE_PIXELS = 32
INK_GAIN = 0.68           # 墨反差保留度；越低压，墨团越退成墨晕
INK_LIFT = 0.32           # 浓墨向材质均色抬升的上限比例（只压暗部）
INK_FLOOR_LUMA = 124.0    # 该亮度以下才算浓墨并开始抬升
INK_FLOOR_SPAN = 62.0
PAPER = np.array((152, 166, 159), dtype=np.float32)      # 淡墨兑水，保留青灰
SILT = np.array((172, 174, 158), dtype=np.float32)       # 贴岸湿泥
RIM = np.array((92, 104, 97), dtype=np.float32)
GRAVEL = np.array((134, 139, 124), dtype=np.float32)
TICK = np.array((106, 120, 98), dtype=np.float32)

# 岸线扰动：周期与河道波浪（980/430）互质，避免和中心线同频。
WOBBLE = ((4.6, 173.0, 0.8), (2.1, 61.0, 2.3), (1.3, 27.0, 4.1))
SIDE_PHASE = (0.0, 1.7)


def _waves(x, waves):
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    for amp, period, phase in waves:
        total = total + amp * np.sin(np.asarray(x, dtype=np.float64) / period + phase)
    return total


def center_y(x, river):
    """与 make-world-mask.py 的 river_center_x 同式（同为弧度、同为累加），不另立真源。"""
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    for wave in river['wave']:
        total = total + wave['amp'] * np.sin(np.asarray(x, dtype=np.float64) / wave['period'] + wave.get('phase', 0.0))
    return float(river['baseY']) + total


def _rough(x, shift=0.0):
    """[0,1] 的坐标纹理，供宽度/浓度用；只求非周期感。"""
    raw = _waves(x, ((0.5, 37.0, 0.4 + shift), (0.3, 13.0, 2.2 + shift), (0.2, 89.0, 5.0 + shift)))
    return np.clip(raw, 0.0, 1.0)


def water_layers(water, period):
    """把同一张水材质缩成三个错位八度，纵向多压 ANISOTROPY 倍。只缩不放。"""
    layers = []
    for factor, off_x, off_y, weight in OCTAVE_PLAN:
        size = max(MIN_OCTAVE_PIXELS, int(round(float(period) * factor)))
        width = min(size, min(water.shape[0], water.shape[1]))
        height = min(max(MIN_OCTAVE_PIXELS // 2, int(round(width / ANISOTROPY))), water.shape[0])
        image = Image.fromarray(np.uint8(np.clip(water, 0, 255)))
        image = image.resize((width, height), Image.Resampling.LANCZOS)
        layers.append((np.asarray(image, dtype=np.float32), weight, off_x, off_y))
    return layers


def sample_water(layers, x, y, base):
    """加权取三度墨；再把浓墨往材质均色抬升、整体压低反差。base 为均色，形状 (1,1,3)。"""
    mixed = np.zeros((x.shape[0], x.shape[1], 3), dtype=np.float32)
    for material, weight, off_x, off_y in layers:
        sample = material[np.mod(y + off_y, material.shape[0]).astype(np.int32),
                          np.mod(x + off_x, material.shape[1]).astype(np.int32)]
        mixed += sample * weight
    dark = np.clip((INK_FLOOR_LUMA - mixed.mean(axis=2, keepdims=True)) / INK_FLOOR_SPAN, 0.0, 1.0)
    mixed = mixed + (base * 1.12 - mixed) * (dark * INK_LIFT)
    return base + (mixed - base) * INK_GAIN


def _window(x, y, terrain, river):
    """返回本窗口的河心线；窗口离河太远返回 None。"""
    reach = float(river['bankHalf']) + 60.0
    center = center_y(x[0], river)
    if y.min() > center.max() + reach or y.max() < center.min() - reach:
        return None
    return center


def dress_river(canvas, x, y, config):
    """浅滩 + 湿岸 + 断续岸缘；只动河道两侧窄带，陆地与桥面保持原样。"""
    terrain = config['terrainShape']
    river = terrain['river']
    center = _window(x, y, terrain, river)
    if center is None:
        return canvas
    out = canvas.copy()
    delta = y - center
    side = delta > 0
    phase = np.where(side, SIDE_PHASE[1], SIDE_PHASE[0])
    near = np.abs(delta)
    edge = float(river['waterHalf']) + _waves(x, WOBBLE) * np.where(side, 0.8, 1.0) + np.where(side, 1.4, 0.0)

    near_bank = near < edge + 26.0

    # 桥、渡口按 mask 的同一条件整段让开（桥面材质会盖住这里）。
    for bridge in terrain['bridges']:
        near_bank &= np.abs(x - bridge['x']) > bridge['half'] + 4
    ferry = terrain['ferry']
    near_bank &= ~((np.abs(x - ferry['x']) < ferry['half']) & side)
    if not np.any(near_bank):
        return out

    rough = _rough(x)
    rough_b = _rough(x + 47.0, 1.3)

    # 连续湿岸：跨岸线对称的一道洗，水里收成浅滩、岸上收成湿泥，没有贴岸切线。
    span = np.clip((edge + 10.0 - near) / 34.0, 0.0, 1.0)
    span = span * span * (3.0 - 2.0 * span)
    inward = np.clip(near / np.maximum(edge, 1.0), 0.0, 1.0)[:, :, None] ** 1.2
    tint = PAPER * (1.0 - inward) + SILT * inward
    ink = np.clip((130.0 - out.mean(axis=2)) / 72.0, 0.0, 1.0)
    wash = span * (0.44 + 0.22 * rough) * (0.7 + 0.3 * ink) * near_bank
    out = out * (1.0 - wash[:, :, None]) + tint * wash[:, :, None]

    # 岸笔：自有扰动相位的断续墨缘，宽度与浓度都随 x 变，整段带豁口。
    line = edge + 0.6 + 2.0 * rough_b
    width = 0.9 + 2.1 * rough
    d = near - line
    profile = np.clip((d + 1.2) / 1.2, 0.0, 1.0) * np.clip((width + 1.2 - d) / 1.2, 0.0, 1.0)
    gaps = np.clip(np.sin(x / 19.0 + phase) * 0.5 + 0.5, 0.15, 1.0)
    rim = profile * gaps * (0.22 + 0.26 * rough_b) * near_bank
    out = out * (1.0 - rim[:, :, None]) + RIM * rim[:, :, None]

    # 碎石短划：长度与间隔都不齐，专治"笔直"。
    chip = np.mod(x + phase * 29.0 + 13.0 * np.sin(x / 47.0), 64.0) < (4.0 + 9.0 * rough)
    stones = near_bank & chip & (near > line + 2.0) & (near < line + 5.0 + 11.0 * rough_b)
    stone_alpha = stones * (0.16 + 0.14 * rough_b)
    out = out * (1.0 - stone_alpha[:, :, None]) + GRAVEL * stone_alpha[:, :, None]

    # 零星短草：岸内侧竖着几根，破掉水平重复。
    blade = near_bank & (np.mod(x + phase * 17.0, 37.0) < 2.0) & \
        (near > line + 4.0) & (near < line + 7.0 + 6.0 * rough)
    blade_alpha = blade * 0.34
    out = out * (1.0 - blade_alpha[:, :, None]) + TICK * blade_alpha[:, :, None]
    return out
