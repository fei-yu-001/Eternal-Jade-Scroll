# 项目协作约定

- 用户斐哥已于 2026-11-19 授权：完成改动并通过相关验证后，及时提交并推送 GitHub 备份，不需要每次再次询问。
- 正式备份分支为 `backup/verified`。首次备份是独立根提交，不继承旧 `feature/front-end` 中含明文密钥的历史；旧历史保留本机，不推送旧分支、不用 `--all` 或强制推送。
- 后续在新工作目录的 `backup/verified` 上正常提交。推送前运行 `python Tools/check-no-secrets.py`，并对本次提交运行 `python Tools/check-no-secrets.py --history --range origin/backup/verified..HEAD`。失败就修复，不绕过扫描。
- 保留用户未提交工作，不修改自动上下文压缩设置。查阅大文件时限定范围，日志与截图留在本地，结论写进文档。
- 当前正式客户端是 Unity 6000.3.25f1；团结引擎鸿蒙迁移必须用独立副本验证，不能直接降级正式工程。
- 服务端使用 MySQL 持久化、HTTPS 和版本化数据库脚本。部署资料见 `GameServer/deploy/README.md`。服务器凭据和数据库备份放仓库外。
