# Session 27 代码与资产变更

- 基线：`4a687c0059609185bd5ce79a352aa69a7ba435a1`。
- 范围：本地原版查证后的四轮实现及对应测试；归档只补三份文档。
- 120 个实际实现文件（含 `.meta` 和测试）按交付记录完成哈希核验。最终状态见 [进度归档](../progress/session-27.md)。

## 近战动作、跟踪与攻击计时

| 类/文件 | 核心变化 |
| --- | --- |
| `MeleeAttackMotion` / `MeleeMotionPose`（新增） | `Distance`、`FacesRight`、`Evaluate` 输出局部握柄位移与角度。严格按瞄准角定左右，Godot 向下 Y 转为 Unity 向上 Y；横挥两段线性、突刺及前摇/回收缓出 |
| `MeleeAttackTiming` | 三段时长、`Recoil`、`Cooldown`、`RandomizedCooldown`、`Interval`；范围成本进入主动段，正攻速缩短前摇/回收，负攻速保留这两段基础值；冷却按 60 ticks 截断 |
| `MeleeSwingHitbox` | 随所有者挂点运动；持续瞄准出手时目标并验证生命代次，丢失目标保持可用方向；固定刀身、主动段低分配补采碰撞，保留去重/暂停/取消/池清理 |
| `WeaponBase` / `MeleeWeapon` / `ExpansionWeapon` | 出手、动作持续与后续冷却分开；每波首击、随机错峰及原扩展攻击接入，动作结束后扣冷却 |
| `WeaponHeldView` / `WeaponVisualGeometry` / `WeaponTargeting` | 持武到攻击的姿态交接、左右刃向、刀身长度与挂点索敌来源一致 |
| `WeaponDataSO` 与作者工具 | `meleeRecoil`、`meleeWindup`、`meleeHitWidth`、`meleeGripOffset` 等数据接入与导入保持；对应原创近战资产同步 |

横挥行程为 `min(范围, max(250 范围点, 目标距离))`，突刺使用完整范围；项目每范围点为 0.01 世界单位。详情间隔按最大范围动作总时长加未扰动冷却计算，实际横挥目标更近时可以更快。刀身不随范围拉长，后续范围调校仍未实施。

## 经济、奖励与属性计算

| 类/文件 | 核心变化 |
| --- | --- |
| `RunEconomyRules`（新增） | `Price`、`RerollPrice`、`Recycle` 统一商店/宝箱/升级的经济公式 |
| `RunRewardSelection`（新增） | `Pick` 在合法候选集内按品质挑选；道具缺档仅向下回退，不绕过封印/放逐、退池或持有上限 |
| `RunShopService` | 先品质后商品、前期武器保底、上一页排除和安全回退；锁定预留计数；`RecycleValue` 与实际回收共用报价 |
| `RunShopCatalogSO` / `ShopCatalog.asset` | 武器四档独立基础价；保留道具基础价和回收比例，旧刷新价字段仅用于序列化兼容 |
| `RoundController` / `RunCrateReward` | 普通宝箱按品质抽取，计入锁定预留；无合法候选补偿 10 材料。实际升级次数驱动品质，上一页属性家族排除，同波付费刷新累计 |
| `BrotatoStatRules` / `WeaponHitSnapshot` | 基础伤害与属性系数先保底并截断，乘百分比后四舍五入且最低 1；暴击半入取整；负幸运品质使用倒数衰减，护甲/再生用整数点 |
| `PlayerHealth` | 闪避后 0.2 秒保护；现代属性受伤保护按实际损血比例在 0.2–0.4 秒变化 |
| `RoundShopPresentation` / `PlayerStatDescriptions` | 显示共用计算结果；属性说明简化为当前效果；保留本地化键 |

设 `b` 为当前品质基础价、`w` 为刚完成波次、`r` 为本波已付费刷新次数：商品价为 `floor(b+w+0.1*b*w)`，刷新费为 `floor(0.75*w)+max(1,floor(0.4*w))*(1+r)`。回收按当前商品价与目录比例向下取整，至少 1 且不超过商品价；基础价为 1 的商品固定回收 1。商店和属性奖励分别计次，免费重投不抬价。

