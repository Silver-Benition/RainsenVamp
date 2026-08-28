# Session 13 代码变更：角色属性系统阶段二

- 日期：2026-08-28
- 对应进度：`DevLog/progress/session-13.md`

## 新增运行时代码

### Core

- `RunState.cs`
  - 单局击杀、金币、四类可消耗次数和 Banish ID 的权威来源。
  - 属性容量变化只按差额调整剩余次数，不会因普通重算返还已消费次数。
- `UpgradeCandidateResolver.cs`
  - 定义 IRandomSource、UnityRandomSource 和可确定测试的加权无放回候选算法。
  - 过滤空数据、非正权重和重复稳定 ID。
- `DropChanceResolver.cs`
  - 使用 `1 - (1 - baseChance)^Luck` 修正掉率，并统一边界处理。

### Enemy

- `EnemySpawnSnapshot.cs`
  - 保存单个敌人本生命周期的生命、移动、接触伤害、其他输出倍率和 Defang 状态。
  - EnemySpawnSnapshotFactory 统一 Curse、Charm、Defang 公式。
- `EnemyProjectile.cs`
  - 新增池化敌方飞行道具基础组件。
  - Launch 在发射时复制 EnemyBase.ResolveOutgoingDamage；Defang 来源固定为零伤害。
  - OnEnable/OnDisable 清空动量、寿命和伤害，避免对象池状态泄漏。

### Data

- `EnemyDropTableSO.cs`
  - 配置金币 Prefab、基础概率、基础价值和宝箱 Prefab、基础概率。

### Pickup

- `IMagneticPickup.cs`
  - 抽象经验球和金币共享的磁吸启动协议。
- `CoinPickup.cs`
  - 池化、磁吸、按 Greed 结算并写入 RunState。
- `TreasureChestPickup.cs`
  - 池化宝箱；拾取时请求 LevelUpManager 即时授予一个合法武器奖励。

### UI

- `LevelUpActionBarUI.cs`
  - 运行时构建重掷、跳过、放逐按钮。
  - 展示剩余次数、按钮可交互状态和 Banish 选择模式。

## 修改运行时代码

### 玩家属性与流程

- `PlayerStats.cs`
  - 新增 Luck、Greed、Curse、Revival、Reroll、Skip、Banish、Charm、Defang 只读最终值入口。
- `PlayerHealth.cs`
  - 新增 Revived 事件和 `Revive(normalizedHealth, protectionDuration)`。
- `GameFlowManager.cs`
  - 死亡不再自动消费 Revival。
  - 正常进入 Game Over 后按剩余次数显示运行时复活按钮。
  - `RequestRevive` 点击消费，使用非缩放时间播放复活动画，再半血恢复并解除暂停。

### 升级与候选

- `UpgradeDataSO.cs`
  - 新增 upgradeID、baseWeight、luckInfluence 和 GetStableId。
- `LevelUpManager.cs`
  - 使用 Luck 加权候选；缓存当前候选与玩家 RunState。
  - 接入 Reroll、Skip、Banish 和运行时操作栏。
  - Banish 选择后立即结束本次升级机会。
  - 宝箱奖励只从合法武器候选中抽取。
- `UpgradeUIItem.cs`
  - 候选点击统一转发 HandleCandidateSelected，使普通选择和 Banish 模式走同一入口。

### 敌人、波次与对象池

- `EnemyDataSO.cs`
  - 新增 canBeDefanged 和 dropTable。
- `EnemyBase.cs`
  - 改为出生快照驱动生命、移动和伤害。
  - Defang 使用实例颜色提示，不污染共享材质。
  - 死亡登记 RunState、生成经验与概率掉落并回池。
  - OnDisable 进入零输出安全快照；OnCollisionStay2D 拒绝已禁用或 Defang 实例，隔离延迟物理消息。
- `WorldWaveManager.cs`
  - 双世界生成速率和有限并发上限接入 Curse、Charm。
  - 生成时建立 EnemySpawnSnapshot，并缓存 Prefab 对应 EnemyDataSO。
- `WorldEnemySimulation.cs`
  - TrackedEnemy 缓存 EnemyBase，并在池取出后应用出生快照。
- `WaveManager.cs`
  - 兼容旧波次路径接入同一属性公式、出生快照和 EnemyDataSO 缓存。

### 掉落与 HUD

- `ExpGem.cs`
  - 实现 IMagneticPickup，并补齐池生命周期 XML 注释。
- `PlayerMagnet.cs`
  - 从只识别 ExpGem 改为识别任意 IMagneticPickup。
- `RunStatsUI.cs`
  - RunState 存在时订阅权威状态；独立 UI 测试保留本地后备计数。

## 资产变更

- 新增 `Assets/Data/WeakEnemyDropTable.asset`。
- 新增 `Assets/Prefab/Pickup/CoinPickup.prefab`。
- 新增 `Assets/Prefab/Pickup/TreasureChestPickup.prefab`。
- 修改 `Assets/Data/WeakEnemy_1.asset`，绑定 Defang 与掉落表。
- 修改五个升级资产：
  - FireBall、Knife：基础权重 100，Luck 影响 0。
  - Axe、Umbrella：基础权重 70，Luck 影响 0.5。
  - Aura：基础权重 40，Luck 影响 1。
- 新增目录、脚本、Prefab 和数据资产对应的 Unity `.meta` 文件。

## QA 与工具变更

- 新增 `PhaseTwoAttributeSystemTests.cs`：
  - Luck 掉率和升级权重。
  - 重复稳定 ID。
  - RunState 容量差额。
  - Curse/Charm/Defang 快照。
  - Defang 飞行道具。
  - Revival 主动确认。
  - Banish 与领取升级二选一。
- 新增 `PhaseTwoPoolLifecyclePlayModeTests.cs`：
  - Defang 快照和颜色回池重置。
  - Defang 敌人持续碰撞及接触中停用不伤害玩家。
  - Greed 金币真实物理拾取和回池。
- 修改 `Tools/Run-ProjectChecks.ps1`：
  - 等待 XML 文件完整可解析，不读取正在写入的半成品。
  - 全新隔离工程首次导入等待上限调整为五分钟。

## 等价手动搭建

1. 在 WeakEnemy_1 的 EnemyDataSO 中勾选 `Can Be Defanged`。
2. 创建 EnemyDropTableSO，绑定金币和宝箱 Prefab，并配置 0.12/0.01 基础概率。
3. CoinPickup Prefab 根节点设置 Pickup Layer、CircleCollider2D Trigger、SpriteRenderer 和 CoinPickup。
4. TreasureChestPickup Prefab 根节点设置 Pickup Layer、CircleCollider2D Trigger、SpriteRenderer 和 TreasureChestPickup。
5. MainLevel 不需要新增升级操作栏或复活按钮对象；两者由现有管理器运行时创建并复用当前 TMP 字体、死亡按钮样式。

## 验证结果

- 老大完成全部阶段二手测，并针对 Defang、Revival、Banish 修正版复测通过。
- EditMode：48/48 Passed。
- PlayMode：19/19 Passed。
- 最终报告：`Logs/Automation/20260828-215738-phase2-feedback-fixes/summary.json`。
