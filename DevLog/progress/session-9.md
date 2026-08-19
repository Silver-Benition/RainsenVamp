# Session 9 · 玩家生命、敌群碰撞与游戏流程闭环

> **本次性质**：补齐玩家受击与生命表现、敌群实体碰撞、暂停和游戏结束流程。
> **日期**：2026-08-20
> **状态**：已实现并通过 C# 编译与场景引用检查；玩家受击、敌群分离、防挤压、血条、暂停、死亡与重开均已由老大在 Play Mode 验证。

## 一、本次完成内容

| 类别 | 项目 | 状态 |
|---|---|---|
| 玩家生命 | 100 点生命、伤害入口、0.5 秒全局无敌帧、死亡事件 | ✅ |
| 接触伤害 | 敌人持续接触玩家时由 EnemyBase 请求 collisionDamage | ✅ |
| 阵营过滤 | 玩家实现 IDamageable 后，己方五类武器仍只命中 Enemy Layer | ✅ |
| 受击表现 | 有效伤害触发角色短暂红色染色 | ✅ |
| 像素血条 | 人物下方黑框、红底、绿条的 World Space Slider | ✅ |
| 血条定位 | 使用每个角色独立的 HealthBarAnchor，不再猜测贴图视觉中心 | ✅ |
| 敌群碰撞 | Dynamic Rigidbody2D、零摩擦材质和实体 Collider 形成物理分离 | ✅ |
| 玩家抗挤压 | 玩家质量提高到 50，使用 Continuous 检测并移除冗余 CircleCollider2D | ✅ |
| 手动暂停 | Escape 切换暂停，提供继续和重新开始按钮 | ✅ |
| 游戏结束 | 生命归零后停控、冻结时间、显示 Game Over 与重新开始按钮 | ✅ |
| 暂停协调 | LevelUp、Manual、GameOver 三种原因统一管理 Time.timeScale | ✅ |

## 二、玩家受击链路

```text
EnemyBase.OnCollisionStay2D
        ↓ Player Layer 过滤
DamageTargetFilter.TryGetPlayerDamageable
        ↓
PlayerHealth.TakeDamage
        ├─ 生命扣减与 0.5 秒全局无敌帧
        ├─ Damaged → PlayerDamageFeedback 红色染色
        ├─ HealthChanged → PlayerHealthBarUI 更新 Slider
        └─ Died → GameFlowManager 进入 GameOver
```

- WeakEnemy_1 当前 collisionDamage 为 10。
- PlayerHealth 当前 maxHealth 为 100，invulnerabilityDuration 为 0.5 秒。
- 多个敌人或多个 Collider 在同一无敌窗口内重复请求伤害时，只会生效一次。
- 当前实现是“持续接触检测 + 离散无敌帧伤害”，不是按秒连续扣血。

## 三、敌群碰撞和玩家物理状态

### EnemyWeak_1

- GameObject 使用 Enemy Layer。
- Rigidbody2D 改为 Dynamic，Gravity Scale 为 0。
- EnemyBase 在 FixedUpdate 中写入朝向玩家的目标 velocity，让 Physics2D 负责接触求解。
- CapsuleCollider2D 使用 EnemyCrowd PhysicsMaterial2D；Friction 和 Bounciness 都为 0。
- 对象池取出和回收时都会清空线速度与角速度，避免旧动量污染下一次生命周期。

### Player

- Rigidbody2D：Dynamic、Mass 50、Gravity Scale 0、Interpolate、Continuous、Freeze Rotation。
- 保留一个非 Trigger CapsuleCollider2D 作为玩家实体碰撞体。
- 删除原有冗余 CircleCollider2D，避免多个实体 Collider 共同参与接触求解。
- 高质量玩家刚体使普通敌群更难推动玩家；掩体仍由物理求解阻挡玩家，不再容易被敌人挤入缝隙。

### 当前世界线限制

未激活世界线会关闭敌人的 Collider 和 Renderer，但 EnemyBase 与 Rigidbody2D 仍继续追踪玩家。敌人失去相互碰撞后可能重叠，重新激活 Collider 时会被 Physics2D 集中分离，产生短暂“天女散花”现象。本次没有继续优化该跨世界线表现，当前优先保证单一激活世界的正确性。

## 四、角色血条最终状态

- 使用 PlayerHealthBarCanvas（World Space Canvas）挂在 Player 下方。
- Canvas Scale 为 1/32，Pixel Perfect 开启。
- 外框尺寸 18×4 像素；内部有效条尺寸 16×2 像素。
- Slider 范围为 0 至 16，Whole Numbers 开启，保证填充边缘落在整数像素列。
- 黑色 Border、红色 Background、绿色 CurrentHealthFill；不显示具体生命数值。
- HealthBarAnchor 位于 Player 根节点下，当前局部位置为 `(0.0625, -0.625, 0)`，即原始朝向右移 2px、下移 20px。
- PlayerHealthBarUI 在角色 flipX 时镜像锚点 X，并按当前 Sprite PPU 吸附位置。

