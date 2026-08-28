# Session 12 进度归档：角色选择、统一属性系统与初始化竞态修复

- 日期：2026-08-27 至 2026-08-28
- 状态：角色选择、属性框架、局内消费链路与自动化回归已完成；Windows 64 位独立构建成功。角色初始武器、固有能力和部分属性的实际规则仍待后续实现。
- 归档范围：角色数据资产、主菜单角色选择、跨场景选择会话、21 项玩家属性、武器/生命/磁吸消费、属性面板、调试工具、自动化测试，以及蓝衣战士属性缓存修复。

## 已完成

| 模块 | 当前结果 |
| --- | --- |
| 角色数据 | 新增 CharacterDataSO 与 CharacterBaseStats，以稳定 characterID、显示信息、选择页 Sprite 和 21 项基础属性描述角色。 |
| 可选角色 | 新增默认角色和蓝衣战士资产；蓝衣战士配置为 140 最大生命、0.25/秒恢复、2 护甲、2.6 移速和 1.25 力量。 |
| 角色选择页 | 主菜单开始按钮先打开 4×3、共 12 槽的角色选择页；当前有 2 个可用角色，支持悬停/点击、确认、返回、左右立绘滑入渐显和预览帧循环。 |
| 跨场景选择 | CharacterSelectionSession 保存本次确认的 CharacterDataSO，MainLevel 的 PlayerStats 在首次属性初始化时消费。 |
| 统一属性模型 | 新增 21 项 PlayerStatType；PlayerStats 使用数组缓存最终值，支持 Flat、AdditivePercent、Multiplicative 三类修改方式和稳定 sourceId 替换。 |
| 玩法接入 | 最大生命、恢复、护甲、移动速度、力量、范围、投射物速度、持续时间、数量、冷却、成长和磁吸已经接入对应运行时系统。 |
| 武器快照 | 五类武器通过 WeaponBase 统一读取玩家战斗属性；投射物与环绕物保存本次生成使用的范围/持续时间快照，池化回收时恢复初始尺寸。 |
| 属性 UI | 暂停菜单右侧新增 21 行最终属性看板；F9 属性调试面板通过独立调试来源设置和清除修改器。 |
| 初始化竞态修复 | PlayerStats 在任何组件首次读取最终属性前解析一次角色选择，避免 PlayerHealth 或子物体先触发惰性缓存后仍保留默认角色数值。 |
| 自动化与构建 | 最新质量门禁 EditMode 40/40、PlayMode 16/16 通过；Windows 64 位构建成功，并完成 8 秒无窗口启动冒烟测试。 |

## 当前运行链路

1. MainMenuController 打开 CharacterSelectionUI。
2. 玩家选中角色并确认后，MainMenuController 调用 CharacterSelectionSession.Select。
3. MainLevel 加载期间，PlayerHealth、PlayerMagnet、武器或其他消费者可能先于 PlayerStats.Awake 请求属性。
4. PlayerStats.EnsureStatsInitialized 首先调用一次性角色解析，再基于角色基础值与全部修改器生成最终属性快照。
5. PlayerHealth 使用最终 MaxHealth、Recovery、Armor；PlayerController 使用 FinalMoveSpeed；PlayerMagnet 使用 Magnet。
6. WeaponBase 及派生武器使用 Might、Area、ProjectileSpeed、Duration、Amount、Cooldown。
7. 经验吸收使用 Growth；暂停属性看板和 F9 调试面板读取同一最终快照。

## 角色配置

### 默认角色

- 最大生命：100
- 恢复：0/秒
- 护甲：0
- 移动速度：3
- 力量：100%

### 蓝衣战士

- 最大生命：140
- 恢复：0.25/秒
- 护甲：2
- 移动速度：2.6
- 力量：125%

其余当前已配置属性沿用系统中性值。

## 蓝衣战士属性问题与修复

### 现象

角色选择页和 MainLevel 中的 CharacterData 引用均显示为蓝衣战士，但暂停属性值、生命和实际战斗仍表现为默认角色。

### 根因

PlayerStats 原实现只在 Awake 中直接替换 characterData。Unity 不保证不同物体及组件间的 Awake 顺序；PlayerHealth 或 PlayerMagnet 可能先读取最终属性，使 PlayerStats 使用场景默认角色建立缓存并设置 _statsInitialized。PlayerStats.Awake 随后虽然替换了角色引用，却没有让旧缓存失效。

