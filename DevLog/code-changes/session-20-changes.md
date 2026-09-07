# Session 20 代码变更：地图即时效果、冻结状态与像素显示链

- 日期：2026-09-04
- 基线：`ea17933`
- 实施分支：`main`
- 归档提交：见包含本文件的 `session-20` 提交

## 新增数据与效果策略

### MapInstantEffectSO / MapInstantEffectContext

- 新增 `Assets/Scripts/Data/MapInstantEffectSO.cs`。
- `MapInstantEffectContext` 保存触发玩家的 `PlayerStats` 与 `PlayerHealth`，作为效果执行的不可变上下文。
- `MapInstantEffectSO.TryApply` 是地图即时效果统一策略入口；共享 ScriptableObject 不保存剩余时间或消费状态。

### HealingMapInstantEffectSO

- 新增 `Assets/Scripts/Data/HealingMapInstantEffectSO.cs`。
- `TryApply` 调用 `PlayerHealth.RestoreHealth`，只有实际恢复量大于 0 时返回成功。
- `HealAmount` 与 `OnValidate` 对负值进行钳制。

### WorldFreezeMapInstantEffectSO

- 新增 `Assets/Scripts/Data/WorldFreezeMapInstantEffectSO.cs`。
- `TryApply` 通过 `WorldFreezeController.Instance.TryFreeze` 请求冻结。
- `Duration` 与 `OnValidate` 对负值进行钳制，正式资产配置为 15 秒。

### MapInstantEffectPickupDataSO

- 新增序列化 `effect` 引用与只读 `Effect` 属性。
- 保留稳定 ID、本地化键、中文回退名称、图标和结果页排序职责。

## 新增掉落与拾取运行时

### MapInstantEffectDropResolver

- 新增 `Assets/Scripts/Core/MapInstantEffectDropResolver.cs`。
- `Select` 忽略空条目、空 Prefab 和非正权重，按单位随机数执行一次非负权重选择。
- 明确处理 `unitRoll=1` 的右边界，回退到最后一个有效 Prefab。

### EnemyDropTableSO / EnemyBase

- 新增 `MapInstantEffectDropEntry`、`baseMapInstantEffectChance` 和 `mapInstantEffectDrops`。
- `EnemyBase.SpawnAdditionalDrops` 在金币与宝箱之外独立执行地图道具判定，并继续复用 `PoolManager`。
- `WeakEnemyDropTable.asset` 配置 2.5% 基础概率、舰长权重 4、水晶球权重 1。

### MapInstantEffectPickup

- 新增 `Assets/Scripts/Pickup/MapInstantEffectPickup.cs`。
- 实现 `IPoolable`，缓存 Prefab 键和 `MapInstantEffectPickupReporter`。
- `OnTriggerEnter2D` 只接受碰撞对象本体上的 `PlayerStats`；地图道具不实现 `IMagneticPickup`。
- 每个池生命周期使用 `_consumed` 保证只消费一次；效果成功后才报告统计，最后归还对象池。

## 新增冻结权威与表现

### WorldFreezeController

- 新增 `Assets/Scripts/Core/WorldFreezeController.cs`，并挂载到 `MainLevel/GameManager`。
- `TryFreeze` 取现有剩余时间与新时长的较大值；结算已冻结时拒绝新请求。
- `Update` 使用 `Time.deltaTime` 推进，因此暂停菜单期间自然停止，但不需要修改全局时间缩放。
- `FreezeStateChanged` 只在冻结状态边沿广播，避免表现层逐帧重复工作。
- `CancelFreeze`、`OnDisable` 清除状态和静态实例，避免场景重载残留。

### EnemyBase

- 缓存全部子级 `Animator` 及其初始速度。
- `SynchronizeFreezeState` 在冻结边界保存刚体约束、归零速度并使用 `FreezeAll`；解冻时恢复原约束和动画速度。
- 冻结期间 `FixedUpdate` 不移动，接触回调不造成玩家伤害。
- `RestoreFrozenState` 在回池时清理冻结状态，防止下一生命周期继承。

### EnemyProjectile

- 新增冻结前线速度、角速度与 `_motionFrozen` 状态。
- `SynchronizeFreezeMotion` 只在状态边界保存/恢复动量；稳定冻结帧保持速度为零。
- 冻结期间暂停寿命倒计时和碰撞伤害，解冻后沿原方向继续飞行。

