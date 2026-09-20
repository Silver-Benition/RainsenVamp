# Session 23 代码与资产归档：回合流程及局间交互

- 日期：2026-09-20；范围：`37f1a0f..d67c7fa` 的游戏实现及本次文档归档。
- 最终玩家规则与证据见[进度归档](../progress/session-23.md)；各轮差异见[实施计划](../plans/session-23-round-combat.md)。

## 新增核心模块

| 类与路径 | 职责与关键入口 |
| --- | --- |
| `Assets/Scripts/Data/RoundRunConfigSO.cs` | RoundDefinition/RoundObjectiveDefinition 定义时长、截止、Boss、刷怪与额外目标；Validate 检查配置合法性；SO 不存进度。 |
| `Assets/Scripts/Core/RoundRuntime.cs` | RoundRuntime.Tick/Report/Evaluate/TryClose 管理单回合与代次；RunMaterialWallet.Collect/Bag/Credit/Transact 分离余额、储备与交易；RunTransactionEvents 延迟通知到提交后。 |
| `Assets/Scripts/Core/RoundController.cs` | Tick/ReportObjective 驱动目标；SettleRound/AdvanceSettlement 清场与真实时间过渡；ContinueGrowth/ContinueCrates 按顺序处理升级和宝箱；Choose/RerollUpgrade/SkipUpgrade 提交成长；ResolveCrate 处理单箱；BeginNextRound/Finish 控制生命周期。 |
| 同上：ResetPlayerPosition/ResetFollowingCameras | 战斗关闭时清速度、暂关插值，同步刚体与 Transform 并同步物理；通知跟随玩家的 Cinemachine 目标瞬移并失效旧状态，Brain 正常 LateUpdate 重建商店期镜头。首次整局另行初始化，后续不在开波时再传送。 |
| `Assets/Scripts/Core/RoundArena.cs` | 有限矩形约束、运动边界与安全刷怪位置。 |
| `Assets/Scripts/Data/RunShopCatalogSO.cs` | RunShopProduct、RoundStatUpgrade 与商品/属性配置，稳定 ID、名称键与中文回退分离。 |
| `Assets/Scripts/Core/RunShopService.cs` | Enter/FillUnlocked 管理报价、锁定及候选过滤；Buy/Refresh/Banish/Recycle/Combine 原子交易；RefreshPrice 在四格空时归零；操作期间拒绝重入。 |
| `Assets/Scripts/Core/RoundUpgradeRollRules.cs` | 待领取等级推导、普通逐卡品质与十级里程碑保底规则。 |
| `Assets/Scripts/Core/RunCrateReward.cs` | 当前箱奖励与回收报价快照，CrateRewardAction 三种选择；奖励不写回共享资产。 |

## 接入现有系统

- RunDirector 仍是胜败、Boss 与战斗总时钟权威，新增 CompleteRoundRun/PrepareRoundEncounter；WorldWaveManager 接入每波刷怪配置，WorldLineCoordinator 固定主世界并阻止切换。GameFlowManager 区分手动暂停和局间暂停。
- LevelUpManager 持有独立武器顺序表、重复实例、品质与合并；WeaponDataSO 新增 roundTierConfigs。WeaponBase 准备新波冷却，AuraWeapon/OrbitWeapon 停战时清理持续攻击对象。AbilityManager 在回合模式放宽道具种类容量，仍使用既有等级及来源管理。
- PlayerStats 累积 PendingLevelUps 并发布 LevelGained；PlayerHealth.PrepareRound 回满血；PlayerController 接入竞技场边界。EnemyBase/CombatDamageResolver 与回合属性和终局记账协作。
- PoolManager.ReleaseRoundObjects 统一非死亡回池，可暂留回血物；ExpGem 的回合材料价值进入钱包；TreasureChestPickup 战斗只排队；CoinPickup/MapInstantEffectPickup 新增受阶段保护、单次消费的结算入口。
- RunState 原子更新放逐次数与集合，暴露容量；AccountProgressService 的交易通知配合延迟发布，不改账号存档格式。RunTelemetry/RunResultSnapshot/RunResultsUI 增加回合与材料统计，同种武器统计聚合有效伤害/最高品质和战斗生效时间。

## UI 模块

