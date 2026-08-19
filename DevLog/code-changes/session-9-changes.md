# Session 9 代码改动详情

> **本次性质**：玩家生命与受击、敌群实体碰撞、阵营过滤、像素血条、暂停和 Game Over 流程。
> **日期**：2026-08-20

## 一、新增运行时代码

### Assets/Scripts/Player/PlayerHealth.cs

- 新增 PlayerHealth，作为玩家生命与死亡状态的唯一运行时写入口，并实现 IDamageable。
- maxHealth 默认 100；Awake 初始化本局当前生命、死亡标记和下一次允许受伤时间。
- TakeDamage(float) 转发到包含暴击标记的统一重载。
- TakeDamage(float, bool) 过滤非正数、死亡状态和无敌窗口内的重复请求。
- invulnerabilityDuration 默认 0.5 秒；先写入下一次伤害时间，再发布事件，避免监听者在同帧绕过保护。
- 暴露 CurrentHealth、MaxHealth、NormalizedHealth 与 IsDead 只读状态。
- 暴露三个事件：
  - HealthChanged(currentHealth, maxHealth)：供血条同步。
  - Damaged(appliedDamage)：供受击表现响应。
  - Died：供游戏流程进入 Game Over。

### Assets/Scripts/Player/PlayerDamageFeedback.cs

- 依赖 PlayerHealth，监听 Damaged 事件，不参与伤害数值计算。
- 自动缓存玩家子节点 SpriteRenderer 和原始顶点色。
- 有效受击后短暂使用 `(1, 0.35, 0.35, 1)` 红色染色，默认持续 0.1 秒。
- 新伤害到来时重启协程；组件禁用时停止协程、取消订阅并恢复原色。
- 使用 SpriteRenderer.color 保留现有 URP Sprite-Lit 材质和 2D 光照链路。

### Assets/Scripts/Core/DamageTargetFilter.cs

- 新增静态阵营过滤工具，集中缓存 Player 与 Enemy Layer 编号。
- TryGetEnemyDamageable 供玩家武器只获取敌方 IDamageable。
- TryGetPlayerDamageable 供敌人接触伤害只获取玩家 IDamageable。
- EnemyLayerMask 供 Physics2D 查询直接限定 Enemy Layer。
- 私有 TryGetDamageableOnLayer 先做 Layer 过滤，再检查 Collider 本体和 attachedRigidbody 根节点。
- 热路径不创建集合、不递归 GetComponentInParent，兼容 Collider 位于刚体子节点的 Prefab。

### Assets/Scripts/UI/PlayerHealthBarUI.cs

- 新增事件驱动的世界空间血条表现组件。
- Awake/OnEnable 解析 PlayerHealth、SpriteRenderer、HealthBarAnchor 和 UI 引用。
- HealthChanged 只更新目标比例；Update 使用 unscaledDeltaTime 平滑显示，因此暂停帧仍可完成过渡。
- SetSliderFill 把 0 至 1 生命比例映射到 Slider 的 0 至 16 整数范围。
- ApplyColors 统一应用黑边、红底和绿条颜色。
- LateUpdate 读取角色专属 HealthBarAnchor：
  - 以 PlayerHealth.transform 为镜像根节点。
  - SpriteRenderer.flipX 时反转锚点相对根节点的 X。
  - X/Y 按当前 Sprite pixelsPerUnit 吸附到整数像素。
  - 只在位置实际变化时写 RectTransform，避免无效 UI Dirty。
- 未显式绑定锚点时按约定名称 `HealthBarAnchor` 在玩家直接子节点中查找一次。

### Assets/Scripts/Core/GameFlowManager.cs

- 新增单局流程管理器，当前挂载在 MainLevel 的 GameManager。
- 使用私有 Flags 枚举 PauseReason 保存 LevelUp、Manual、GameOver 三种暂停原因。
- IsPaused 与 IsGameOver 提供只读流程状态。
- Awake 建立单例、恢复 Time.timeScale、解析玩家引用、初始化面板并绑定按钮。
- Update 监听 Escape；升级选择和 Game Over 期间禁止切换手动暂停。
- PauseGame/ResumeGame 只修改 Manual 原因，不会误解除其他暂停来源。
- EnterLevelUpPause/ExitLevelUpPause 为升级系统提供独立暂停入口。
- HandlePlayerDied 清空玩家速度、禁用 PlayerController、关闭冲突面板并显示 GameOverPanel。
- RestartGame 先把 Time.timeScale 恢复为 1，再重新加载当前场景。
- OnDisable/OnDestroy 负责事件、按钮监听和单例清理。

以上新脚本均包含对应 .meta 文件。

