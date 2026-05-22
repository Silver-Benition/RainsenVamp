# 【开发更新摘要】 - 存档点：Session 2（数据驱动与核心战斗循环）

## 1. 本次新增/修改的 C# 类与核心方法

### [性能基建]
- **PoolManager.cs**: 全局对象池单例。核心方法 `Spawn()` 与 `Release()`，基于字典实现多预制体自动分池。
- **IPoolable.cs (接口)**: 规范所有池化对象的回收凭证 `SetPrefabReference()`。

### [战斗与数据层]
- **WeaponDataSO.cs / EnemyDataSO.cs / UpgradeDataSO.cs**: 确立了全模块的 ScriptableObject 数据驱动标准。
- **WeaponBase.cs & ProjectileBase.cs**: 武器发射器与子弹基类。
- **IDamageable.cs (接口)**: 抽象受击逻辑 `TakeDamage()`，彻底解耦攻击方与受击方。
- **EnemyBase.cs**: 怪物基类。实现无重力动态刚体（Dynamic Rigidbody2D）的物理防重叠追踪，以及死亡掉落逻辑。

### [核心循环与 UI]
- **PlayerStats.cs**: 玩家状态中心。引入"升级队列（Queue）"机制防止经验溢出导致的 UI 冲突。
- **ExpGem.cs & PlayerMagnet.cs**: 实现了低性能开销的"两段式物理触发"经验吸附逻辑。
- **LevelUpManager.cs & UpgradeUIItem.cs**: 基于 MVC 架构的三选一升级面板，接管 Time.timeScale 实现游戏时停与恢复，并动态下发武器奖励。

## 2. 引入的新机制与设计模式

- **官方对象池架构 (UnityEngine.Pool)**：彻底消灭 Instantiate/Destroy 带来的 GC 卡顿，支撑海量同屏实体。
- **接口解耦 (Interface-based Design)**：利用 `TryGetComponent<T>(out var)` 结合接口，打破继承树限制，实现极高扩展性的碰撞交互。
- **物理碰撞矩阵隔离 (Collision Matrix)**：在 Project Settings 中严格剔除了经验球与怪物、经验球之间的物理运算，保护 CPU 性能。
- **工业级 UI 适配方案**：引入 TextMeshPro (SDF 矢量渲染) 与 Canvas Scaler (Scale With Screen Size + Match Height)，配合 Layout Group 实现多分辨率下的完美动态排版。

## 3. 推荐的下一步 Todo List (Session 3)

- [架构验证]：基于 WeaponBase 派生近战光环类武器（如大蒜/圣经），验证多态与扩展性。
- [心流控制]：开发数据驱动的波次刷怪系统（Wave Manager），接管目前的无脑定时器。
- [X变量基建]：着手开发核心创新点——"无限履带式"背景拼接逻辑与局内无缝切换地图的底层接口。
