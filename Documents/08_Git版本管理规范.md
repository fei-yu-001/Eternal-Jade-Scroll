# 《十二玉楼长生经》Git 版本管理规范

> （提取自 look.txt 8100-8181 行，"29_Git开发规范"）

---

# 一、分支设计

```
main      正式版本
develop   开发版本
feature/* 功能开发
```

**当前实际执行**（个人项目简化版）：

- 现阶段直接在 `main` 上提交（项目还在阶段 0）
- 阶段二开始功能较多时，再启用 `develop` + `feature/*`
- 原则：**每天至少提交一次**，提交信息说清楚改了什么

# 二、提交规范

格式：

```
类型: 内容
```

常用类型：

```
feat:    新功能（如 feat: 新增NPC对话系统）
fix:     修 bug（如 fix: 修复任务无法完成Bug）
docs:    文档（如 docs: 更新设计文档）
refactor: 重构（不改变功能的代码整理）
chore:    杂项（依赖、配置）
```

# 三、版本标签

```
v0.1  青石镇 Demo
v0.2  任务系统
v1.0  正式版本
```

打标签时机：完成一个里程碑（见 00 文档"里程碑"一节）后打 tag。

```bash
git tag v0.1
git push origin v0.1
```

# 四、本仓库信息

- 远端：`https://github.com/fei-yu-001/Eternal-Jade-Scroll.git`
- 主分支：`main`
- 提交身份：仓库级配置（fei-yu-001，noreply 邮箱）