## 二、修改的核心代码

### Assets/Scripts/IDamageable.cs

- 接口职责说明增加玩家，明确 IDamageable 现在覆盖玩家、敌人、Boss 和可破坏物。

### Assets/Scripts/Data/EnemyDataSO.cs

- collisionDamage 从预留字段转为正式敌人接触伤害配置，默认值保持 10。
- 为敌人名称键、生命、移动速度、接触伤害和经验掉落补充 Min、Tooltip 与类注释。
- 移除未使用的 Collections 命名空间。

### Assets/Scripts/Enemy/EnemyBase.cs

- Awake 缓存 Rigidbody2D 与可选 HitFlash。
- OnEnable 重置生命、线速度、角速度并解析玩家目标。
- 新增 OnDisable 清空刚体动量，保证对象池复用安全。
- FixedUpdate 从 MovePosition 改为写 Dynamic Rigidbody2D.velocity，由 Physics2D 处理 Enemy 间接触和滑开。
- 新增 OnCollisionStay2D：通过 DamageTargetFilter 获取玩家 IDamageable 并请求 collisionDamage。
- TakeDamage 增加非正伤害和已死亡状态过滤。
- 为生命周期、移动、伤害和死亡方法补齐中文 XML 注释。

### Assets/Scripts/LevelUpManager.cs

- ShowLevelUpPanel 优先调用 GameFlowManager.EnterLevelUpPause，不再直接独占 Time.timeScale。
- OnUpgradeSelected 通过 ExitLevelUpPause 释放升级暂停。
- 缺少 GameFlowManager 的独立测试场景仍回退到旧的 Time.timeScale 行为。

### Assets/Scripts/Weapon/ProjectileBase.cs

- OnTriggerEnter2D 改用 DamageTargetFilter.TryGetEnemyDamageable，避免玩家实现 IDamageable 后被己方火球误伤。
- FindNextBounceTarget 的 OverlapCircleAll 增加 EnemyLayerMask，减少无关候选。
- 弹射候选再次通过统一敌方过滤，兼容 Collider 与刚体根节点分离的结构。

### Assets/Scripts/Weapon/AuraDamageZone.cs

- OnTriggerEnter2D 与 OnTriggerExit2D 改用 TryGetEnemyDamageable。
- 光环目标列表不会加入玩家或其他非 Enemy Layer 的 IDamageable。

### Assets/Scripts/Weapon/OrbitingProjectile.cs

- 旋刃触发伤害改用 TryGetEnemyDamageable。

### Assets/Scripts/Weapon/LobbedProjectile.cs

- 飞斧触发伤害改用 TryGetEnemyDamageable，保留本生命周期已命中 Collider 集合。

### Assets/Scripts/Weapon/MeleeSwingHitbox.cs

- 雨伞挥击触发伤害改用 TryGetEnemyDamageable，保留单次挥击去重逻辑。

## 三、新增与修改的物理资产

### Assets/Material/EnemyCrowd.physicsMaterial2D

- 新增 EnemyCrowd PhysicsMaterial2D。
- Friction = 0，避免敌群互相卡住或拖慢追踪。
- Bounciness = 0，避免接触求解产生弹跳和抛飞。
- 新增对应 .meta，并绑定到 EnemyWeak_1 的 CapsuleCollider2D。

### Assets/Prefab/Enemy/EnemyWeak_1.prefab

- Layer 从 Default 改为 Enemy。
- Rigidbody2D 从 Kinematic 改为 Dynamic。
- Gravity Scale 从 1 改为 0。
- CapsuleCollider2D 绑定 EnemyCrowd PhysicsMaterial2D。
- EnemyBase 继续使用 WeakEnemy_1.asset；该资产当前 collisionDamage 为 10。

## 四、MainLevel 场景改动

### Player 组件

- 新增 PlayerHealth：maxHealth 100、invulnerabilityDuration 0.5。
- 新增 PlayerDamageFeedback，并绑定 PlayerHealth 与 Visuals SpriteRenderer。
- Rigidbody2D Mass 从 1 提高到 50。
- Rigidbody2D Collision Detection 从 Discrete 改为 Continuous。
- 保持 Interpolate、Gravity Scale 0 和 Freeze Rotation。
- 删除冗余 CircleCollider2D，只保留非 Trigger CapsuleCollider2D 作为实体碰撞体。

### 玩家血条层级

```text
Player
├─ Visuals
├─ HealthBarAnchor
└─ PlayerHealthBarCanvas
   ├─ Border
   ├─ Background
   └─ FillArea
      └─ CurrentHealthFill
```