本次先后验证了 Transform/Pivot、Renderer Bounds 和 Tight Mesh 等自动中心思路。它们只能得到几何中心，无法理解人物主体的视觉权重。最终改为每个角色配置一个 HealthBarAnchor，以一次性人工配置换取稳定、可控且无动画抖动的结果。

## 五、暂停与游戏结束

GameFlowManager 使用 Flags 位掩码保存三类暂停原因：

- LevelUp：升级选择期间冻结游戏。
- Manual：按 Escape 或点击按钮进入/解除手动暂停。
- GameOver：玩家死亡后的不可恢复暂停，只有重开场景才能退出。

当任意暂停原因存在时 Time.timeScale 为 0；只有所有原因都解除后才恢复为 1。LevelUpManager 已改为通过 GameFlowManager 请求和释放升级暂停，避免某个界面错误解除另一个界面的暂停。

玩家死亡时：

1. 玩家 Rigidbody2D 速度归零。
2. PlayerController 被禁用。
3. 升级面板和暂停面板关闭。
4. GameOverPanel 显示。
5. Time.timeScale 设为 0。

重新开始按钮会先恢复 Time.timeScale，再重新加载当前场景。

## 六、验证状态

- 已静态检查：玩家/敌人 Layer、Prefab 组件、PhysicsMaterial2D、场景组件引用和本地 fileID。
- 已编译验证：Assembly-CSharp 通过 Visual Studio MSBuild 编译，0 个错误。
- 编译保留 4 个既有 CS0649 警告，均来自 WorldLineCoordinator.WorldSlot 的序列化字段，不是 Session 9 新增问题。
- 已场景检查：MainLevel 没有重复本地对象 ID，也没有失效本地引用。
- 已运行验证：老大确认玩家受击扣血、血条比例、角色受击表现、敌群分离和玩家防挤压有效。
- 已运行验证：老大确认生命归零、Game Over、Escape 暂停、继续游戏和重新开始功能通过。
- 已运行验证：老大确认最终 HealthBarAnchor 位置比自动视觉中心方案更自然。
- 未验证：高敌人数下大量 Dynamic Rigidbody2D 接触的 Physics2D Profiler 基线。
- 未验证：未激活世界线持续隐藏较长时间后的无聚集/无爆散方案。

## 七、当前已知限制

1. 当前接触伤害依赖 0.5 秒全局无敌帧；《吸血鬼幸存者》式按帧连续掉血方案仅完成设计讨论，未实现。
2. 隐藏世界线关闭敌人 Collider，因此无法维持敌群实体分离；重新激活时可能瞬间散开。
3. 每种新角色都需要配置自己的 HealthBarAnchor；自动几何中心不能替代美术判断。
4. 暂停和 Game Over 界面是 MVP 场景 UI，尚未接入正式本地化、动画、音效和局内结算数据。
5. Dynamic Rigidbody2D 敌群适合当前规模；海量敌人阶段仍需通过 Profiler 决定是否改为自定义邻域分离。
6. 五武器 Lv.1/Lv.5 完整回归仍未执行，本次继续暂缓。

## 八、下一步 Todo

### MVP 回归

- 使用 F8 面板完成五把武器 Lv.1 / Lv.5 的固定回归清单。
- 用单只、多只和包围三种敌群测试接触伤害节奏、无敌窗口和死亡触发。
- 复测玩家贴近掩体时的敌群挤压，并覆盖不同方向和狭窄地形。
- 为后续新角色验证 HealthBarAnchor 的左右镜像和不同身高适配。

### 工程完善

- 决定是否切换为连续接触 DPS：按 FixedUpdate 累计伤害，同时把受击表现频率与逻辑扣血频率解耦。
- 为 PlayerHealth、DamageTargetFilter 和 GameFlowManager 补充 EditMode/PlayMode 测试。
- 评估隐藏世界线的低成本分离策略，例如保留专用碰撞层、重新激活前预分离或限制隐藏敌群密度。
- 将暂停、Game Over 和按钮文案接入本地化键值系统。
- 把角色生命、受击配置和 HealthBarAnchor 纳入正式角色 Prefab 工作流。

### 长期扩展

- 增加复活、结算、返回标题和局内统计流程。
- 把玩家最大生命、防御、受伤倍率和恢复能力接入角色属性与升级系统。
- 在目标敌人数下建立 Dynamic Rigidbody2D、接触回调和 GC Alloc 性能预算；必要时迁移到空间哈希或自定义群体分离。