## 武器标签、羁绊与偏好

- 新增 `WeaponSetSO` 和 `Assets/Data/WeaponSets/` 八套数据；稳定 ID、显示键及 2–6 件各档总收益保存在数据层。
- `WeaponSetBonuses.Rebuild` 在装备变化后按实例重算，以单一属性来源替换羁绊。相同武器副本分别计数；同一武器重复标签只计一次；品质不增加件数，合成减少件数后重新计算。
- `WeaponShopPreference.Roll` / `MatchesOwnedSet` 按同种和共享标签选择。第 1–6+ 波同种概率为 25/24/23/22/21/20%，共享标签分支为 30/27/24/21/18/15%；后者是独立分支的区间宽度，累计分界为 55/51/47/43/39/35%。候选不足仍遵守合法池约束。
- `WeaponSetAuthoring.Resolve`、两组扩展作者工具、武器导入器及两份 definitions.json 保持标签配置一致；22 把配置武器纳入标签，长鞭继续退池。
- `WeaponSetPresentation.Names` / `AllTiers` 共用名称与完整档位文本。

| 标签 | 2 / 3 / 4 / 5 / 6 件总收益 |
| --- | --- |
| 元素 | 元素伤害 +1 / 2 / 3 / 4 / 5 |
| 枪械 | 范围 +10 / 20 / 30 / 40 / 50 |
| 医疗 | 生命再生 +1 / 2 / 3 / 4 / 5 |
| 精准 | 暴击率 +3 / 6 / 9 / 12 / 15% |
| 刀刃 | 近战伤害 +1 / 2 / 3 / 4 / 5，吸血 +1 / 2 / 3 / 4 / 5% |
| 重型 | 伤害 +5 / 10 / 15 / 20 / 25% |
| 原始 | 最大生命 +3 / 6 / 9 / 12 / 15 |
| 工具 | 工程学 +1 / 2 / 3 / 4 / 5 |

## 敌人成长与远程预警

- `EnemyDataSO` 增加逐波生命、伤害增量，`EnemySpawnSnapshotFactory.CreateForRound` 生成不可变快照；`WorldWaveManager` 使用真实波次。出伤快照包含加值，失牙仍优先归零，对象复用不继承旧波状态。
- `WeakEnemy_1.asset`：生命 `floor(6+1.5*(w-1))`、速度 1.8、接触伤害 `1+0.12*(w-1)`。
- `RangedEnemy_1.asset`：生命 `floor(8+1*(w-1))`、速度 1.5、接触伤害 `1+0.05*(w-1)`；弹体伤害 `2+0.08*(w-1)`。
- `Standard20.asset` 及 `RoundCombatSetup` 的普通敌人额外生命/伤害倍率统一为 1，以逐敌种成长为主；刷怪密度和 Boss 独立配置没有在本轮重做。
- `EnemyProjectile` 仅被 Default 层实体 Collider 拦截；Default Trigger 攻击/辅助范围不消弹，玩家命中继续走 `PlayerHurtbox` 过滤。
- `RangedEnemyAttackDataSO.warningDuration` 及正式配置为 0.55 秒。`RangedEnemyController` 用 `_warningElapsed`、`WarningProgress` 在现有首发 0.8 秒/间隔 2 秒的末段推进预警；发射、离开射程和池回收清除状态。
- `EnemyBase` 合成基础/失牙色与预警红，受击闪白继续使用独立表现状态；逻辑不依赖截图或颜色读取。

## 浮层、上一波伤害与回收显示

