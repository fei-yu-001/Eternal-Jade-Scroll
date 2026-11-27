# 美术入库台账 · 走帧素材

本文件由脚本生成，**禁止手抄**。判级只认闸门退出码，数字只从闸门输出原样摘录——
台账答「盘上现在是什么状态」，批次日志答「这一轮做了什么」，两者都要有（T126）。

- 生成命令：`python Tools/art-intake-ledger.py`
- 闸门：`Tools/check-walk-sheet-motion.py`（sha256 前 16 位 `a5f5948bec97bdce`）——闸门一变，本台账即过期，重跑。
- 素材目录：`GameClient/TwelveJade/Assets/Resources/Art/Characters/Walk/`
- 盘上 9 张：**PASS 6 / FAIL 3**

| 素材 | 判级 | 帧高 | B 起伏(px) | C 触地变化(%) | D 帧高偏离 | A 姿态幅度 |
|---|---|---|---|---|---|---|
| `farmer-male-0-walksheet-back-v1.png` | PASS | 1025..1037 | 12 | 250.0 | 0.012 | 0.942 |
| `farmer-male-0-walksheet-front-v1.png` | PASS | 1021..1035 | 14 | 728.6 | 0.014 | 0.864 |
| `farmer-male-0-walksheet-side-v1.png` | FAIL | 1010..1056 | 32 | 0.0 | 0.044 | 1.000 |
| `merchant-male-1-walksheet-back-v1.png` | PASS | 994..1027 | 33 | 63.3 | 0.033 | 1.000 |
| `merchant-male-1-walksheet-front-v1.png` | PASS | 988..1021 | 33 | 10.9 | 0.033 | 0.328 |
| `merchant-male-1-walksheet-side-v1.png` | FAIL | 1011..1013 | 2 | 41.8 | 0.002 | 0.726 |
| `traveller-male-1-walksheet-back-v1.png` | FAIL | 1013..1015 | 2 | 27.8 | 0.002 | 0.736 |
| `traveller-male-1-walksheet-front-v1.png` | PASS | 1009..1015 | 6 | 59.0 | 0.006 | 0.769 |
| `traveller-male-1-walksheet-side-v1.png` | PASS | 1005..1016 | 11 | 29.7 | 0.011 | 0.673 |

## 闸门 RESULT 行原文（判级凭据）

```text
[rc=0 farmer-male-0-walksheet-back-v1.png] RESULT: PASS  （起伏 12 >= 3 ／ 触地分布变化 250.0% >= 10.0% ／ 帧高偏离 0.012 <= 0.14 ／ 参考姿态幅度 0.942）
[rc=0 farmer-male-0-walksheet-front-v1.png] RESULT: PASS  （起伏 14 >= 3 ／ 触地分布变化 728.6% >= 10.0% ／ 帧高偏离 0.014 <= 0.14 ／ 参考姿态幅度 0.864）
[rc=1 farmer-male-0-walksheet-side-v1.png] RESULT: FAIL  （起伏 32 >= 3 ／ 触地分布变化 0.0% < 10.0% ／ 帧高偏离 0.044 <= 0.14 ／ 参考姿态幅度 1.000）
[rc=0 merchant-male-1-walksheet-back-v1.png] RESULT: PASS  （起伏 33 >= 3 ／ 触地分布变化 63.3% >= 10.0% ／ 帧高偏离 0.033 <= 0.14 ／ 参考姿态幅度 1.000）
[rc=0 merchant-male-1-walksheet-front-v1.png] RESULT: PASS  （起伏 33 >= 3 ／ 触地分布变化 10.9% >= 10.0% ／ 帧高偏离 0.033 <= 0.14 ／ 参考姿态幅度 0.328）
[rc=1 merchant-male-1-walksheet-side-v1.png] RESULT: FAIL  （起伏 2 < 3 ／ 触地分布变化 41.8% >= 10.0% ／ 帧高偏离 0.002 <= 0.14 ／ 参考姿态幅度 0.726）
[rc=1 traveller-male-1-walksheet-back-v1.png] RESULT: FAIL  （起伏 2 < 3 ／ 触地分布变化 27.8% >= 10.0% ／ 帧高偏离 0.002 <= 0.14 ／ 参考姿态幅度 0.736）
[rc=0 traveller-male-1-walksheet-front-v1.png] RESULT: PASS  （起伏 6 >= 3 ／ 触地分布变化 59.0% >= 10.0% ／ 帧高偏离 0.006 <= 0.14 ／ 参考姿态幅度 0.769）
[rc=0 traveller-male-1-walksheet-side-v1.png] RESULT: PASS  （起伏 11 >= 3 ／ 触地分布变化 29.7% >= 10.0% ／ 帧高偏离 0.011 <= 0.14 ／ 参考姿态幅度 0.673）
```

## 待裁决（FAIL 的素材）

- `farmer-male-0-walksheet-side-v1.png`
- `merchant-male-1-walksheet-side-v1.png`
- `traveller-male-1-walksheet-back-v1.png`

<!-- 生成时间：2026-11-27 22:51:04 -->
