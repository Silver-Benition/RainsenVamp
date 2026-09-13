# Session 22 代码变更：账号升级配置、事务与商店

- 日期：2026-09-13；最终实施提交 `9f0dedac6934a25e60eee02689a5882b1fb48b9a`。
- 整体提交范围：Session 21 基线 `5dbae229` 到最终实施，共 80 个文件，含源码、测试、场景、Prefab、21 项配置及 meta。
- 测试与验收结果见 [进度归档](../progress/session-22.md)。

## 类与核心方法

| 文件/类 | 本次职责与方法 |
| --- | --- |
| `Assets/Scripts/Data/AccountUpgradeDataSO.cs` | 新增单属性等级表；`Validate` 检查稳定 ID、等级、价格和修改器；`CanToggleEnabled` 约束可关闭属性；`GetDisplayName/GetDescription` 提供配置回退文本/键值接口。`AccountUpgradeLevel` 保存逐级价格和累计修改器。 |
| `Assets/Scripts/Data/AccountUpgradeCatalogSO.cs` | 新增完整 21 属性目录、封印额外容量价格及图标引用；`Validate` 检查类型覆盖、唯一性和合法等级，`Find` 拒绝重复 ID 歧义。 |
| `Assets/Scripts/Data/AccountProgressData.cs` | 最终版本 v3；新增 `AccountUpgradePurchaseRecord` 的逐级 `paidCosts` 和独立 `disabledAccountUpgradeIds`。`MigrateToCurrent/Normalize/FindValidPurchase` 迁移、去重、保留历史并推导封印容量；`IsUpgradeAlwaysEnabled` 保护四种主动使用资源。 |
| `Assets/Scripts/Core/AccountProgressService.cs` | `GetUpgradeLevel/GetLastPaidCost` 读取购买历史；`TryPurchaseUpgrade/TryRefundUpgrade/TrySetUpgradeSealed/TrySetAccountUpgradeEnabled` 走候选数据保存事务；`TryCommitCandidate` 保存成功后替换数据与索引并发布 Changed。`IsAccountUpgradeEnabled` 查询下局偏好。 |
| `Assets/Scripts/Core/AccountProgressStorage.cs` | `JsonAccountProgressStorage.Load/Save` 保留未来版本主档/备份只读保护，使用同目录临时文件和 File.Replace/File.Move 保存，不污染调用者快照。现有内存 Storage 用于隔离测试。 |
| `Assets/Scripts/Core/AccountUpgradeResolver.cs` | 新增 `CreateSnapshot`：按已购有效等级复制累计修改器，过滤停用的可配置属性；来源 ID 为 `account.upgrades.snapshot`。 |
| `Assets/Scripts/Player/PlayerStats.cs` | 新增目录引用与首次快照标志，`EnsureStatsInitialized` 在生命/资源首次读取前解析局外来源；共享属性公式不变。 |
| `Assets/Scripts/UI/AccountShopUI.cs` | 新增三页签控制器，`ChangeTab/Preview/Lock/Refresh` 管理展示和两阶段选择；`Buy/Refund/ChangeEnabled/FinishOperation` 调用账号服务；`CancelOperation` 同帧去重；`ConfigureNavigation/ScrollToEntry` 保证控件和整卡可达。 |
| `Assets/Scripts/UI/AccountShopEntryUI.cs` | 新增 `Bind/Refresh`，复用图标、价格和按 maxLevel 生成的等级格；OnSelect 与 PointerEnter 只预览，Button 确认才锁定，整卡高亮区分焦点与交易目标。 |
| `Assets/Scripts/UI/AccountShopCancelRelay.cs` | 新增 Cancel 事件转发，让卡片、按钮和 Toggle 共用逐层取消入口。 |
| `Assets/Scripts/UI/AccountShopEffectPresentation.cs` | 新增 `Format`，分别聚合累计值和本次增量；保留绝对量单位、负冷却百分比及 Flat 概率语义。 |
| `Assets/Scripts/UI/MainMenuController.cs` | 新增商店引用、`OpenShop/HandleShopClosed`，维护菜单互斥、加载边界和返回焦点。 |
| `Assets/Scripts/UI/CollectionUI.cs` | 关闭原 Seal 操作入口，保留收藏/图鉴展示。 |
| `Assets/Editor/AccountUpgradeSetup.cs` | 新增 `Build` 商店作者工具、`BuildEntryTemplate`、`ConfigureMainMenuNavigation`；填缺失图标、显式序列化 UI。最终版本重建已存在的商店，保留现有价格/上限/自定义配置；只对匹配旧值的移速/磁吸做已批准转换。 |

本 Session 没有删除生产 C# 类，没有修改武器、敌人、跨世界、对象池、物理、ProjectSettings 或性能设施。

