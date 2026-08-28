# Session 13 进度归档：角色属性系统阶段二

- 日期：2026-08-28
- 状态：阶段二九项属性已接入正式玩法；三项人工反馈已修正并复测通过。
- 范围：Luck、Greed、Curse、Revival、Reroll、Skip、Banish、Charm、Defang，以及金币、宝箱、敌人快照、局内状态和升级面板操作栏。

## 已完成

| 模块 | 当前结果 |
| --- | --- |
| 局内权威状态 | 新增 RunState，集中保存击杀、金币、Revival/Reroll/Skip/Banish 剩余次数和本局 Banish 集合。 |
| Luck | 使用加权无放回抽样影响升级候选；使用互补概率公式影响金币与宝箱掉率。 |
| Greed | 金币在实际拾取时按最终 Greed 倍率结算，并更新 RunState 与 HUD。 |
| Curse | 同时影响敌人生命、速度、伤害、生成速率和有限并发上限。 |
| Charm | 每个整数点提高生成速率并增加有限并发容量；双世界和兼容波次入口使用同一公式。 |
| Defang | 出生时按概率生成绿色敌人，接触伤害和敌方飞行道具伤害统一归零；池化回收后清除颜色与快照。 |
| Revival | 死亡后正常进入 Game Over；存在次数时显示可选复活按钮，确认后播放非缩放时间动画、半血复活并获得保护时间。 |
| Reroll | 升级面板可消费次数重新生成合法候选。 |
| Skip | 消费次数后放弃本次升级机会并继续处理升级队列。 |
| Banish | 进入放逐模式后选择一个候选；放逐与领取升级二选一，并在本局后续候选中过滤稳定 ID。 |
| 掉落物 | 新增池化金币和宝箱；经验球、金币共用 IMagneticPickup 磁吸协议。 |
| QA 工具 | Run-ProjectChecks 等待 XML 完整可解析，并允许全新隔离工程最多五分钟导入大型字体资产。 |

## 关键运行链路

### 升级候选

1. LevelUpManager 从未满级、未 Banish 的升级数据构造合法池。
2. UpgradeCandidateResolver 按 `baseWeight × Luck^luckInfluence` 无放回抽取。
3. Reroll 重新抽取，Skip 直接结束，Banish 记录稳定 ID 后结束本次机会。
4. 宝箱只从合法武器升级池抽取一个即时奖励，不占用等级提升队列。

### 敌人生成与 Defang

1. WaveManager 或 WorldWaveManager 读取玩家最终 Curse、Charm 和 Defang。
2. EnemySpawnSnapshotFactory 在生成瞬间建立不可变生命、速度和伤害快照。
3. EnemyBase 只消费本生命周期快照；已经生成的敌人不受之后属性重算影响。
4. 远程敌人应在创建 EnemyProjectile 后立即调用 Launch；投射物在发射时复制来源敌人的最终伤害。
5. 敌人回池后进入零移动、零输出的停用状态，防止同一物理步延迟碰撞回调造成伤害。

### 死亡与复活

1. PlayerHealth 归零后发布 Died。
2. GameFlowManager 总是进入 Game Over、冻结时间、停止控制并显示死亡面板。
3. 如果 RunState 仍有 Revival，运行时复制现有死亡按钮样式并显示“复活 ×次数”。
4. 玩家点击后才消费次数；使用 unscaledDeltaTime 播放约 0.85 秒缩放、透明度和青色脉冲动画。
5. 动画结束后以 50% 生命复活，默认获得 2 秒受击保护，再解除 Game Over 暂停。

## 数据与默认平衡

- 默认角色和蓝衣战士继续保持 Luck=1、Greed=1、Curse=1，其余阶段二属性为 0，不改变现有正式平衡。
- 五个升级资产已配置稳定 upgradeID、基础权重和 Luck 影响系数。
- WeakEnemy_1 使用 WeakEnemyDropTable：基础金币概率 12%、基础宝箱概率 1%、单枚金币基础价值 1。
- TreasureChestPickup 当前使用橙色金币 Sprite 作为临时占位美术，后续可无损替换 Prefab 表现。

## Unity 等价配置说明

- `Assets/Data/WeakEnemy_1.asset`：启用 `canBeDefanged`，并绑定 `WeakEnemyDropTable.asset`。
- `WeakEnemyDropTable.asset`：绑定 `CoinPickup.prefab` 与 `TreasureChestPickup.prefab`。
- 两个拾取 Prefab 位于 Pickup Layer，根节点带 Trigger Collider2D 和对应 IPoolable 组件。
- 升级操作栏无需修改 MainLevel 场景；LevelUpManager 首次打开面板时运行时建立三个按钮。
- 复活按钮无需修改 MainLevel 场景；GameFlowManager 在 Awake 中复制 GameOverRestartButton 风格，只有可复活死亡时才显示。
- F9 属性调试面板中 Luck/Greed/Curse 可使用 Multiply；默认值为 0 的 Revival/Reroll/Skip/Banish/Charm/Defang 必须使用 Flat。

## 验证状态

### 老大人工验证

- 阶段二属性已通过游戏内 F9 面板逐项测试。
- Defang=1 时绿色敌人持续包围不再造成异常扣血。
- Revival 已符合“正常死亡面板 → 主动选择复活 → 动画 → 半血返回”的预期。
- Banish 已符合“放逐与领取升级二选一”的预期。

### 自动化验证

- Unity：2022.3.62f3c1。
- EditMode：48/48 Passed。
- PlayMode：19/19 Passed。
- 编译错误、空引用异常和断言异常扫描：0。
- Meta 缺失：0。
- `git diff --check`：通过，仅存在仓库行尾转换提示。
- 报告：`Logs/Automation/20260828-215738-phase2-feedback-fixes/summary.json`。

## 已知边界

- 当前没有正式远程敌人和射手 Prefab；EnemyProjectile 与 Defang 发射快照已实现并自动化验证，第一种远程敌人接入时仍需真实射击手测。
- 宝箱仍是临时占位美术，尚无打开动画、多奖励或稀有度演出。
- 当前正式角色的阶段二属性保持中性；正式角色差异将在阶段三内容配置中决定。
- RunState 是单局状态，不负责跨局账号持久化。

## 下一阶段 Todo

### 阶段三基础设施

- 建立版本化账号存档与进度服务。
- 保存账号金币、已解锁角色、收藏发现、Seal 状态和最近选择角色。
- 定义损坏存档回退、迁移和重置流程。

### 阶段三角色内容

- 为 CharacterDataSO 配置起始武器和角色被动。
- 在角色选择页展示真实起始武器、被动和解锁条件。
- 接入角色解锁规则与持久化状态。

### 阶段三收藏与 Seal

- 建立主菜单收藏入口与角色、武器、升级项目详情。
- 使用 upgradeID 保存跨局 Seal，并在普通升级和宝箱候选中统一过滤。
- 明确 Seal 数量上限、蓝衣战士解锁条件和收藏首版分类范围。
