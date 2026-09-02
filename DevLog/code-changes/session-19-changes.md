# Session 19 代码变更：Boss 遭遇、胜负冻结与完整结果页

- 日期：2026-09-02
- 基线：`a82eded`
- 实施分支：`codex/session-19-boss-victory-results`
- 最终功能提交：`bf6ed4b`

## Boss 数据与遭遇配置

### BossDataSO / BossEncounterDataSO

- 新增 `Assets/Scripts/Data/BossDataSO.cs`：
  - `BossBarragePhaseData` 保存弹幕数量、间隔、伤害、速度与生命周期。
  - `BossDataSO` 保存稳定 ID、本地化键、显示信息、基础属性、阶段阈值和 Prefab 引用。
  - `CreateSpawnSnapshot()` 生成不受普通敌人波次缩放和 Defang 污染的 Boss 快照。
- 新增 `Assets/Scripts/Data/BossEncounterDataSO.cs`：保存触发时间、生成距离、Boss 数据与 Prefab。
- 新增 `Assets/Data/Boss/ArmedColossus.asset`：
  - 生命 800、移速 0.9、接触伤害 18、不可 Defang。
  - 50% 生命切换第二阶段。
  - 第一阶段：8 向、3 秒间隔、伤害 10、速度 4.5、生命周期 6 秒。
  - 第二阶段：12 向、2 秒间隔、伤害 12、速度 5.5、生命周期 6 秒。
- 新增 `Assets/Data/Boss/ArmedColossusEncounter.asset`：120 秒触发，生成距离 7。

## Boss 运行时与世界归属

### BossEnemyController

- 新增 `Assets/Scripts/Enemy/BossEnemyController.cs`。
- 继承 `EnemyBase`，复用移动、碰撞受击、对象池与生命基础设施。
- `InitializeBoss` 注入本次遭遇数据与 `RunDirector`，重置阶段、动画和弹幕计时。
- `UpdatePhase` 在生命降至 50% 时切换到第二阶段并重启阶段间隔。
- `FireRadialBarrage` 按等角度生成 8/12 向池化 `EnemyProjectile`，不在战斗热路径创建临时集合。
- `Die` 使用 Boss 专属死亡出口：只登记一次击杀、通知 `RunDirector`，不消费普通 `EnemyDataSO` 掉落表。

### WorldEnemySimulation / WorldLineCoordinator

- `WorldEnemySimulation.SpawnBoss` 通过共享 `PoolManager` 生成 Boss，缓存 Collider/Renderer 并登记世界归属。
- `WorldEnemySimulation.SpawnVfx` 管理 Boss 预警 VFX 的生成、世界归属、显隐与回收。
- 新增 `TrackedVfx` 缓存，避免每次切换世界重新扫描组件。
- `WorldLineCoordinator.ActiveWorldSimulation` 提供当前激活世界。
- `SetWorldSwitchLocked` 在 Boss 生成后锁定世界切换，防止玩家通过切换世界绕开遭遇。

### BossWarningVfx

- 新增 `Assets/Scripts/VFX/BossWarningVfx.cs` 与 `Assets/Prefab/VFX/BossWarningVfx.prefab`。
- 使用 0.45 秒对象池生命周期，从 0.35 比例扩张并淡出。
- 记录世界归属，回收时清理计时、缩放、颜色和模拟器引用。

### Boss Prefab

- 新增 `Assets/Prefab/Enemy/BossArmedColossus.prefab`。
- 根节点位于 Enemy Layer，包含 `SpriteRenderer`、Dynamic `Rigidbody2D`、非 Trigger `CapsuleCollider2D` 和 `BossEnemyController`。
- Rigidbody2D：质量 4、重力 0、Continuous 碰撞检测、冻结 Z 旋转。
- 使用既有两帧 Sprite，动画间隔 0.2 秒；Boss 数据引用 `ArmedColossus.asset`。

## 单局导演、胜负与账号结算

### RunDirector

- 新增 `Assets/Scripts/Core/RunDirector.cs`，作为单局生命周期权威组件。
- 独立维护 `ElapsedSeconds`、Boss 是否生成、最终结果是否冻结、临时死亡预览与当前 `RunTelemetry`。
- `TryStartBossEncounter` 在正式游戏时间达到配置值后，仅在当前激活世界生成一次 Boss。
- `NotifyBossDefeated` 在普通路径直接冻结胜利；若仍处于 `CombatDamageResolver` 结算栈，则暂存到 `_pendingBossDefeat`。
- `FlushPendingBossDefeat` 在终结伤害完成记账后提交胜利，避免最后一击从结果中丢失。
- `HandlePlayerDied` 根据剩余 Revival 生成临时失败预览或正式失败结果。
- `FreezeRun` 只执行一次：冻结遥测、复制不可变结果、清除临时预览并交给 `GameFlowManager`。
- `BuildSnapshot` 收集地图、时间、金币、击杀、等级、角色、武器、Items、Abilities 和即时效果拾取物。

### GameFlowManager