## 数据与存档规则

`Assets/Data/AccountUpgrades/` 保存完整目录和 21 个属性资产；当前各属性暂定 3 级，价格 100/200/300，封印额外容量暂定 4 级，价格 100/250/500/1000。价格、maxLevel 与每一级累计修改器可独立配置。

- MoveSpeed/Magnet 最终累计修改器为 AdditivePercent 0.05/0.10/0.15；Defang 是 Flat 概率，不把 0 基础概率改成乘法百分比。
- 每级配置是该等级完整累计快照。购买二级直接使用二级快照，不把一级再叠一遍；说明中的“本次升级”由两个累计档位求差。
- v1→v2 加入购买记录与合法容量推导；v1/v2→v3 默认所有属性启用。v3 独立保存停用 ID，购买及退款记录不变。
- 降低当前配置上限只限制生效/继续购买，不删除历史实付；重复或非法购买记录保留但拒绝歧义交易，不猜测金额。
- 封印保留一个免费容量，额外容量由合法购买记录推导。放逐、重投、跳过是局内资源，封印为局外容量，均不可停用；复活属于可关闭属性。
- 勾选变更仅影响以后创建的账号快照，不订阅战斗中的账号变化、不逐帧扫描或重算全体对象。
- 显示名和描述与 stableId 分离；回退文本和本地化键已预留，尚不具备完整语言切换服务。

## 场景、Prefab 与等价配置

下列引用已保存，无需重复搭建；用于复盘 Inspector 接线。

1. MainMenu 的 Canvas 上保留 `AccountShopUI`，绑定成长目录、GameContentCatalog、AccountShopPanel、三个页签、返回/购买/退款按钮、金币/详情/状态文本、详情图标、ScrollRect、条目 Prefab 和 `ShopEnabled` Toggle。
2. `AccountShopPanel` 固定骨架含标题、背景预留区、竖页签、金币框、右侧网格与底部详情。ScrollRect 使用 RectMask2D 视口、三列 GridLayoutGroup、ContentSizeFitter 和显式滚动条；卡片大小随视口宽度调整。
3. `Assets/Prefab/UI/AccountShopEntry.prefab` 保存正方形 IconFrame（AspectRatioFitter 明确 1:1）、保比例 Image、InformationFrame、等级格模板和 Price。等级格五格一行，数量由配置上限决定；图标复用现有 Point Sprite，不改纹理导入器。
4. `ShopEnabled` 是详情区 Toggle，绑定背景/Checkmark/标签及 CancelRelay；勾选使用即时显隐，刷新用 SetIsOnWithoutNotify，避免递归保存。只有锁定可配置属性才显示。
5. MainMenuController 绑定 ShopButton 和 AccountShopUI。开始→收藏→商店→退出形成四按钮显式上下导航环；关闭商店后焦点返回 ShopButton。
6. MainLevel 的 PlayerStats 只新增 `accountUpgradeCatalog` 序列化引用，指向同一目录；本次 MainLevel 场景差异只有该引用。原角色选择、生命、次数和能力初始化链不变。
7. 收藏页不再接收 Seal 操作；道具排除页仍使用内容目录中的已发现候选。进阶四项只有占位卡，不新增战斗属性类型。
8. 键盘沿用既有 Submit（Return/Enter/Space）与 Cancel（Escape）、方向导航及既有手柄映射。未新增 Input 配置、Layer、Collider 或物理规则。
9. 必要时用菜单 `RainsenVampSur/Account/Build Session 22 Shop` 重建已存在商店。工具最终版本依赖现有目录与控制器，不是从空项目生成全游戏的工具；平时验收直接打开已保存场景。

## 测试覆盖

- `AccountUpgradeTests`：21 属性逐级买退、实付变价、降上限、存档/备份/只读、溢出、封印占用及来源叠加。
- `AccountShopPresentationTests`：累计/增量及百分比语义、移速/磁吸实际效果、定向转换保留价格/上限、动态图标等级格。
- `AccountUpgradePreferenceTests`：v3 迁移、重载、免费启停、失败不发布、四类强制启用、防存档注入、零级偏好及退款权益、角色/其他来源与旧快照隔离。
- `MainMenuSceneStructureTests`：商店场景引用及主菜单导航。
- `AccountShopPlayModeTests`：真实 Move/Submit/Cancel、两阶段交易、切页、退款隐藏、Toggle 焦点与失败恢复、全属性描述布局、两分辨率图形检查、整卡往返滚动、开局/选角/资源/候选耗尽回归。

最终 QA 与 main 集成后完整测试均为 EditMode 160/160、PlayMode 63/63，无失败或编译错误。具体报告路径和用户验收范围以同编号进度归档为准。