- HealthBarAnchor 局部位置为 `(0.0625, -0.625, 0)`，按 32 PPU 等于右移 2px、下移 20px。
- PlayerHealthBarCanvas 使用 World Space Canvas，Scale 为 0.03125，Pixel Perfect 开启，Sorting Order 为 20。
- Canvas 尺寸为 18×4；Border 占满外框，Background 与 FillArea 各内缩 1px。
- Slider 无 Handle，方向为 Left To Right，Min 0、Max 16、Whole Numbers 开启。
- PlayerHealthBarUI 绑定 PlayerHealth、Visuals SpriteRenderer、HealthBarAnchor、Slider 和三层 Image。

### GameManager 与流程 UI

- GameManager 新增 GameFlowManager，并绑定 PlayerHealth、PlayerController、Rigidbody2D、LevelUpPanel、PausePanel、GameOverPanel 与三个按钮。
- pauseKey 使用 Escape。
- 现有主 Canvas 下新增 PausePanel：标题、ResumeButton、PauseRestartButton。
- 现有主 Canvas 下新增 GameOverPanel：标题、GameOverRestartButton。
- 两个面板默认关闭，由 GameFlowManager 在运行时控制。
- Button 不依赖 Inspector Persistent Call；GameFlowManager 在 Awake 中添加监听并在销毁时移除。

## 五、Unity 手动搭建等价步骤

1. 确认 TagManager 中 Player 为 Layer 3、Enemy 为 Layer 6。
2. 在 Player 上添加 PlayerHealth 与 PlayerDamageFeedback：
   - maxHealth = 100。
   - invulnerabilityDuration = 0.5。
   - PlayerDamageFeedback 绑定 Visuals 的 SpriteRenderer。
3. 配置 Player Rigidbody2D：Dynamic、Mass 50、Gravity Scale 0、Interpolate、Continuous、Freeze Rotation。
4. 只保留一个贴合角色主体的非 Trigger CapsuleCollider2D；删除冗余实体 CircleCollider2D。
5. 创建 EnemyCrowd PhysicsMaterial2D，Friction/Bounciness 均设为 0。
6. 配置 EnemyWeak_1：Enemy Layer、Dynamic Rigidbody2D、Gravity Scale 0、非 Trigger CapsuleCollider2D，并绑定 EnemyCrowd。
7. 在 EnemyDataSO 中设置 collisionDamage；WeakEnemy_1 当前为 10。
8. 在 Player 下创建 HealthBarAnchor，按角色原始朝向摆放视觉锚点。
9. 在 Player 下创建 World Space Canvas 和无交互 Slider：
   - Canvas Scale = 1/32，尺寸 18×4，Pixel Perfect 开启。
   - 黑色外框 18×4，红色底与绿色填充有效区域 16×2。
   - Slider 范围 0 至 16，Whole Numbers 开启，不创建 Handle。
   - 挂载 PlayerHealthBarUI 并绑定数据、锚点和 Image 引用。
10. 在 GameManager 上添加 GameFlowManager，绑定玩家引用、LevelUpPanel、暂停/Game Over 面板和按钮。
11. 在主 Canvas 下搭建 PausePanel 与 GameOverPanel，默认关闭；按钮引用交给 GameFlowManager。
12. LevelUpManager 的暂停和恢复必须通过 GameFlowManager；不要再让多个系统各自直接覆盖 Time.timeScale。

## 六、验证记录

- Assembly-CSharp 通过 Visual Studio 2022 MSBuild 编译，0 个错误。
- 保留 4 个既有 WorldLineCoordinator.WorldSlot CS0649 警告。
- MainLevel 本地对象 ID 唯一，所有本地 fileID 引用均可解析。
- git diff --check 通过，仅输出现有工作区的 LF/CRLF 转换提示。
- 老大在 Play Mode 验证玩家受击、血条、敌群碰撞、防挤压、暂停、死亡、重开和最终锚点位置。
- 未建立大规模敌群的 Physics2D Profiler 数据。

## 七、临时方案、删除项与版本控制

- 本次未删除独立 C# 类。
- PlayerHealthBarUI 曾尝试 Renderer.bounds.center 和 Tight Mesh 顶点中心；两者只能提供几何中心，最终实现已完全移除这些自动推测和缓存逻辑。
- 未开启玩家贴图 Read/Write，也未保留逐像素 Alpha 扫描方案。
- MainLevel 删除一个冗余 CircleCollider2D，最终玩家只使用 CapsuleCollider2D 参与实体碰撞。
- `.vsconfig` 为工作区中未跟踪的外部文件，本次未修改、未纳入 Session 9 功能范围。
- 本次未执行 Git 提交、Plastic Checkin 或远程同步。
