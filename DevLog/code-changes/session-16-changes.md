# Session 16 代码变更：PlayerHurtbox 显式过滤与 QA 协作入口

- 日期：2026-08-30
- 对应进度：`DevLog/progress/session-16.md`
- Session 起始 main：`f16679b70091e332eda027bf399e274eafe25bc3`
- QA 指令基线：`21c991d4fd094f2755940c55fe833d13d0e42e3a`
- 最终批准功能提交：`dfffb2ade56f22d7263050ea7e5e9b75334af43c`
- 从 Session 起始 main 计：11 个文件，259 行新增、12 行删除；其中 `AGENTS.md` 为独立 QA 指令提交，功能提交包含其余 10 个文件。

## 新增运行时代码

### PlayerHurtbox

- 文件：`Assets/Scripts/Player/PlayerHurtbox.cs`
- 使用 `[DisallowMultipleComponent]` 防止重复标记。
- 使用 `[RequireComponent(typeof(Collider2D))]` 表明标记必须与实际 Collider 位于同一 GameObject。
- 组件不保存状态、不执行每帧逻辑，只为物理热路径提供低成本、显式的受击语义。
- 新增对应 `.meta`，场景引用 GUID 为 `9f69a14efccd467fbb00f16f5265dac5`。

## 修改运行时代码

### DamageTargetFilter

- 文件：`Assets/Scripts/Core/DamageTargetFilter.cs`
- `TryGetPlayerDamageable` 首先调用命中 Collider 同对象的 `TryGetComponent<PlayerHurtbox>`。
- 未标记 Collider 无论是否 Trigger 都被拒绝，避免 Player 根节点上的 `PlayerHealth` 使所有子级范围成为受击体。
- 标记通过后继续复用原有 Layer 过滤和 `attachedRigidbody` 根节点解析，不引入父级递归、集合或 LINQ。
- 敌人受击过滤逻辑保持不变。

### EnemyProjectile

- 文件：`Assets/Scripts/Enemy/EnemyProjectile.cs`
- `OnTriggerEnter2D` 移除“Player Trigger 一律忽略”的局部规则。
- Trigger 是否为正式玩家受击体统一交给 `DamageTargetFilter` 的显式标记判断。
- Default 掩体、世界激活状态、伤害快照和对象池回收语义保持不变。

## Unity 场景变更

### MainLevel

- 文件：`Assets/Scenes/MainLevel.unity`
- Player GameObject 的 `m_Component` 新增 `PlayerHurtbox` MonoBehaviour 引用。
- 标记与 Player 的正式 CapsuleCollider2D 位于同一 GameObject；该 Collider 保持非 Trigger。
- `MagnetRadius` 的 CircleCollider2D 保持 Trigger，未挂载 `PlayerHurtbox`。
- 未新增 Layer、Tag 或 Physics2D 碰撞矩阵变化，也未修改玩家生命、磁吸数值或场景层级结构。

## 测试变更

### DamageTargetFilterTests

- 文件：`Assets/Tests/Editor/DamageTargetFilterTests.cs`
- 覆盖 Player Layer + 显式标记的正式 Collider 成功解析。
- 覆盖无标记的 Player 非 Trigger Collider 与辅助 Trigger 均被拒绝。
- 覆盖标记子级 Trigger 通过 `attachedRigidbody` 解析根节点 `IDamageable`。

### CombatPhysicsPlayModeTests

- 文件：`Assets/Tests/PlayMode/CombatPhysicsPlayModeTests.cs`
- 玩家物理夹具增加正式 `PlayerHurtbox`，保留真实 EnemyBase 接触伤害回归。

### RangedEnemyPlayModeTests

- 文件：`Assets/Tests/PlayMode/RangedEnemyPlayModeTests.cs`
- 玩家正式身体夹具增加 `PlayerHurtbox`。
- 新增弹体进入未标记 MagnetRadius 后不提前伤害、继续命中正式身体的行为测试。
- 新增标记子级 Trigger 沿刚体解析根节点 `PlayerHealth` 并造成伤害的行为测试。
- 既有首发、冷却、最大射程、Defang、池复用和跨世界覆盖保持不变。

### SceneReloadPlayModeTests

- 文件：`Assets/Tests/PlayMode/SceneReloadPlayModeTests.cs`
- 加载真实 MainLevel 后断言 Player 正式 Collider 同对象存在 `PlayerHurtbox`。
- 断言 MagnetRadius 不存在 `PlayerHurtbox`，保护场景序列化边界。

## 工作流与项目入口

### AGENTS.md

- 新增 Codex 项目规则入口，内容与现有项目约束一致；保留 `CLAUDE.md` 供其他 Agent 工具使用。

### Sol/Luna QA 工作流

- 文件：`DevLog/plans/sol-luna-worktree-session-workflow.md`
- 记录 Session 16 的显式 Hurtbox 契约与长期 QA 实践。
- 路线 B/C 新增 Execute 状态接回规则：Plan 保存任务 ID、持续等待完成/需关注状态，完成后自动读取交付并进入 Review。

## 等价手动搭建

1. 在 `Assets/Scripts/Player/` 创建无状态 `PlayerHurtbox : MonoBehaviour`，添加 `DisallowMultipleComponent` 和 `RequireComponent(Collider2D)`。
2. 在 MainLevel 的 Player 根对象上，把 `PlayerHurtbox` 与正式 CapsuleCollider2D 挂在同一 GameObject。
3. 保持 Player 根对象为 Player Layer，正式 CapsuleCollider2D 为非 Trigger。
4. 保持 `Player/MagnetRadius` 的 CircleCollider2D 为 Trigger，并确认该对象没有 `PlayerHurtbox`。
5. 不修改 TagManager、Physics2DSettings、PlayerHealth、MagnetRadius 数值、对象池、武器、账号或波次。

## 验证结果

- Plan Review 结论：`APPROVED`，无 P0/P1/P2/P3 Finding。
- 老大人工 Unity 验收：通过。
- main 集成后命令：`Tools/Run-ProjectChecks.ps1 -TestPlatform All -NoGraphics`。
- 报告：`Logs/Automation/20260830-230133/summary.json`。
- EditMode：68/68 Passed。
- PlayMode：27/27 Passed。
- failed/errors/inconclusive/skipped：0。
- `compileErrorDetected=false`，`passedQualityGate=true`。
- `git diff --check` 与提交前 staged/unstaged 检查在归档提交前执行。
