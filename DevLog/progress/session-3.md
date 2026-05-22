# 【开发更新摘要】 - 存档点：Session 3（武器升级系统完整搭建 + 波次刷怪系统）

## 1. 本次新增/修改的 C# 类与核心方法

### [武器数据层]
- **WeaponDataSO.cs (重构)**
  - 废弃原有线性成长公式，改为 `List<WeaponLevelData>`，每级独立配置（索引0=Lv1，索引1=Lv2...）
  - 提供 `MaxLevel` 属性与 `GetLevelConfig(level)` 安全读取（越界自动回退最后一级）
  - 新增 `WeaponLevelData`（每级独立参数）：
    - 基础数值：damage / cooldown / projectileSpeed / pierceCount / lifeTime / auraRadius
    - 多发参数：projectileCount / spreadAngle
    - 弹射参数：bounceCount / bounceMode（BounceMode 枚举：None/Directional/Tracking）
    - 功能标签：`List<WeaponFeatureType>`（预留机制型升级挂载口）
  - 新增 `BounceMode` 枚举（指向性弹射/追踪性弹射/不弹射）
  - 新增 `WeaponFeatureType` 枚举（AuraPersistent / MultiShot / Ricochet / ExplodeOnHit）

- **UpgradeDataSO.cs (修改)**
  - 新增 `WeaponRuntimeType` 枚举：Projectile / Aura
  - 新增 `runtimeType` 字段（决定发放武器时挂载哪种运行时脚本）
  - 新增 `List<LevelUpgradeDesc> customLevelDescs`（按等级自定义升级描述，留空则自动生成数值 diff）

### [运行时武器层]
- **WeaponBase.cs (修改)**
  - 等级成长逻辑改为直接读取当前等级 WeaponLevelData，不再走固定公式
  - 新增 `TryLevelUp()` / `IsMaxLevel` / `CurrentLevel` / `MaxLevel`
  - 新增 `HasFeature(WeaponFeatureType)`
  - `Attack()` 支持多发（projectileCount + spreadAngle 均匀角度分布）+ 弹射参数透传给 ProjectileBase

- **AuraWeapon.cs (重构)**
  - 从"挂玩家身上硬耦合"改为由 LevelUpManager 按 runtimeType=Aura 动态挂载
  - 改为单实例持有：已有活跃 Aura 则只 Initialize() 刷新，不再重复 Spawn
  - 读取当前等级 auraRadius / lifeTime，支持 AuraPersistent 功能标签（常驻模式）

- **AuraDamageZone.cs (修改)**
  - `Initialize()` 支持传入半径，实时同步到 CircleCollider2D.radius
  - 新增双层可视化支持：rangeVisualRenderer 自动按 collider 世界尺寸对齐缩放与偏移
  - 新增 drawDebugGizmo 开关：编辑器下绘制真实碰撞半径 Gizmo（青色线框）

- **ProjectileBase.cs (修改)**
  - `Initialize()` 新增快照参数：damage / speed / pierceCount / lifeTime / bounceCount / bounceMode
  - 弹射逻辑实装（Directional 模式：命中后转向最近未命中目标）

### [升级派发与 UI 层]
- **LevelUpManager.cs (重构)**
  - ownedWeapons 改为 `Dictionary<string, WeaponBase>`（weaponID 为 key）
  - 按 runtimeType 动态挂载 WeaponBase 或 AuraWeapon
  - 重复选中同一武器：执行 TryLevelUp() 升级等级，不再跳过
  - 候选池过滤：满级武器不再进入候选
  - 新增 `RegisterDefaultWeapons()`：Start 时扫描玩家身上预置武器，注册进 ownedWeapons
  - 新增 `GetOwnedWeapon()` 供 UpgradeUIItem 查询当前等级

- **UpgradeUIItem.cs (重构)**
  - `Setup()` 区分首次获得（显示武器描述）与升级（显示数值变化）
  - 自动生成升级 diff 文本（伤害/冷却/穿刺/速度/发射数/弹射/光环范围），优先读 customLevelDescs

### [波次刷怪系统]
- **WaveConfigSO.cs（新增）**
  - 数据驱动时间轴：duration + `List<SpawnRule>`
  - SpawnRule：enemyPrefab / startTime / endTime / spawnsPerSecond / maxAlive / spawnRadiusMin/Max

- **WaveManager.cs（新增）**
  - 积分速率器：acc += spawnsPerSecond * deltaTime，acc>=1 刷一只
  - maxSpawnPerRulePerFrame 单帧刷怪上限（默认3），防卡顿补偿时单帧尖峰
  - aliveCounts 并发计数

- **WaveSpawnedNotifier.cs（新增）**
  - 显式追踪模式：`EnableTracking(owner, ruleIndex)` / `DisableTracking()`
  - 防止多生成器共用同一敌人预制体时计数串线

- **PoolManager.cs (修改)**
  - createFunc 中自动 `obj.name = prefab.name`（去掉 Clone 后缀）
  - 新增 Editor 警告：传入的不是 Project Prefab 资产时提示

## 2. 引入的新机制与设计模式

- **数据驱动每级配置架构**：废弃线性公式，改为策划完全自由配置的 levelConfigs 列表。
- **每级数值+功能双通道**：基础属性（数值型）与功能标签（机制型）分轨管理，扩展时互不干扰。
- **光环武器单实例模式**：同一武器只持有一个 Aura 实例，升级时 Initialize 刷新，不累积堆叠。
- **积分速率刷怪器**：平滑支持小数刷怪速率，避免固定计时器的节拍感，并加单帧上限保护。
- **对象池显式追踪计数**：WaveSpawnedNotifier 按来源显式绑定，多生成器共用预制体时计数不串线。

## 3. 已知后续优化项

- [光环范围表现]：双层结构（判定边界层 + 氛围光晕层），需同步制定资源导入规范
- [机制型升级系统]：WeaponFeatureType 框架已就位，待逐步落地具体行为脚本
- [波次刷怪扩展]：WaveManager 待加 `SetWaveConfig()` 热切换入口
- [武器系统解耦（中期）]：引入 WeaponRuntimeController + IWeaponOwner 接口

## 4. 推荐的下一步 Todo List (Session 4)

- [玩法验证]：配置 2~3 把武器的完整 levelConfigs，跑一局验证升级手感与数值曲线
- [X变量基建]：无限履带式背景拼接 + 地图段切换接口
- [机制型升级]：落地首个 WeaponFeature 行为脚本
