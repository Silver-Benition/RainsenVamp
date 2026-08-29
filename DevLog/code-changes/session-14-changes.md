# Session 14 代码变更：角色属性系统阶段三

- 日期：2026-08-29
- 对应进度：`DevLog/progress/session-14.md`
- 基线提交：`8fd979b7971d782a8a7d673868e9739a0b7a13c4`

## 新增运行时代码

### Data

- `AccountProgressData.cs`
  - 定义版本 1 的账号 JSON 数据：账号金币、历史累计金币、累计击杀、最近角色、Seal 容量、角色解锁、收藏发现和已 Seal 升级稳定 ID。
  - `CreateDefault` 创建默认角色已解锁、已发现的新账号。
  - `AccountProgressRules` 集中声明首版 Seal 初始容量与最大容量均为 1。
  - `AccountProgressMigrator.MigrateToCurrent` 迁移无显式版本的兼容 JSON。
  - `Normalize` 钳制负数、去除空白和重复 ID、恢复默认角色，并按容量裁剪 Seal。
- `GameContentCatalogSO.cs`
  - 使用 ScriptableObject 集中保存收藏首版的角色、武器和升级项目列表。
  - 对外只暴露只读列表，运行时不修改共享内容目录。

### Core

- `AccountProgressStorage.cs`
  - `IAccountProgressStorage` 抽象账号读取与保存，隔离正式文件系统和测试内存后端。
  - `AccountProgressLoadResult` 返回数据、是否需要回写、是否只读和诊断消息。
  - `JsonAccountProgressStorage.Load` 按主档、备份、安全新档顺序读取；高版本档进入只读保护。
  - `JsonAccountProgressStorage.Save` 先写临时 JSON 并重新解析验证，再更新备份与主档。
  - 主档损坏时保留 `account-progress.corrupt-时间戳.json`，不让损坏内容覆盖有效备份。
  - `InMemoryAccountProgressStorage` 供批处理和自动化测试使用，避免污染 `persistentDataPath`。
- `AccountProgressService.cs`
  - 作为账号跨局进度唯一运行时权威来源，使用 HashSet 加速稳定 ID 查询，同时维护可序列化列表。
  - `RecordRunResults` 结算账号金币、历史金币和累计击杀。
  - `EvaluateAutomaticUnlocks` 评估累计击杀等无需购买的解锁条件。
  - `TryPurchaseCharacter` 校验金币型角色、扣款并永久解锁。
  - `SetLastSelectedCharacter` 保存最近确认角色。
  - `DiscoverWeapon`、`DiscoverUpgrade` 保存收藏发现。
  - `TrySetUpgradeSealed` 校验发现状态、稳定 ID 和首版容量后保存跨局 Seal。
  - `ResetToDefaults` 为未来设置入口提供账号重置能力。
  - 批处理模式自动使用内存后端；正式运行使用 `Application.persistentDataPath`。

### UI

- `CollectionUI.cs`
  - 管理 MainMenu 场景中已经序列化的收藏固定骨架。
  - 提供角色、武器和升级项目三个分类，并展示账号金币与 Seal 使用量。
  - 按账号发现状态建立运行时数据条目；已发现升级可启用或解除 Seal。
  - 武器型升级通过 `GetUpgradeCollectionName` 和 `GetUpgradeCollectionDescription` 复用 WeaponDataSO 权威词条。
  - `AuthorSceneInterface` 仅在 Unity Editor 编译条件下提供固定骨架作者入口；正式运行不会创建固定收藏面板。

## 修改运行时代码

### 角色数据与属性

- `CharacterDataSO.cs`
  - 新增 CharacterUnlockConditionType、CharacterUnlockDefinition 和 CharacterPassiveDefinition。
  - 新增稳定 `characterID`、`startingWeapon`、`passive` 和 `unlock`。
  - 提供解锁说明、被动名称/描述和稳定被动来源 ID。
