# pi 新模型辅助工单：门禁证据台账

时间：2026-11-05 14:55 Asia/Shanghai。斐哥已恢复pi使用；沿用wH:p2当前模型配置，不改模型/网关/扩展。

## 职责与边界

你只做有界日志核对与台账整理。三位Codex分别负责地图(wH:pE)、角色(wH:pF)、地砖与素材(ground-assets / wH:pM)。原pi地砖任务已正式移交，不得再写地砖脚本或任何资产。

唯一写入目录：Tools/logs/gate-ledger-20261105/**。
其他所有文件只读；不运行任何门禁/安装/生成命令，不开Unity，不改代码、资源、阈值、状态板、技能、模型配置，不Git写操作，不外网，不派其他助手。

## 输入

- Tools/logs/qingshi-map-20261105/** 下的门禁日志与manifest。
- Tools/logs/character-walk-phase2-20261105/shared-gate-results.json 及 gates/**。
- Tools/logs/qingshi-ground-20261105/seamless-report.json 与 audit-existing/** 中的复核结果。
- Tools/logs/temp-chief-audit-20261105/elevation-results.json、elevation-round2-results.json、manifest-results.json。

## 交付

写一个只读源文件的复算脚本及 ledger.json、report.md。台账按地图/角色/地砖/架构独立复验分组，每项记录源路径、最后写入时间、SHA256、原文结果与已记录的退出码（没有则null，不推测）。标明本次快照时间。

同一检查多个轮次按实际mtime和内容选最新证据，不因为文件叫final就认最新；保留旧失败来源供对照。脚本或图片的生成完成、进程退出0、交付校验PASS都不能替代素材/动作/场景通过。角色配色、动作、抠图分别列；运行时替换与Unity验收是否有证据分别列。

并发仍在进行：读前后各取哈希，变化则标unstable并留待复查。没有结果就写无证据，禁止脑补。建议全表控制在20项左右，报告重点指出仍红、证据过期/不稳定和待实机部分。

任务完成后回报台账路径和3至5条事实。无需阅读巨型历史架构文档或继续等待其他线完成。若输入变更，架构师会再派一次刷新。