- 新增独立 `PauseReason.RunResult`，与升级、手动暂停和 GameOver 分离。
- `EnterRunResult` 停止玩家刚体、禁用移动控制、隐藏其他面板、冻结时间并只提交一次账号进度。
- 重新开始、返回主菜单和应用退出前，若本局尚未结算则请求 `RunDirector.EndRunAsDefeat()`。
- 玩家死亡时优先展示 `RunDirector` 的可复活预览或最终快照，不再与旧 GameOver 面板重叠。
- 复活成功后关闭结果预览，并继续当前局遥测。

### GameTimerUI

- 不再自行累计 `Time.deltaTime`。
- `CurrentTimeSeconds` 与文本统一读取 `RunDirector.ElapsedSeconds`，保证 HUD、Boss 触发和结算使用同一时间源。

## 不可变结果快照与统计

### RunResultSnapshot

- 新增 `RunOutcome`、`RunResultCharacterSnapshot`、`RunResultWeaponSnapshot`、`RunResultAbilitySnapshot`、`RunResultPickupSnapshot` 和总快照 `RunResultSnapshot`。
- 构造时复制所有列表并只暴露 `IReadOnlyList`，避免结果冻结后被运行时对象或外部集合改变。
- 武器快照保存稳定 ID、显示字段、图标、等级、有效命中总伤害、首次获得时刻、有效作用时长与 DPS。
- 能力快照携带显式 `AbilityPresentationCategory`；拾取快照携带稳定 ID、显示字段、排序权重和数量。

### RunResultValueSanitizer

- 新增有限非负浮点清洗、饱和加法、武器有效作用时长和 DPS 计算。
- `CalculateActiveDuration` 使用 `max(0, survivalTime - firstEffectTime)`，异常或未来时刻归零。
- DPS 直接消费冻结后的同一有效时长，避免 UI 时间与统计分母分叉。

### RunTelemetry

- 以武器稳定 ID 使用字典累计有效命中伤害，同时以小型列表保留首次获得顺序。
- 初始武器在 `InitialWeaponsReady` 后以 0 秒登记；运行时新增武器使用正式 `WeaponAdded` 事件登记，升级不会重置首次时间。
- 命中热路径仅执行字典查找与饱和浮点累加；排序和快照分配只在结果冻结时发生。
- 冻结后拒绝迟到的伤害与拾取报告。
- 即时效果拾取物按稳定 ID 聚合，冻结时按 `sortOrder + stableId` 稳定排序。

### CombatDamageResolver / EnemyBase

- 新增 `ICombatDamageTarget` 与不可变 `CombatDamageResult`。
- `CombatDamageResult` 明确区分：
  - `RequestedDamage`：攻击方请求值。
  - `AppliedDamage`：目标接受的有效完整命中伤害。
  - `HealthLost`：受当前剩余生命限制的实际生命减少量。
- 正式武器命中统一经过 `CombatDamageResolver.Apply`，由目标修改生命，由解析器向 `RunTelemetry` 记录 `AppliedDamage`。
- `EnemyBase.ApplyCombatDamage` 保持生命最低为 0，但致死过量伤害的飘字和武器统计不再截断为残余生命。
- 已死亡、已回池、无效数值和最终结果冻结后的迟到命中全部拒绝。
- 使用结算深度保护同步 Boss 死亡回调，确保最终一击先记账再冻结。

## 武器接入

以下武器命中点改用 `CombatDamageResolver`，并持续传递权威 `WeaponDataSO`：

- `Assets/Scripts/Weapon/ProjectileBase.cs`
- `Assets/Scripts/Weapon/AuraDamageZone.cs`
- `Assets/Scripts/Weapon/OrbitingProjectile.cs`
- `Assets/Scripts/Weapon/LobbedProjectile.cs`
- `Assets/Scripts/Weapon/MeleeSwingHitbox.cs`

以下武器控制器在生成攻击对象时补充稳定武器来源：

- `Assets/Scripts/Weapon/OrbitWeapon.cs`
- `Assets/Scripts/Weapon/LobbedWeapon.cs`
- `Assets/Scripts/Weapon/MeleeWeapon.cs`

`WeaponDataSO.GetStableId()` 为已有资产提供稳定 ID 读取入口；缺失时仅以资产名回退。

## 物品与未来拾取接口

### AbilityDataSO / 能力资产

- 新增 `AbilityPresentationCategory.Item / Ability`。
- 6 个正式能力资产写入显式分类，结果页不再依赖 mechanic 类型猜测展示区域。
- 分类只影响结果表现，不改变能力获得、升级、机制执行或六格容量。

### MapInstantEffectPickupDataSO / Reporter

- 新增 `Assets/Scripts/Data/MapInstantEffectPickupDataSO.cs`：保存稳定 ID、本地化键、回退名称、图标与排序权重。
- 新增 `Assets/Scripts/Core/MapInstantEffectPickupReporter.cs`：池对象每次启用后允许在效果真正成功时报告一次。
- 本 Session 不创建具体即时效果资产；经验、金币和宝箱不接入该接口。

## 结果 UI

### RunResultsUI

