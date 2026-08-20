# Session 10 · 玩家装备栏、暂停等级信息与游戏主页面

> **本次性质**：建立玩家装备容量规则与装备栏 HUD，补充暂停时的装备等级表现，并把游戏启动流程接入正式主菜单。
> **日期**：2026-08-20
> **状态**：已实现；Unity 2022.3.62f3c1 编译、场景序列化检查、EditMode/PlayMode 自动化测试均通过，主菜单已由老大手动验证可以进入游戏。

## 一、本次完成内容

| 类别 | 项目 | 状态 |
|---|---|---|
| 装备容量 | 武器种类上限 6；为未来能力系统预留能力上限 6 | ✅ |
| 武器登记 | LevelUpManager 保存按首次获得顺序排列的武器清单 | ✅ |
| 满槽保护 | 达到 6 种武器后，新的武器升级候选被过滤；已有武器仍可继续升级 | ✅ |
| 装备栏 | MainLevel 右上角固定 6×2 网格，第一行武器、第二行能力 | ✅ |
| 图标表现 | 武器图标统一填充槽位，并允许按 WeaponDataSO 配置补偿缩放与偏移 | ✅ |
| 暂停等级 | 手动按 Escape 暂停时显示装备等级；九级以内为点阵，超过九级显示数字 | ✅ |
| 等级强调 | 等级点阵/数字区域放大并增加外框、内底和点位边框 | ✅ |
| 主菜单 | 新增 MainMenu 场景、标题、玩家装饰、开始游戏和退出游戏按钮 | ✅ |
| 启动流程 | Build Settings 首场景改为 MainMenu，开始按钮异步加载 MainLevel | ✅ |
| 自动化测试 | EditMode 27/27、PlayMode 9/9 全部通过 | ✅ |

## 二、运行时流程

```text
启动游戏
   ↓
MainMenu（Build Index 0）
   ├─ 开始游戏 → 锁定按钮 → 恢复 Time.timeScale → 异步加载 MainLevel
   └─ 退出游戏 → 正式构建中 Application.Quit

MainLevel
   ├─ PlayerLoadoutDisplayUI：右上角 6×2 装备栏
   ├─ LevelUpManager：管理武器种类、等级和容量
   └─ Escape 手动暂停 → GameFlowManager.ManualPauseChanged
                              ↓
                       装备栏展开等级区域
```

### 装备容量和登记

- `PlayerLoadoutRules.MaxWeaponCount` 与 `MaxAbilityCount` 都为 6，UI 和运行时规则共用同一常量来源。
- `LevelUpManager` 继续使用稳定武器 ID 去重，同时用 `_ownedWeaponOrder` 保留显示顺序。
- 获得新武器前检查空槽；已有武器的升级不受种类上限影响。
- 场景预置武器超过上限时，多余武器会被禁用并输出明确错误，避免场景配置静默突破容量。
- `OwnedWeaponsChanged` 供 HUD 事件驱动刷新，不需要每帧扫描玩家子节点。

### 装备栏和等级表现

- `PlayerLoadoutDisplayUI` 在 `Awake` 阶段一次性创建 6 个武器槽和 6 个能力槽；运行中只更新内容和显隐。
- 面板锚定 Canvas 右上角，紧凑状态只占用图标槽高度；手动暂停时动态增加等级区域高度。
- 每个槽位预建 9 个等级点和一个数字文本，切换暂停不会反复 Instantiate/Destroy。
- 当前持有武器使用 `WeaponDataSO.icon`；`loadoutIconScale` 和 `loadoutIconOffset` 只影响装备栏，不改变战斗贴图。
- `GameFlowManager` 只发布“手动暂停”状态变化；升级暂停和 Game Over 不会误展开装备等级。

### 主菜单

- `MainMenu.unity` 使用场景原生 UI 层级：草地平铺背景、深色遮罩、标题、玩家像素装饰、米色边框菜单面板、开始/退出按钮、版本文本和 EventSystem。
- `MainMenuController` 负责按钮监听、默认焦点、版本号、防重复点击、Build Settings 检查和异步场景加载。
- Editor 中退出动作被条件编译忽略，避免自动化测试或编辑器被关闭；正式构建调用 `Application.Quit()`。
- 本轮没有加入设置、存档、继续游戏或返回主菜单按钮，因为对应系统尚未实现。

## 三、实际改动文件

### 新增运行时与场景

- `Assets/Scripts/Core/PlayerLoadoutRules.cs`：武器/能力容量规则常量。
- `Assets/Scripts/UI/PlayerLoadoutDisplayUI.cs`：6×2 装备栏、图标和暂停等级表现。
- `Assets/Scripts/UI/MainMenuController.cs`：主菜单行为控制器。
- `Assets/Scenes/MainMenu.unity`：场景原生主菜单层级。
- 上述脚本和场景的 `.meta` 文件。