- `PlayerStats.cs`
  - `SetCharacterData` 切换角色时同步基础属性和固有被动。
  - `SynchronizeCharacterPassiveSource` 先移除上一角色稳定来源，再应用当前角色被动。
  - 普通属性重算不会重复叠加角色被动，角色切换后旧被动不会残留。

### 武器、升级与候选

- `WeaponDataSO.cs`
  - 增加收藏用稳定 weaponID、显示名称和显示描述入口。
- `UpgradeDataSO.cs`
  - 增加收藏显示描述入口；稳定 upgradeID 继续作为 Banish、Seal 和存档键。
- `LevelUpManager.cs`
  - 启动时通过 `EnsureCharacterStartingWeapon` 读取本局角色配置，不再假设场景固定火球。
  - 获得武器时把 weaponID 写入账号收藏发现。
  - 候选生成时记录出现过的 upgradeID。
  - `BuildSelectableUpgradePool` 在原有本局 Banish 之外，独立过滤账号跨局 Seal。
  - 普通升级和宝箱奖励继续使用同一合法候选入口。

### 局流程

- `GameFlowManager.cs`
  - 新增 `CommitRunProgressIfNeeded`，把 RunState 本局金币和击杀结算到账号服务。
  - Restart、ReturnToMainMenu 和 OnApplicationQuit 共用同一提交入口。
  - 使用局内布尔状态保证同一局多次触发退出路径时只结算一次。

### 角色选择 UI

- `CharacterSelectionSlotUI.cs`
  - 同时支持鼠标进入和 EventSystem 选中回调。
  - 缓存角色、槽位编号和解锁状态。
  - 锁定槽位使用黑色头像剪影与锁定框体颜色。
- `CharacterSelectionUI.cs`
  - 订阅账号进度变化并刷新角色解锁状态。
  - 优先选择最近使用且已解锁的角色；否则回退到首个已解锁角色。
  - 已解锁角色展示属性、起始武器和固有被动。
  - 锁定角色隐藏姓名、属性、立绘、预览、起始武器和被动，只显示解锁条件浮层。
  - 累计击杀型浮层显示当前进度；GoldPurchase 型浮层显示当前金币和购买按钮。
  - 购买成功后立即刷新当前槽位和角色详情。
  - 禁止确认锁定角色。
- `MainMenuController.cs`
  - 显式序列化 CollectionButton 和 CollectionUI。
  - 打开收藏时锁定主菜单按钮，关闭后恢复焦点。
  - 确认角色时保存最近选择。
  - 删除运行时克隆收藏按钮和动态挂载 CollectionUI 的做法；静态 UI 结构由场景持有。

## 数据资产变更

- 新增 `Assets/Data/GameContentCatalog.asset`，绑定：
  - 角色：DefaultCharacter、BlueWarrior。
  - 武器：FireBall、Knife、Axe、Umbrella、Aura。
  - 升级：对应五个武器升级项目。
- `DefaultCharacter.asset`
  - ID：`character_default`。
  - 起始武器：FireBall。
  - 被动：“旅者直觉”，Reroll Flat +1。
  - 默认解锁。
- `BlueWarrior.asset`
  - ID：`character_blue_warrior`。
  - 起始武器：Axe。
  - 被动：“不屈”，Revival Flat +1。
  - LifetimeKills 解锁，要求 100。
- 五个武器资产新增稳定 weaponID、显示名称和收藏描述。
- 五个升级资产补充收藏描述；武器型升级最终展示仍以 WeaponDataSO 为权威来源。
- 所有新增脚本和 ScriptableObject 均包含对应 Unity `.meta` 文件。

## 场景变更

### MainLevel

- 删除场景中固定存在的 FireBall 武器对象及其序列化引用。
- 起始武器改由 CharacterSelectionSession、CharacterDataSO 与 LevelUpManager 在运行时共同决定。

### MainMenu