- 新增 `Assets/Scripts/UI/RunResultsUI.cs`，挂载后在运行时一次性构建 1920×1080 参考分辨率的双栏 UI。
- 左侧基础信息展示地图、生存时间、金币、击杀和等级。
- 武器区使用固定表头和五个独立列对象，名称列可伸缩，等级/伤害/时间/DPS 保持固定列宽。
- 最多创建 6 条武器行；隐藏模板不参与布局。
- 右侧展示角色、Items 与 Abilities；两个网格各自固定 6 列，标题与首行没有矩形重叠。
- Part4 展示地图即时效果拾取次数；没有数据时显示空状态。
- 可复活预览显示复活、重新开始、返回主菜单；最终结果隐藏复活入口。
- 武器时间绑定 `ActiveDurationSeconds`，不再错误显示首次获得时刻。

## 场景修改

- `Assets/Scenes/MainLevel.unity`：
  - 在 `GameManager` 增加 `RunDirector`，绑定 `ArmedColossusEncounter`、`WorldLineCoordinator`、玩家、RunState/LevelUpManager 依赖和地图显示信息。
  - 在主 `Canvas` 增加 `RunResultsUI`。
- 没有新增按键、Layer、Tag 或 Physics2D 碰撞矩阵规则。

## 测试变更

### EditMode

- 新增 `Assets/Tests/Editor/RunEndingTests.cs`，覆盖：
  - 初始/运行时武器首次获得时间、有效作用时长、DPS 与异常浮点边界。
  - 致死过量伤害的 Requested/Applied/HealthLost 分离和迟到命中拒绝。
  - 不可变结果集合复制、即时效果聚合与冻结拒绝。
  - Boss 配置、能力显式分类、Prefab/资产 GUID 与关键序列化绑定。

### PlayMode

- 新增 `Assets/Tests/PlayMode/RunEndingPlayModeTests.cs`，覆盖：
  - MainLevel 真实 Boss 触发、世界切换锁与单次生成。
  - 8/12 向弹幕角度、伤害、速度、生命周期、池回收和再次生成。
  - Boss 致死最终一击先记账，再冻结一次胜利结果。
  - 玩家死亡预览、复活继续、最终失败、重启/主菜单和账号统计只提交一次。
  - 满载 6 武器、6 Items、6 Abilities 的结果布局、表头、列宽、容器矩形和双分辨率缩放结构。
  - 初始武器在 125 秒结果中显示 `02:05`，后续武器显示持有时长。
  - 真实 `EnemyBase -> DamagePopupManager -> PoolManager -> DamagePopup` 过量伤害链显示 150，而非剩余生命 100。
- 修改 `PoolLifecyclePlayModeTests.cs` 和 `PlayModeTestUtility.cs`，补充 Session 19 对象池与运行时类型测试支持。

## 等价手动搭建

1. 创建 `ArmedColossus.asset`，配置稳定 ID、显示名、Boss Sprite、800/0.9/18 基础值、不可 Defang、50% 阶段阈值及两阶段弹幕参数。
2. 创建 `BossArmedColossus.prefab`：Enemy Layer；根节点添加 SpriteRenderer、Dynamic Rigidbody2D、非 Trigger CapsuleCollider2D 和 `BossEnemyController`；绑定 Boss 数据及两帧动画。
3. 在 Boss 数据中绑定既有 `EnemyProjectile_1.prefab` 和 `BossWarningVfx.prefab`。
4. 创建 `ArmedColossusEncounter.asset`，配置 120 秒触发、距玩家 7 单位、Boss 数据和 Prefab。
5. 在 MainLevel 的 `GameManager` 挂载 `RunDirector`，绑定 Encounter、WorldLineCoordinator、PlayerStats、PlayerHealth、LevelUpManager，并设置地图键与“双世界试炼”回退名称。
6. 在 MainLevel 主 Canvas 挂载 `RunResultsUI`；界面层级由脚本运行时构建，无需手工创建各行或网格单元。
7. 6 个能力资产将 `presentationCategory` 明确配置为 Item 或 Ability；不要通过 mechanic 是否为空推断。
8. 不需要新增输入、Layer、Tag 或碰撞矩阵；Boss 弹体、预警和实体继续使用现有对象池与世界归属系统。
9. 测试时可调用 `RunDirector.DebugTriggerBossEncounter()` 跳过 120 秒等待；正式游戏仍按配置时间触发。

## 验证结果

- 静态检查：通过；`git diff --check` 通过。
- 编译验证：通过；Unity 2022.3.62f3c1，`compileErrorDetected=false`。
- 完整质量门禁：`C:\Unity_Project\RainsenVampSur-QA\Logs\Automation\20260902-201555\summary.json`。
  - EditMode：88/88。
  - PlayMode：40/40。
- 独立 Review 复跑：`C:\Unity_Project\RainsenVampSur-QA\Logs\Automation\20260902-202500\summary.json`，PlayMode 40/40。
- 人工验收：Boss、胜负与结果流程、五列武器表、Items/Abilities 布局、武器有效时长、完整过量伤害飘字与统计均通过。