### 其他敌对系统

- `WorldWaveManager.Update`：冻结期间停止波次积分与生成。
- `RangedEnemyController.Update`：冻结期间停止远程攻击计时与发射。
- `BossEnemyController.Update`：冻结期间停止 Boss 阶段攻击逻辑。
- `BossWarningVfx.Update`：冻结期间暂停预警 VFX 计时。

### WorldFreezeOverlay

- 新增 `Assets/Scripts/VFX/WorldFreezeOverlay.cs`。
- 监听 `FreezeStateChanged`，只负责 `SpriteRenderer` 显隐，不参与逻辑与计时。
- `MainLevel/Main Camera/WorldFreezeOverlay` 使用世界 SpriteRenderer：Default Sorting Layer、Order 32000、蓝色 `(0.18, 0.55, 1, 0.32)`。
- 玩家 Sprite 和 `PlayerHealthBarCanvas` 使用 Player Sorting Layer，因此不被蒙版覆盖；Screen Space UI 保持在最上层。

## 拾取动画解耦

### MagneticPickupMotion

- 新增 `Assets/Scripts/Pickup/MagneticPickupMotion.cs` 与 `MagneticPickupMotionState`。
- `StartFlyingTowards` 只接受首次捕获，状态从 `Idle` 进入 `Scatter`。
- `AdvanceScatter` 使用三次缓出插值，先明显离开玩家再减速。
- `BeginHoming` 与 `AdvanceHoming` 从基础速度开始持续加速追踪。
- `CreateFallbackDirection` 和 `CreateSignedUnitValue` 用实例 ID 生成稳定方向，不消费全局随机状态。
- `OnEnable`/`OnDisable` 重置目标、路径、计时和速度，满足对象池复用。

### ExpGem / CoinPickup

- 移除两类脚本各自重复的磁吸字段、`Update` 与 `StartFlyingTowards`。
- Prefab 增加唯一 `MagneticPickupMotion`，逻辑脚本只负责奖励结算与对象池生命周期。
- 正式参数：`scatterDuration=0.18`、`scatterDistance=0.45`、`lateralDriftRatio=0.3`、`baseFlySpeed=5`、`acceleration=15`。

## 玩家生命接口

### PlayerHealth

- 新增 `RestoreHealth(float)`，返回真实增加的生命值。
- 旧 `Heal(float)` 保留并转发到新入口，避免破坏已有调用。
- 满血、死亡或非正数请求返回 0，不发布 `HealthChanged`。

## 正式资源与配置

### 地图拾取物

- 新增 64×64 `CaptainAnchor.png` 与 `CrystalBall.png`，导入为 Sprite Single、PPU 128、Point、Clamp、无 Mipmap、无压缩、透明、无回退物理形状。
- 新增 `CaptainHealEffect.asset`：恢复 45。
- 新增 `CrystalBallFreezeEffect.asset`：冻结 15 秒。
- 新增 `CaptainPickup.asset` 与 `CrystalBallPickup.asset`：稳定 ID、本地化键、中文回退名称、图标、效果和排序。
- 新增 `CaptainPickup.prefab` 与 `CrystalBallPickup.prefab`：ExpGem Layer、SpriteRenderer、半径 0.55 的 Trigger、`MapInstantEffectPickup`、`MapInstantEffectPickupReporter`；不挂 `MagneticPickupMotion`。

### 高清资源显示链

- 金币 Sprite：16×16、PPU 64，显示 0.25 世界单位。
- 经验 Sprite：32×32、PPU 128，显示 0.25 世界单位。
- 两者导入设置统一为 Point、Clamp、无 Mipmap、无压缩、透明、关闭回退物理形状。
- `ExpGem.prefab` 改用正式经验 Sprite、白色 Tint、`localScale=1`；碰撞半径保持 0.025。
- `CoinPickup.prefab` 保持 `localScale=1` 与碰撞半径 0.55。
- Pixel Perfect Camera 保留 Assets PPU 32 和 480×270 参考网格，但使用 Pixel Snapping + Point 直接输出原生分辨率，不再使用低分辨率中间 RenderTexture。

### 能力图标

- 实际覆盖六枚正式 48×48 PNG，保留原 `.meta` 和 GUID。
- 新图标主题分别为拳套冲击、飞翼跑鞋、冷却时钟、磁力核心、燃烧心脏和反击脉冲。
- Alpha 统一离散为 0/255，避免半透明边缘在整数倍放大时发糊。
- `UpgradeButton.prefab/UpgradeIcon` 由 100×100 改为 96×96，48×48 素材按精确 2 倍显示。