### 修复

- EnsureStatsInitialized 在构建缓存前调用 ResolveSessionCharacterOnce。
- 成功消费菜单角色后标记 _sessionCharacterResolved，避免同一局重复读取静态会话。
- 角色引用变化时使旧快照失效，再使用正确角色重算。
- SetCharacterData 标记为显式角色来源，保证测试、重开或未来角色切换不会被残留菜单会话反向覆盖。

## 验证状态

### 已人工确认

- 老大已在 Unity 中确认主菜单打开角色选择、选中角色并进入 MainLevel 的交互流程正常。
- 老大已确认角色选择页、经验条、击杀图标、属性面板等界面主体表现。

### 已静态检查

- git diff --check 通过；仅有仓库既有 LF/CRLF 提示，没有空白错误。
- 角色选择、角色资产、MainLevel 默认角色和 Build Settings 引用完整。
- 回归测试明确覆盖“生命组件先读取属性，PlayerStats.Awake 后执行”的原始缺陷顺序。

### 已编译与运行验证

- 入口：Tools/Run-ProjectChecks.ps1 -NoGraphics
- Unity：2022.3.62f3c1
- EditMode：40/40 通过，0 失败，0 错误。
- PlayMode：16/16 通过，0 失败，0 错误。
- compileErrorDetected：false。
- passedQualityGate：true。
- 报告：Logs/Automation/20260828-095917/summary.json。
- 真实 MainMenu → 蓝衣战士 → MainLevel 用例验证：
  - CharacterData 为 character_blue_warrior；
  - 最大生命 140、恢复 0.25、护甲 2、力量 1.25、移速 2.6；
  - PlayerHealth 以 140/140 开局。

### 独立构建

- 平台：Windows 64 位。
- 输出：Build/RainsenVamp.exe。
- 构建日志：Logs/Build/Windows64-20260828-100035.log。
- Unity 结果：Build Finished, Result: Success。
- Assembly-CSharp.dll、MainMenu 和 MainLevel 场景数据均更新于 2026-08-28。
- 无窗口启动 8 秒保持运行且未崩溃；测试结束后主动关闭进程。

### 未验证与已知限制

- 修复后的蓝衣战士仍需老大再进行一次带画面的实际游玩确认，重点观察满血值、暂停属性面板、移动速度和武器伤害手感。
- 无图形冒烟测试使用 Null GPU，URP/TMP Shader 不支持日志属于该模式限制，不能替代真实 GPU 画面验收。
- msyh SDF 缺少 U+00B7“·”，MainMenu 的 InputHint/Tagline 中该字符会被替换为空格。
- 选择角色目前改变角色属性与选择页表现，不会替换 MainLevel 中玩家的战斗 Sprite、Animator、碰撞体或角色 Prefab。
- 角色初始武器和固有能力仍显示“待配置”。

## 下一步 Todo

### P0 回归

- 老大在 Unity 或最新独立构建中重新选择蓝衣战士，确认 140/140 生命、2 护甲、0.25/秒恢复、125% 力量和 2.6 移速。
- 使用正常图形模式检查最新独立构建的主菜单、角色选择页、HUD 与不同分辨率表现。
- 为 msyh SDF 补充“·”字形，或把对应文案改为字体已覆盖的分隔符。

### P1 功能补全

- 在 CharacterDataSO 接入初始武器配置，并在开局时授予。
- 设计角色固有能力数据与运行时来源，接入 PlayerStats 修改器，且不占普通能力栏。
- 实现 AbilityDataSO、能力授予/升级与装备栏第二行真实绑定。
- 将 Luck、Greed、Curse、Revival、Reroll、Skip、Banish、Charm、Defang 接入实际规则。
- 如需角色外观差异，建立正式角色 Prefab/皮肤、Animator、碰撞体与 HealthBarAnchor 工作流。

### P1 经济与状态

- 实现金币掉落、对象池、磁吸拾取和 AddGold 调用。
- 将击杀数和金币数迁移到独立 RunState/RunStats 服务，由 UI 订阅状态变化。

### 长期

- 设置、存档/继续、本地化、音效、加载过渡与局内结算。
- 在目标敌人数和多武器组合下建立 CPU、Physics2D、GC Alloc 与构建性能基线。
