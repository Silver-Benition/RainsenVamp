# Session 12 代码与资源变更记录

- 日期：2026-08-27 至 2026-08-28
- 主题：角色数据与选择、统一玩家属性、属性消费与 UI、测试扩展、角色属性初始化竞态修复、字体补全与独立构建验收。

## 数据与属性基础

### PlayerStatType

文件：Assets/Scripts/Player/PlayerStatType.cs

- 定义 21 项局内玩家属性：MaxHealth、Recovery、Armor、MoveSpeed、Might、Area、ProjectileSpeed、Duration、Amount、Cooldown、Luck、Growth、Greed、Curse、Magnet、Revival、Reroll、Skip、Banish、Charm、Defang。
- Seal 保持为局外候选池规则，不进入运行时属性枚举。

### PlayerStatModifier

文件：Assets/Scripts/Player/PlayerStatModifier.cs

- 新增 PlayerStatModifierMode：Flat、AdditivePercent、Multiplicative。
- PlayerStatModifier 保存属性类型、计算方式和值。
- PlayerStats 通过稳定 sourceId 管理来源，使同一能力升级可以整体替换旧等级，而不是重复叠加。

### CharacterDataSO 与 CharacterBaseStats

文件：Assets/Scripts/Data/CharacterDataSO.cs

- CharacterBaseStats 保存 21 项角色基础属性，并通过 GetValue(PlayerStatType) 提供无字典读取。
- CharacterDataSO 保存稳定 characterID、本地化键、直接显示名、槽位头像、左右立绘和预览帧。
- GetBaseValue、GetDisplayName、GetSelectionIcon、GetPortraitSprite、GetPreviewFrame 为缺失配置提供安全回退。
- 角色资产只保存跨局静态数据，运行时不修改 ScriptableObject。

## 角色资产

### DefaultCharacter.asset

路径：Assets/Data/Characters/DefaultCharacter.asset

- 默认生命 100、移动速度 3，其余倍率类属性使用中性值 1。
- 选择页头像、立绘和预览帧复用现有默认玩家 Sprite。

### BlueWarrior.asset

路径：Assets/Data/Characters/BlueWarrior.asset

- characterID：character_blue_warrior。
- 最大生命 140、恢复 0.25、护甲 2、移动速度 2.6、力量 1.25。
- 使用新增 blue_warrior.png 作为选择页头像、立绘和预览帧。

### blue_warrior.png

路径：Assets/Art/Sprites/Player/blue_warrior.png

- 新增蓝衣战士选择页美术与对应 Unity .meta。

## PlayerStats

文件：Assets/Scripts/Player/PlayerStats.cs

### 统一属性快照

- 新增角色配置引用与 21 项最终值缓存。
- 新增 GetFinalStat(PlayerStatType) 及 MaxHealth、Recovery、Armor、FinalMoveSpeed、Might、Area、ProjectileSpeed、Duration、Amount、Cooldown、Growth、Magnet 快捷属性。
- SetModifiers 按 sourceId 添加或替换修改器集合。
- RemoveModifiers 完整移除一个来源。
- RecalculateFinalStats 按以下固定顺序计算：

  基础值与 Flat 相加 → 乘以全部 AdditivePercent 合计 → 乘以 Multiplicative 连乘。

- NormalizeFinalValue 为生命、冷却、范围、持续时间和概率类属性提供安全边界。

### 角色初始化竞态修复

- 新增 _sessionCharacterResolved，记录是否已消费或显式确定本局角色来源。
- Awake 不再只直接覆盖 characterData，而是统一调用 EnsureStatsInitialized。
- EnsureStatsInitialized 在任何缓存命中判断之前先调用 ResolveSessionCharacterOnce。
- ResolveSessionCharacterOnce 在首次属性读取时消费 CharacterSelectionSession；角色变化时使旧快照失效。
- SetCharacterData 标记显式角色来源，防止仍存活的静态菜单选择覆盖测试或未来明确切换。
- 修复 PlayerHealth、PlayerMagnet 或其他组件先读取属性时，CharacterData 已变更但最终数组仍保持默认值的问题。

## 角色选择

### CharacterSelectionSession

文件：Assets/Scripts/Player/CharacterSelectionSession.cs