- 新增 `WeaponDetailSideView`：`Show` 按内容测量完整羁绊窗和独立伤害窗，贴主窗右侧；库存详情空间不足时整体左移并保持顶部对齐。控件初始化后复用，文字和背景不接收射线，避免抢走源标签悬停。
- `RoundIntermissionUI` 在商品的 `WeaponTags` 行挂悬停入口，已持有武器详情同时打开右侧窗；`PauseInventoryView` 复用同类。`Hide` / `Dispose` 随离开、翻页、关闭、恢复战斗及销毁清理。
- `RoundShopPresentation.WeaponDetails` 保留原四参数入口，商店商品使用独立 `WeaponOfferDetails`，共用内部格式化逻辑，避免破坏旧反射调用。
- 新增 `WeaponWaveDamage.Begin` / `Record` / `Complete` / `MergePrevious`，`WeaponBase` 持有实例账本；`RoundController` 开关战斗时开始或封存。
- `WeaponHitSnapshot` 捕获账本和波次令牌；`CombatDamageResolver.ApplyAttributed` 在 Boss 死亡结果收口前记账。原 `Apply` 仍可用；弹体、近战、持续与派生伤害沿快照归属，`EnemyCombatStatus` 灼烧使用 `ApplyDamageOverTime`，保留不暴击/不吸血规则。
- `LevelUpManager.CombineRoundWeapon` 汇总上一波贡献；拒绝伤害、旧波次攻击和结算期请求不进入下一波。该统计不改变原整局遥测口径，不给道具工程建筑绑定武器实例。
- 按钮调用 `RunShopService.RecycleValue` 展示“回收 (+X)”，回收交易使用同一报价，未持有实例不能获得有效报价。

## 等价 Unity 手动配置

1. 在 Project 中打开对应 `WeaponDataSO`，检查 `Melee Pattern`、`Held Size`、`Melee Recoil/Windup/Hit Width/Grip Offset` 和四品质数值。范围只改变行程；Sprite、持武组件、池化攻击 Prefab 沿用现有引用，不另挂运行时账本组件。
2. 为武器的 `Weapon Sets` 数组拖入 `Assets/Data/WeaponSets/` 对应资产。羁绊资产的五档列表依次表示 2–6 件的总收益；调整同一标签数据即可同时影响商店偏好、属性和说明。作者工具及 JSON 同步维护，避免重建时丢失配置。
3. 打开 `Assets/Data/Rounds/ShopCatalog.asset`，武器条目的 `Weapon Tier Prices` 按 I–IV 填四个正整数；道具使用 `Base Price`，`Recycle Ratio` 当前维持 0.25。刷新费由共用规则计算。
4. 打开 `WeakEnemy_1`、`RangedEnemy_1` 配置基础生命/速度/接触伤害及逐波增量；Standard20 普通敌人额外生命/伤害倍率为 1。远程攻击引用仍指向 `RangedEnemyAttack_1`，其 `Warning Duration=0.55`、`First Shot Delay=0.8`、`Cooldown=2`。
5. 弹体、武器攻击继续使用现有池和 Collider；正式玩家 Collider 保留 `PlayerHurtbox`，辅助磁吸范围不加该标记。实体掩体保持非 Trigger，玩家攻击保持 Trigger。本 Session 未改 Layer Collision Matrix、Tag、场景层级或引擎设置。
6. 羁绊及伤害窗由现有 UI 代码在 Canvas 下创建同级浮层，使用现有 TMP 字体、Image 和 Outline，无需在 Scene 手动挂新组件。商店悬停标签、库存悬停武器、Esc 暂停即可复核；F8/F9 沿用已有武器/属性调试入口。

## 测试与工具

- 新增 Editor 测试：`OriginalMeleeTests`、`OriginalCoreRulesTests`、`WeaponSetsAndEnemyWaveTests`、`WeaponWaveDamageTests`。
- 扩展已有属性、内容、工程、远程敌人与回合 Editor 测试，以及武器、工程、远程怪和回合 PlayMode 测试；覆盖真实 MainLevel 流程、投射物穿过攻击 Trigger、预警复用、两种分辨率浮层、合成记账和报价一致性。
- `Tools/Run-ProjectChecks.ps1` 使用隐藏窗口启动并等待进程退出，再读取 XML；缺报告、失败、跳过仍不能判通过。
- 最终 QA 完整结果为 EditMode 258/258、PlayMode 134/134；图形 7/7。报告路径、复用条件和人工边界见 [进度页](../progress/session-27.md)。