### 新增测试

- `Assets/Tests/Editor/MainMenuBuildSettingsTests.cs`：验证 MainMenu/MainLevel 场景顺序。
- `Assets/Tests/PlayMode/MainMenuPlayModeTests.cs`：验证主菜单引用、默认焦点、加载锁、时间恢复和真实场景切换。
- 本次同时保留并纳入回归的装备栏/容量测试：`LevelUpManagerLoadoutTests`、`PlayerLoadoutDisplayPlayModeTests`。

### 修改运行时代码、资产和配置

- `Assets/Scripts/LevelUpManager.cs`：武器顺序清单、容量判断、变化事件和满槽升级池过滤。
- `Assets/Scripts/Core/GameFlowManager.cs`：手动暂停状态、幂等暂停和 `ManualPauseChanged` 事件。
- `Assets/Scripts/Data/WeaponDataSO.cs`：装备栏图标、缩放和偏移配置。
- `Assets/Scripts/UpgradeUIItem.cs`：优先使用武器统一图标，保留旧升级资产图标回退。
- 五个武器数据资产：补充装备栏图标与显示参数。
- `Assets/Scenes/MainLevel.unity`：挂载装备栏表现和暂停联动所需引用。
- `ProjectSettings/EditorBuildSettings.asset`：MainMenu 置于首场景，MainLevel 保持第二场景。

一次性场景生成器只存在于隔离 Unity 项目中，生成完成后已删除，没有进入正式项目。

## 四、验证状态

- **已静态检查**：MainMenu 场景 92 个本地序列化对象、控制器按钮/TMP 引用、Build Settings 场景 GUID、场景本地 fileID 引用均完整；未发现缺失本地引用。
- **已编译验证**：Unity 2022.3.62f3c1 在隔离项目中完成脚本编译和场景保存，退出码 0。
- **已运行验证**：EditMode 测试 27/27 通过。
- **已运行验证**：PlayMode 测试 9/9 通过；新增用例真实加载 MainMenu、点击开始按钮并进入 MainLevel。
- **已人工验证**：老大重新打开 Unity 后找到 MainMenu，并确认可以进入游戏。
- **已保存报告**：`Logs/MainMenuEditMode.xml`、`Logs/MainMenuPlayMode.xml`。
- **未验证**：退出按钮在正式独立构建中的实际进程退出；Editor 中按设计不会退出编辑器。

## 五、Unity 等价手动搭建步骤

### 主菜单

1. 在 `Assets/Scenes` 创建 `MainMenu` 场景。
2. 创建 Screen Space Overlay Canvas，CanvasScaler 使用 1920×1080 参考分辨率。
3. 按 `MainMenu.unity` 的层级建立背景 Image、遮罩、标题、玩家 Sprite、菜单外框、两个 Button、版本 TMP 文本和 EventSystem。
4. 创建空物体 `MainMenuController`，挂载 `MainMenuController.cs`。
5. Inspector 中绑定 `StartButton`、`QuitButton`、`VersionText`，`gameplaySceneName` 设置为 `MainLevel`。
6. 在 Build Settings 中按 `MainMenu`、`MainLevel` 顺序加入并启用场景。

### MainLevel 装备栏

1. 在 MainLevel 的 Canvas 下创建或保留 `PlayerLoadoutDisplayUI` 挂载对象。
2. 绑定 `LevelUpManager`、`GameFlowManager` 可解析的场景对象；组件会在 Awake 时创建 6×2 槽位。
3. 设置 `slotSize`、`levelAreaHeight`、`levelDotSize` 和颜色字段；手动暂停时等级区域会自动展开。
4. 在每个 `WeaponDataSO` 绑定 `icon`，必要时调整 `loadoutIconScale` 与 `loadoutIconOffset`。

## 六、下一步 Todo

### MVP 回归

- 在正式独立构建中验证退出按钮确实退出进程。
- 通过 F8 调试面板确认第 6 种武器可获得，第 7 种武器不会进入候选；已有武器在满槽后仍可升级。
- 用不同等级、不同图标比例的武器复测暂停点阵、数字和槽位填充。
- 在低分辨率和非 16:9 窗口中复测 MainMenu 和装备栏 CanvasScaler 表现。

### 工程完善

- 实现能力数据、能力获得/升级和第二行装备栏的真实数据绑定。
- 增加返回主菜单、设置、存档/继续游戏入口，并补齐对应测试。
- 将主菜单、暂停和装备栏文字接入本地化键值系统。
- 为正式主菜单补充背景音乐、按钮音效和过渡动画。

### 长期扩展

- 增加角色选择、局内结算、成就和版本信息页。
- 将装备栏等级表现抽象为可复用的武器/能力统一视图模型。
- 建立启动场景、场景切换和构建版本的 CI 检查，避免 Build Settings 与实际场景资产再次脱节。