- Select(CharacterDataSO) 保存非空角色引用。
- SelectedCharacter 提供只读访问。
- Clear() 在进入一轮新菜单流程或测试清理时移除静态状态。

### CharacterSelectionUI

文件：Assets/Scripts/UI/CharacterSelectionUI.cs

- 运行时创建 4×3、容量 12 的角色选择界面。
- 当前从 availableCharacters 绑定默认角色和蓝衣战士，其余槽位保持不可用。
- 支持悬停选择、点击选择、再次点击/确认提交、返回主菜单。
- 左右立绘使用非缩放时间执行滑入渐显，暂停时间缩放不会冻结选择页动画。
- 底部预览按 CharacterDataSO.previewFrames 循环。
- 角色摘要显示生命、力量和移动速度。
- 初始武器与角色能力区域保留“待配置”说明，未伪装为已实现功能。

### CharacterSelectionSlotUI

文件：Assets/Scripts/UI/CharacterSelectionSlotUI.cs

- 封装单个角色槽位的角色引用、选择状态、悬停和点击转发。
- 空槽保持不可交互；已选择槽使用 Outline 与颜色表现。

### MainMenuController

文件：Assets/Scripts/UI/MainMenuController.cs

- 开始按钮不再直接加载 MainLevel，而是锁定主菜单并打开角色选择页。
- 订阅 CharacterConfirmed 和 Closed。
- 确认后保存角色、锁定选择控件并异步加载 MainLevel。
- 返回后恢复主菜单按钮和默认焦点。
- Awake 清除上一轮菜单残留选择。

## 属性消费

### PlayerHealth

文件：Assets/Scripts/Player/PlayerHealth.cs

- 最大生命由 PlayerStats.MaxHealth 初始化并随 StatsChanged 同步。
- Update 按 Recovery 每秒恢复，暂停时随 Time.deltaTime 停止。
- TakeDamage 使用 Armor 固定平减，同时保证有效攻击至少造成 1 点伤害。
- 最大生命降低时钳制当前生命；普通运行时提高上限不额外治疗。

### PlayerMagnet

文件：Assets/Scripts/Player/PlayerMagnet.cs

- 从父级缓存 PlayerStats。
- 初始与 StatsChanged 时把 CircleCollider2D.radius 同步为最终 Magnet。

### PlayerController

- 移动速度统一读取 PlayerStats.FinalMoveSpeed；找不到属性组件时才使用后备值。

### WeaponDataSO 与 WeaponBase

文件：Assets/Scripts/Data/WeaponDataSO.cs、Assets/Scripts/Weapon/WeaponBase.cs

- WeaponDataSO 增加 IgnoredPlayerWeaponStats 位标志，允许个别武器显式忽略不适用的玩家属性。
- WeaponBase 缓存父级 PlayerStats。
- 统一计算伤害、冷却、弹速、数量、持续时间和范围。
- 热路径只读取已缓存最终值，不为每次攻击重新聚合修改器。

### 五类武器与池化实体

涉及：

- AuraWeapon、MeleeWeapon、LobbedWeapon、OrbitWeapon。
- ProjectileBase、LobbedProjectile、MeleeSwingHitbox、OrbitingProjectile。

变更：

- 攻击时传入处理后的伤害、范围、弹速、持续时间、数量和冷却。
- 池化攻击实体保存本次 Area 快照。
- OnDisable 或重新初始化时恢复 Prefab 初始尺寸，防止对象池复用后倍率重复累积。
- Aura、Knife、Umbrella 数据资产同步补充属性忽略配置。

## 属性 UI 与调试

### PlayerStatPresentation

文件：Assets/Scripts/UI/PlayerStatPresentation.cs

- 统一 21 项属性的中文名称、展示顺序和格式。
- 倍率类显示相对中性值 1 的百分比，计数类取整，Recovery 显示“/秒”。

### PlayerStatBoardUI

文件：Assets/Scripts/UI/PlayerStatBoardUI.cs

- 在手动暂停面板右侧创建 21 行属性看板。
- 订阅 PlayerStats.StatsChanged，暂停期间修改属性也会即时刷新。
- UI 只读取最终值，不参与属性计算。

