# 独立 Linux 服务部署

部署包含 Java 21 API、MySQL 8.4 容器、现有 Nginx 的独立 HTTPS 虚拟主机和每日数据库备份。API 仅监听 `127.0.0.1:18080`，MySQL 仅监听 `127.0.0.1:3316`。不修改宿主机其他项目的容器、数据库或域名配置。

## 构建与安装

1. 在 `GameServer` 运行 `mvnw.cmd verify`（Linux 用 `./mvnw verify`）。必须通过测试。
2. 将 `target/game-server-0.0.1.jar` 重命名为 `game-server.jar`，与本目录的部署文件和 `Database/SQL/V1__user_cloud_save.sql` 放在服务器同一临时目录。
3. 确认域名解析到这台服务器，已有有效证书覆盖该域名，服务器具备 Java 21、Docker Compose v2、Nginx、curl、openssl。
4. 执行 `sudo bash install.sh <唯一发布编号> <游戏域名> <现有证书目录名>`。安装脚本会创建专用系统用户、仅本机可读的随机数据库凭据、持久卷和 systemd 服务；验证 API 后才启用 HTTPS。

鸿蒙相关服务统一登记在 `/opt/harmonyos/README.md`，各项目放在 `/opt/harmonyos/services`。本游戏部署目录为 `/opt/harmonyos/services/eternal-jade-scroll`，当前 JAR 通过 `current.jar` 链接选择；环境配置在 `/etc/twelvejade`，权限 600。MySQL 持久卷名 `twelvejade_twelvejade-db`。不要运行 `docker compose down -v`。应用 profile 固定为 `mysql`，`ddl-auto=validate` 保留启动校验，不能改成自动改表以绕过问题。

宿主机资源预算：API 最大 512MiB、MySQL 最大 512MiB；实际并发承载量需要另做压力测试。

## 更新、验证和回退

每次更新使用新发布编号重新执行安装。已有环境文件、数据库卷不重建，API 验证失败时回退到更新前通过健康检查的 JAR；首次发布失败会停下本项目 API。后续数据库迁移必须单独审阅、备份并按顺序执行版本化 SQL，V1 初始化脚本只在空卷首次启动时执行。

`verify-live.py` 用一个随机测试账号验证 HTTPS、鉴权、200KB 存档、令牌轮换，并重启本项目的数据库和 API 验证持久化；最后删除自己创建的账号和存档，再生成备份。**它会短暂停止本项目服务，应在首次部署或安排好的维护窗口执行。** 需要 Python 的 `requests` 包以及该主机已有的 SSH 权限：

```sh
python verify-live.py --base-url https://GAME_DOMAIN --ssh-host USER@HOST --ssh-key /path/to/key.pem --report /private/path/report.json
```

查看状态：`systemctl status twelvejade-api`；查看日志：`journalctl -u twelvejade-api`。手动应用回退时检查 `/opt/harmonyos/services/eternal-jade-scroll/previous-release.txt` 指向的 JAR，再更新 `current.jar` 并重启 `twelvejade-api`。跨数据库结构变更的回退需另行评估，不能只换 JAR。

## 数据库备份与恢复

`systemctl list-timers twelvejade-backup.timer` 可查看下一次备份。每天 UTC 03:20 起的随机十分钟内运行，保留约 14 天；也可 `sudo /usr/local/sbin/twelvejade-backup` 手工执行。压缩 SQL 保存在 `/var/backups/twelvejade`，生成后做 gzip 完整性检查。

备份恢复前先停 API 并额外备份当前库；确认目标主机和归档后，将解压 SQL 通过容器内 MySQL 客户端导入，完成后再启动 API 和验证账号/存档。归档含 `CREATE DATABASE`/`USE`，不要未经检查导入其他实例。日常自动备份当前只保存在这台主机，应另外定期复制到受控的异地存储；本次部署已下载一份到本地私有目录。备份文件和环境文件不要提交 Git。

## 首次真库问题及验证

2026-11-19 首次启动发现 `@Lob` 把存档列映射为 MySQL tinytext/CLOB，与版本化 DDL 的 LONGTEXT 不一致。现改为普通长文本容量映射，保留 schema 校验；新增 MySQL 方言 DDL 回归测试。服务端共 68 项测试通过，线上 200KB 存档在数据库和 API 重启后逐字段一致。
