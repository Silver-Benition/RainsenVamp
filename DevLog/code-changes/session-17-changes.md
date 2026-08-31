# Session 17 代码变更：正式能力数据、运行时与机制能力

- 日期：2026-08-31
- 对应进度：`DevLog/progress/session-17.md`
- Session 起始 main：`996b06bcefa9f872524b2f5768d74c68f6c44be9`
- 最终自动化报告：`Logs/Automation/20260831-090854/summary.json`

## 新增运行时代码

### AbilityDataSO 与 AbilityLevelData

- 文件：`Assets/Scripts/Data/AbilityDataSO.cs`
- `AbilityLevelData` 保存每一级的累计 `StatModifierDefinition` 快照和显示描述。
- `AbilityDataSO` 保存稳定 ID、本地化/显示字段、图标、HUD 变换、等级配置及可选 `AbilityMechanicSO`。
- 数据资产只描述内容，不保存玩家当前等级或机制激活状态。

### AbilityRuntimeContracts

- 文件：`Assets/Scripts/Ability/AbilityRuntimeContracts.cs`
- `AbilityRuntimeContext` 汇总玩家对象、`PlayerStats`、`PlayerHealth`、能力数据与稳定来源。
- `IAbilityMechanicRuntime` 定义机制等级更新和释放契约。
- `OwnedAbilityState` 保存单局能力数据、当前等级和对应机制运行时。

### AbilityMechanicSO

- 文件：`Assets/Scripts/Ability/AbilityMechanicSO.cs`
- 定义机制能力工厂接口，由共享 SO 为每个玩家、每局创建独立运行时实例。
- 避免事件订阅、冷却和激活标记写入共享资源而污染重开或多玩家状态。

### AbilityManager

- 文件：`Assets/Scripts/Ability/AbilityManager.cs`
- `GrantOrUpgrade` 负责首次获得、逐级升级、累计修正来源替换和机制运行时创建/更新。
- `CanAcquireAbility` 同时处理 6 槽容量、已有能力继续升级和最大等级过滤。
- `GetOwnedAbility`、`DebugEnsureAbilityLevel` 提供 UI、测试和开发调试入口。
- `OwnedAbilitiesChanged` 驱动 HUD 更新；销毁时释放机制运行时并清除能力来源。

### LowHealthBuffMechanicSO

- 文件：`Assets/Scripts/Ability/LowHealthBuffMechanicSO.cs`
- 订阅 `PlayerHealth` 生命变化事件，在生命比例不高于 40% 时写入动态 Might 与 MoveSpeed 修正。
- 升级期间若已处于低血量会立即替换为新等级快照；生命恢复后立即移除机制来源。
- 不使用每帧 `Update` 轮询。

### RetaliationPulseMechanicSO

- 文件：`Assets/Scripts/Ability/RetaliationPulseMechanicSO.cs`
- 仅在实际非致命受伤事件触发，使用缩放时间执行等级冷却。
- 使用预分配 `Collider2D[128]`、`ContactFilter2D` 和 `Physics2D.OverlapCircle` 扫描 Enemy Layer。
- 使用可复用 `HashSet<IDamageable>` 合并同一敌人的多个 Collider，再按当前 Might 结算一次伤害。
- 从 `ObjectPoolManager` 请求脉冲表现，不在伤害热路径直接实例化特效。

### AbilityPulseVfx

- 文件：`Assets/Scripts/VFX/AbilityPulseVfx.cs`
- 实现 `IPoolable`，只负责缩放和渐隐表现，不参与伤害计算。
- 播放结束后主动回到对象池，保持逻辑层与表现层分离。

### AbilityDebugPanel

- 文件：`Assets/Scripts/UI/AbilityDebugPanel.cs`
- 仅在 Editor 或 Development Build 中启用，F7 打开正式能力调试面板。
- 调试按钮始终调用 `AbilityManager.GrantOrUpgrade`，不绕过正式运行时流程。

## 修改运行时代码

### UpgradeDataSO

- 文件：`Assets/Scripts/Data/UpgradeDataSO.cs`
- 新增 `abilityToGrant`。
- 新增 `HasExactlyOneReward`，要求一条升级奖励只能配置武器或能力之一，不能同时为空或同时存在。

### LevelUpManager

- 文件：`Assets/Scripts/LevelUpManager.cs`
- 解析玩家 `AbilityManager` 并将能力奖励交给正式管理器。
- 候选过滤增加奖励 XOR 合法性、能力容量和最大等级规则。
- 新增 `GetOwnedAbility` 查询；宝箱升级路径继续只处理武器。

### UpgradeUIItem

