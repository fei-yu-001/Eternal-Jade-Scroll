# 2026-11-20 鸿蒙工作区与服务端交接

当前正式工作目录为 HarmonyOS/apps/EternalJadeScroll，独立 Git 仓库，分支 backup/verified；首次备份 afe5543 已推送 GitHub。原工程与旧历史保留，不推送其含密钥的 feature/front-end 历史。

工作区根 README.md 集中给出工具、服务器私有连接配置和项目路径。连接后先读 /opt/harmonyos/README.md；游戏服务为 /opt/harmonyos/services/eternal-jade-scroll。服务端 Java21 + MySQL8.4 持久卷 + Nginx HTTPS；云存档客户端已连接 jade.ddyu.online。服务端 68 项测试通过，线上 200KB 存档和账户/令牌通过数据库与 API 重启持久化检查，临时账号已清理，备份已生成并下载本地私有目录。

用户明确要求公开仓库不含密钥。当前四个美术脚本改读 LUPI_API_KEY，原值留在仓库外，未上传。源码快照扫描通过；后续每轮验证后正常 commit/push，并运行 AGENTS.md 中的工作树与提交范围扫描。

团结 1.11.1（2022.3.62t18）安装包及 OpenHarmony 模块已核对官方校验值与签名，模块安装成功；DevEco CLI 1.3.0-stable 可用。编辑器启动尚未核验成功，Windows NetFx3 仍在安装，DevEco Studio/SDK 和华为账号签名未完成。后续读取工作区 tools/README.md 与 tool-status.json 获得当时实际状态，不假定已能打包。

原 Unity6 正式工程不能直接用团结 2022 降级打开，迁移另建实验副本。自动上下文压缩保持用户设置不变。