| 类 | 最终职责 |
| --- | --- |
| RoundIntermissionUI | 运行时创建通过蒙版、升级页、宝箱页、四格商店、属性页签、图标库存和详情；交易交由服务；ShowWeapon 区分悬停与点击锁定；仅 Shop 蒙版 Alpha=1。 |
| RoundHoverTarget / RoundShopPresentation | 转发鼠标/导航焦点；共用武器/道具/品质文本格式与可替换本地化入口。 |
| RoundUpgradeIcon | 通过 UI 顶点画升级箭头，显式 CanvasRenderer，无新增位图。 |
| HoldToConfirmButton | 使用 unscaledDeltaTime 的 0.8 秒长按，进度填充不拦截射线；真实按下边沿及持续状态确认，CancelHold 用于换箱等取消。 |
| PlayerLoadoutDisplayUI / PlayerStatBoardUI | 战斗隐藏装备；手动暂停显示右下武器，上方属性；保留事件订阅，隐藏子面板。 |
| GameTimerUI | 显示当前回合/20 与倒计时。 |

## 正式资产与等价 Unity 配置

无需手工补挂即可打开 `Assets/Scenes/MainLevel.unity` 或从主菜单试玩。首版作者工具 `Assets/Editor/RoundCombatSetup.cs` 已生成正式接线，后续 UI 自动构建。

1. RunDirector 同对象增加 RoundController，config 指向 `Assets/Data/Rounds/Standard20.asset`；其 shopCatalog 指向 `ShopCatalog.asset`，20 条回合分别引用 `Waves/Wave01.asset` 至 `Wave20.asset`。最终配置 settlementSeconds=1.5，crateIcon 指向既有 TreasureChest Sprite。
2. 场景新增 RoundArena，size=(24,16)；Ground/Grid/Tilemap 使用 `Tiles/Grass0..3.asset`，TilemapRenderer sortingOrder=-100；四边墙使用既有 Sprite 与 BoxCollider2D。尺寸修改需同步配置、边界、墙与地面。未新增 Layer 或修改全局碰撞矩阵。
3. 两世界 MapStreamManager 禁用，运行初始化保持主世界。既有 Canvas 增加 RoundIntermissionUI，font 绑定 `Assets/Fonts/msyh SDF.asset`；GameTimer 文本宽度适配回合信息。
4. `Aura/Axe/FireBall/Knife/Umbrella.asset` 各增加四档 roundTierConfigs，旧等级数据保留。`WeakEnemyDropTable.asset` 中 CrystalBallPickup 权重置 0，CaptainPickup 保持 4；非回血资产与代码仍保留。
5. 暂停属性区域锚点为 (0.735,0.19)–(0.965,0.865)，武器区域为 (0.735,0.035)–(0.965,0.17)。宝箱回收/禁用使用 HoldToConfirmButton，拿取仍用普通 Button；这些布局与组件由代码创建，无需改 Prefab。
6. 镜头保持场景原 Cinemachine 2.10.6 与 FramingTransposer X/Y 阻尼 0.5，Brain LateUpdate；复位调用 OnTargetObjectWarped 并设置 PreviousStateIsValid=false，不改正常阻尼、死区或时间配置。

`RainsenVampSur/Rounds/Build Session 23` 会重建默认回合表及竞技场，不作为日常启动步骤；已有平衡修改后直接编辑 SO，避免重新生成覆盖配置。敌人、子弹、掉落和效果继续使用原对象池。没有新增外部包、正式 Prefab 改动或账号存档迁移。

## 测试变更与覆盖

新增 RoundCombatTests、RoundCombatPlayModeTests、HoldToConfirmPlayModeTests；更新 MapInstantPickupTests、PlayerAttributeUiTests、AccountShopPlayModeTests、PlayModeTestUtility、PlayerAttributePlayModeTests、PlayerLoadoutDisplayPlayModeTests、RunEndingPlayModeTests、SceneReloadPlayModeTests。

覆盖目标代次/截止、20 回合与最后一波带奖励出口、材料储备、交易重入和观察者异常、武器实例与合并、十级品质保底、共享禁用、宝箱延迟与单次消费、长按取消与连续按住、暂停和局间 HUD、双分辨率布局、详情锁定与实际合并、零余额空店补货、物理/Transform/首发原点和真实镜头复位。20 回合自动化采用加速时钟，不等于自然时长性能试玩。

最终完整 QA 门禁 177/177 与 82/82，图形镜头/首发专项 2/2，报告路径详见进度归档。归档只改文档，沿用被测内容一致的证据。