- 文件：`Assets/Scripts/UpgradeUIItem.cs`
- 根据升级奖励类型读取武器或能力的权威名称、图标、描述和下一等级信息。

### PlayerLoadoutDisplayUI

- 文件：`Assets/Scripts/UI/PlayerLoadoutDisplayUI.cs`
- 新增第二排 6 个能力槽位并订阅 `OwnedAbilitiesChanged`。
- 使用能力数据中的 HUD 缩放/位移，暂停时显示当前等级点。

### CollectionUI

- 文件：`Assets/Scripts/UI/CollectionUI.cs`
- 内容目录中的能力升级改为从 `AbilityDataSO` 获取名称、图标和描述。

## 数据、Prefab 与场景

- 新增 `Assets/Data/Abilities/`，包含 6 个 `AbilityDataSO`、2 个机制 SO 和 6 个 `UpgradeDataSO` 包装资产。
- 新增 `Assets/Prefab/VFX/AbilityPulseVfx.prefab`，由 `SpriteRenderer` 与 `AbilityPulseVfx` 组成并进入对象池。
- `Assets/Data/GameContentCatalog.asset` 注册 6 个能力升级内容。
- `Assets/Scenes/MainLevel.unity` 在 Player 上挂载 `AbilityManager`，并向 `LevelUpManager` 候选池加入 5 个武器与 6 个能力升级。
- 未新增 Layer、Tag、输入绑定或 Physics2D 碰撞矩阵修改。

## 测试变更

### EditMode

- 新增 `Assets/Tests/Editor/AbilitySystemTests.cs`：覆盖累计来源替换、低血量机制切换/升级，以及 6 个正式能力资产与 Prefab 引用契约。
- 修改 `Assets/Tests/Editor/LevelUpManagerLoadoutTests.cs`：覆盖非法空奖励/双奖励过滤和能力 6 槽上限下已有能力继续升级。
- 修改 `Assets/Tests/Editor/PhaseThreeAccountProgressTests.cs`：使 Seal/Banish 夹具使用合法能力奖励，并继续保护账号域与局内域边界。

### PlayMode

- 新增 `Assets/Tests/PlayMode/AbilitySystemPlayModeTests.cs`：覆盖反击脉冲非致命触发、冷却、多 Collider 去重和致命伤跳过。
- 修改 `Assets/Tests/PlayMode/PlayerLoadoutDisplayPlayModeTests.cs`：覆盖能力槽、图标变换和暂停等级点。
- 修改 `Assets/Tests/PlayMode/SceneReloadPlayModeTests.cs`：覆盖 MainLevel 的 `AbilityManager`、6 个候选绑定，以及获得能力后真实重开清零。

## 等价手动搭建

1. 在 MainLevel 的 Player 根对象挂载 `AbilityManager`，确保同对象可解析 `PlayerStats` 与 `PlayerHealth`。
2. 在 `Assets/Data/Abilities/` 创建能力数据，设置唯一稳定 ID、显示/本地化字段、累计等级快照和可选机制资产。
3. 为每个能力创建一个 `UpgradeDataSO` 包装资产，仅填写 `abilityToGrant`，保持 `weaponToGrant` 为空。
4. 将 6 个能力升级加入 MainLevel 的 `LevelUpManager.allAvailableUpgrades`，并注册到 `GameContentCatalog`。
5. 为逆境本能配置 `LowHealthBuffMechanicSO`；为反击脉冲配置 `RetaliationPulseMechanicSO` 及池化 VFX Prefab。
6. 保证 VFX Prefab 根对象包含 `SpriteRenderer` 与 `AbilityPulseVfx`，由对象池负责生成和回收。
7. 不需要新增 Layer、Tag、按键或碰撞矩阵；反击扫描沿用 Enemy Layer。
8. Editor/Development Build 可按 F7 调试能力；原有 F8 武器和 F9 属性调试入口保持不变。

## 验证结果

- 静态检查：通过；资源 GUID 唯一，场景/Prefab 脚本绑定无缺失，`git diff --check` 通过。
- 编译验证：通过；最终报告 `compileErrorDetected=false`。
- 运行验证：通过；EditMode 73/73、PlayMode 28/28，failed/errors/inconclusive/skipped 均为 0。
- 质量门禁：`passedQualityGate=true`。
- 老大人工 Unity 验收：通过，目前未发现明显问题。
- 未验证为最终交付的部分：专属能力图标、专属脉冲 VFX、最终音效/手感，以及极端同屏敌人专项性能 Profiling。

## 素材待办

当前能力图标与反击脉冲 VFX 复用了项目已有素材，只用于验证系统闭环。后续必须替换为专属正式像素素材，并在替换后重新检查 HUD 可读性、缩放位置、动画节奏与对象池回收表现。