### 性能验收计划

- 新增 `DevLog/plans/hd-rendering-performance-validation.md`。
- 记录目标敌群/弹幕/掉落物密度下的 CPU、GPU、GC、Overdraw 和冻结蒙版验收范围。
- 固化决策：性能不达标时先优化热路径与渲染成本，低像素回退必须单独确认。

## Unity 等价手动搭建步骤

1. 在 `Assets/Data/MapInstantEffects` 创建 Healing 与 World Freeze 效果资产，分别配置 45 与 15 秒。
2. 创建两份 `MapInstantEffectPickupDataSO`，填写稳定 ID、本地化键、中文回退名称、64×64 图标和对应 Effect。
3. 创建舰长/水晶球 Prefab，Layer 设为 `ExpGem`，挂 `SpriteRenderer`、`CircleCollider2D(isTrigger=true, radius=0.55)`、`MapInstantEffectPickup` 与 `MapInstantEffectPickupReporter`；不要挂 `MagneticPickupMotion`。
4. 在 `WeakEnemyDropTable` 设置 `baseMapInstantEffectChance=0.025`，依次配置舰长权重 4、水晶球权重 1。
5. 在 `MainLevel/GameManager` 挂唯一 `WorldFreezeController`。
6. 在 `Main Camera` 下创建 `WorldFreezeOverlay`，挂 `SpriteRenderer` 与 `WorldFreezeOverlay`；Sorting Layer 设 Default、Order 32000、蓝色 Alpha 0.32，并显式引用 GameManager 上的冻结控制器。
7. 确认玩家 Sprite 与 `PlayerHealthBarCanvas` 使用 Player Sorting Layer，使玩家与血条位于蓝色蒙版上方。
8. 在经验和金币 Prefab 上各挂一个 `MagneticPickupMotion`，填写 0.18、0.45、0.3、5、15；逻辑脚本不再实现 `IMagneticPickup`。
9. 把金币/经验 Sprite 按 64/128 PPU 导入并保持 Prefab Scale 为 1；不要通过 Transform 缩放补偿尺寸。
10. Pixel Perfect Camera 选择 Pixel Snapping 与 Point，关闭低分辨率 Upscale Render Texture。
11. 六枚能力图标保持 48×48、Point、无压缩、无 Mipmap和原 GUID；把 `UpgradeIcon` RectTransform 改为 96×96。
12. 本次没有新增输入按键、Tag、Layer、Sorting Layer 或碰撞矩阵规则。

## 自动化测试

### MapInstantPickupTests（EditMode）

- 校验舰长/水晶球源图尺寸、PPU、Point、无压缩、无半透明软边与小色板。
- 校验舰长 45 点治疗、水晶球 15 秒冻结、稳定 ID、本地化键、图标和效果绑定。
- 校验满血治疗失败、冻结刷新不叠加、4:1 权重边界与无效条目过滤。
- 校验地图道具不实现磁吸，经验/金币各只有一个磁吸组件。
- 校验 2.5% 掉落表、Prefab 碰撞器、场景冻结控制器和蒙版分层。
- 校验 Pixel Perfect Camera 原生分辨率像素对齐配置。

### MapInstantPickupPlayModeTests

- 验证磁吸拾取物先外飘、后加速追踪并在池复用时重置。
- 验证舰长在磁吸范围内保持静止，只有玩家本体接触才恢复 45 点生命。
- 验证水晶球接触后暂停敌人刚体与动画，解冻后恢复原状态并切换蓝色蒙版。
- 验证冻结期间敌方弹体停止位移与寿命，暂停菜单期间冻结倒计时不推进，解冻后恢复原速度。

### AbilitySystemTests

- 新增升级选项 96×96 容器断言。
- 新增六枚能力图标 0/255 二值 Alpha 断言，防止半透明软边回归。

## 最终验证

- `git diff --check`：通过。
- Unity 2022.3.62f3c1 独立 QA：EditMode 101/101，PlayMode 44/44。
- 报告：`C:\Unity_Project\RainsenVampSur-QA\Logs\Automation\20260904-000839\summary.json`。
- QA 同步 66 个文件，测试前后与主项目哈希差异为 0，QA 最终状态干净。
