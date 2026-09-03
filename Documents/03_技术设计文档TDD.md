# 《十二玉楼长生经》技术设计文档 TDD

> （提取自 look.txt 439-622 行，"文档二"）
>
> 注：本文件是早期技术设计。更细的 Unity 架构 / 服务器架构 / 数据库设计见 `待整理/README.md` 索引（16/14/15 号文档），进入对应阶段时提取。

---

# 一、技术目标

通过项目学习：

- C#
- Unity
- Java
- SpringBoot
- MySQL
- Redis
- Linux
- 网络通信

# 二、系统架构

```
            Unity客户端
                |
            WebSocket
                |
         SpringBoot服务器
                |
        ----------------------
        |          |          |
      MySQL      Redis      Linux
```

# 三、客户端模块（Unity）

```
Client
├── Player
├── Map
├── NPC
├── Quest
├── Battle
├── UI
├── Inventory
└── Network
```

# 四、服务器模块（SpringBoot）

```
Server
├── User模块
├── Character模块
├── Quest模块
├── Item模块
├── Friend模块
├── Rank模块
└── Save模块
```

# 五、数据库设计（核心表）

## 用户表 user

```
id, username, password, create_time
```

## 角色表 character

```
id, user_id, name, level, exp, realm, luck
```

## NPC 表 npc

```
id, name, position, favor, camp
```

## 任务表 quest

```
id, name, type, reward, status
```

> 完整表设计（含索引 / 外键 / 规范化）见待整理 15 号文档，阶段四提取。
