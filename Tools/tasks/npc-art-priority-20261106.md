# NPC 美术优先级清单 · 2026-11-06

依据：`Resources/Config/npcs.json`、`town-map.json` 的 id/name/title/art 字段，加 `Resources/Art/Portraits`、`Art/Characters/Stand`、`Art/NPC` 文件名。未看图像内容，不明处写未知。

| 优先 | id | 名字 | 角色身份 | 现有图 | 确定缺口 |
|---|---|---|---|---|---|
| 1 | npc-yao-lao | 药老 | 北山药圃的悬壶郎中 | Portrait: Art/Portraits/npc-yao-lao.png；世界: Art/Characters/Stand/merchant-male-1-{front,side,back}.png | 仅 1 张 portrait，无表情差分；世界图沿用通用 merchant-male-1；图内是否有药篓/药箱未知 |
| 2 | npc-shitou | 石头 | 西巷拾遗队的苦孩子 | Portrait: Art/Portraits/npc-shitou.png；世界: farmer-male-0-{front,side,back}.png | 仅 1 张 portrait，无表情差分；世界图为通用 farmer-male-0；是否带拾遗背篓未知 |
| 3 | npc-zhao-da | 赵大 | 守渡退伍的铁匠 | Portrait: Art/Portraits/npc-zhao-da.png；世界: farmer-male-1-{front,side,back}.png | 世界图为通用 farmer-male-1，非铁匠专属；锤/围裙等细节未知；无表情差分 |
| 4 | npc-shenhuai-an | 沈怀安 | 摸骨测字的老先生 | Portrait: Art/Portraits/npc-shenhuai-an.png；世界: traveller-male-0-{front,side,back}.png | 无表情差分；卦摊/测字道具是否入画未知 |
| 5 | huolang | 货郎老栓 | 走街串巷的独脚货郎 | Portrait: Art/Portraits/huolang.png；世界: merchant-male-0-{front,side,back}.png | 无表情差分；“独脚/货担”是否已体现未知 |
| 6 | yaoshi | 林药师 | 青石镇唯一的坐堂郎中 | 无 Portraits 文件；town-map NPC 列表未放置 | 缺立绘与地图位置；是否启用未知 |

补充事实：
- `Art/NPC` 现有 4 张均为敌对/兽类（bandit-chief、boar-demon、local-thug、stone-mandrill），与上述镇民无关。
- town-map 另有 6 名无 portrait 的镇民（柳嫂、阿杏、何二娘、胡三、阿禾、小满），复用通用 farmer/merchant/traveller 立绘，未列入前 6。
- 上述世界立绘均为 11 名镇民共享的通用 front/side/back 三视图，非角色专属；是否可作对话半身像未知。