### PlayerAttributeDebugPanel

文件：Assets/Scripts/UI/PlayerAttributeDebugPanel.cs

- F9 打开或关闭属性调试面板。
- 使用独立 DebugSourceId 写入修改器。
- 设置、更新和清除调试数值不会污染其他能力或装备来源。

## 场景变更

### MainMenu.unity

- 挂载 CharacterSelectionUI。
- availableCharacters 绑定 DefaultCharacter 与 BlueWarrior。
- MainMenuController 绑定角色选择组件。

### MainLevel.unity

- PlayerStats 绑定 DefaultCharacter 作为直接进入场景时的安全默认角色。
- 暂停菜单接入 PlayerStatBoardUI。
- 场景接入 PlayerAttributeDebugPanel。

## 测试变更

### CharacterSelectionTests

文件：Assets/Tests/Editor/CharacterSelectionTests.cs

- 验证单角色配置生成 12 槽并显示角色信息。
- 验证确认角色后 PlayerStats 使用同一角色。
- 回归增强：玩家对象保持未激活，先手动执行 PlayerHealth.Awake 触发属性读取，再执行 PlayerStats.Awake；断言角色最终属性和生命均来自所选角色。

### PlayerAttributeSystemTests

文件：Assets/Tests/Editor/PlayerAttributeSystemTests.cs

- 验证三类修改器计算顺序。
- 验证同一来源升级替换与完整移除。
- 验证 Growth 只乘算一次。
- 验证最大生命、护甲最低伤害、磁吸范围和武器属性快照。

### PlayerAttributeUiTests

文件：Assets/Tests/Editor/PlayerAttributeUiTests.cs

- 验证属性看板刷新 21 项最终值。
- 验证调试面板只修改和清除调试来源。

### PlayerAttributePlayModeTests

文件：Assets/Tests/PlayMode/PlayerAttributePlayModeTests.cs

- 验证护甲减伤与持续恢复。
- 验证池化投射物 Area 快照和禁用复位。
- 验证暂停期间属性变化即时刷新看板。

### MainMenuPlayModeTests

文件：Assets/Tests/PlayMode/MainMenuPlayModeTests.cs

- 验证开始按钮先打开角色选择而非立即加载。
- 验证 12 槽、2 个角色、蓝衣战士展示数据、立绘动画和加载锁。
- 进入 MainLevel 后不仅检查 characterID，还检查最大生命、恢复、护甲、力量、移速最终值和 140/140 满血状态。

## 字体资源闭环

### msyh SDF

文件：Assets/Fonts/msyh SDF.asset

- 保持 Static Atlas，不改变源字体 GUID、Atlas Texture 引用或现有材质关系。
- 字符序列扩展为 0020-007E、00B7、2000-206F、3000-303F、4E00-9FFF、FF00-FFEF。
- 唯一字符由 21087 增至 21406，新增 319 个字符，原有字符 0 丢失。
- U+00B7“·”、中文句读与常用全角标点均已进入字符表。

## 最终验证

- git diff --check：通过。
- EditMode：40/40 通过。
- PlayMode：16/16 通过。
- 编译错误：0。
- 质量门禁：通过。
- 报告：Logs/Automation/20260828-103057-standalone-snapshot/summary.json。
- Windows 64 位构建：成功。
- 构建输出：Build/Windows64-P0-20260828-1032/RainsenVamp.exe。
- 构建日志：Logs/Build/Windows64-P0-20260828-1032.log。
- 正常图形后端：NVIDIA GeForce RTX 4070 SUPER，Direct3D 11.0，1280×720 与 1024×768 均启动成功。
- 视觉检查：MainMenu、默认角色页、蓝衣战士页、MainLevel HUD 与暂停属性板均无裁切、重叠、黑屏或缺字。
- 独立构建属性检查：蓝衣战士最大生命 140、恢复 0.25/秒、护甲 2、移速 2.6、力量 +25%。
- 退出按钮：键盘选中“退出游戏”后进程自行以退出码 0 结束。
- Player 日志：未发现 U+00B7 缺字、NullReferenceException、运行时错误或图形初始化失败。
- 人工复测：老大已在 Unity 中完成蓝衣战士复测，结果正常。