- 主菜单 MenuFrame 调整为可容纳三按钮的尺寸。
- 新增场景对象 ButtonGroup，并挂载 VerticalLayoutGroup。
- StartButton、CollectionButton、QuitButton 作为同级子节点纵向布局，配置循环导航。
- 新增默认停用的 CollectionPanel 固定骨架：
  - 标题。
  - 账号金币与 Seal 文本。
  - CharacterTab、WeaponTab、UpgradeTab。
  - CollectionContent。
  - CollectionBackButton。
- MainMenuController 序列化引用 CollectionButton 和 CollectionUI。
- CollectionUI 序列化引用固定面板、内容根节点、三个页签与返回按钮。
- 一次性 Editor 作者脚本仅用于把固定 UI 写入场景，完成后已经删除，不进入正式项目。

## QA 与测试变更

- 新增 `PhaseThreeAccountProgressTests.cs`
  - 默认账号、迁移和数据归一化。
  - 主档损坏后恢复上一份有效备份。
  - 未来版本账号进入只读保护。
  - 累计击杀自动解锁和金币购买扣款。
  - 最近角色与收藏发现持久化。
  - 单槽 Seal 与 RunState Banish 分域。
  - 升级池统一过滤 Seal 与 Banish。
  - 角色起始武器、被动稳定来源和单局只结算一次。
- 新增 `MainMenuSceneStructureTests.cs`
  - 不进入 Play Mode 即可在 MainMenu 场景找到 CollectionButton 与 CollectionPanel。
  - 三按钮使用 VerticalLayoutGroup，且矩形范围位于 MenuFrame 内。
  - 固定收藏骨架引用完整、默认停用。
  - 武器型升级复用 WeaponDataSO 权威名称与描述。
- 修改 `CharacterSelectionTests.cs`
  - 覆盖锁定黑影、锁定信息隐藏、解锁条件浮层、累计击杀解锁与金币购买。
- 修改 `LevelUpManagerLoadoutTests.cs`
  - 覆盖按角色数据授予起始武器和移除场景固定武器后的兼容行为。
- 修改 `MainMenuPlayModeTests.cs`
  - 覆盖收藏入口、打开/关闭状态、按钮交互和主菜单焦点恢复。

## 等价手动搭建

1. 在 CharacterDataSO 中增加稳定 Character ID、Starting Weapon、Passive 和 Unlock 配置。
2. DefaultCharacter 绑定 FireBall，配置 Reroll +1，并设为默认解锁。
3. BlueWarrior 绑定 Axe，配置 Revival +1，并设置累计击杀 100 解锁。
4. 为五个 WeaponDataSO 配置稳定 weaponID、收藏名称和描述；保持五个 UpgradeDataSO 的 upgradeID 唯一。
5. 创建 GameContentCatalogSO 资产，按角色、武器、升级三个列表绑定首版内容。
6. 从 MainLevel 删除固定 FireBall 对象，确认 LevelUpManager 仍能从当前角色数据创建武器运行时对象。
7. 在 MainMenu 的 MenuFrame 中建立带 VerticalLayoutGroup 的 ButtonGroup，将三个按钮按顺序放入并设置循环导航。
8. 在 MainMenu Canvas 下建立默认停用 CollectionPanel，配置三个页签、内容根节点、账号信息和返回按钮。
9. 把 CollectionButton、CollectionUI 和固定骨架引用分别绑定到 MainMenuController 与 CollectionUI。
10. 固定 UI 必须在 Scene 编辑状态可见；运行时只生成内容条目，不应克隆主菜单按钮或动态创建固定页面。

## 验证结果

- 老大已经完成功能与 UI 修正的人工复测，未发现剩余问题。
- EditMode：60/60 Passed。
- PlayMode：20/20 Passed。
- 功能与 UI 修正报告：`Logs/Automation/20260829-094101/summary.json`。
- 归档前再次执行 `Tools/Run-ProjectChecks.ps1 -TestPlatform All -NoGraphics`：
  `Logs/Automation/20260829-164404/summary.json`，质量门禁通过。
