# Session 14 进度归档：角色属性系统阶段三

- 日期：2026-08-29
- 基线提交：`8fd979b7971d782a8a7d673868e9739a0b7a13c4`
- 状态：阶段三功能已实现；自动化门禁与老大人工复测均通过，进入归档与 main 集成。
- 范围：版本化账号持久化、角色解锁、角色选择页锁定与详情、起始武器、固有被动、收藏页面、跨局 Seal、单局结算，以及主菜单收藏 UI 优化。

## 已确认的首版策划

| 策划项 | Session 14 结论 |
| --- | --- |
| 蓝衣战士解锁 | 账号累计击杀 100 名敌人后自动永久解锁。 |
| Seal 初始容量与上限 | 新账号初始 1 槽，本阶段上限同为 1，不提供第二槽位和额外获得方式。 |
| 收藏首版分类 | 角色、武器、升级项目。 |
| 账号金币 | 单局结算后跨局保存，可供未来金币角色解锁等账号消费。 |
| 蓝衣战士起始武器 | 斧子。 |

## 已完成

| 模块 | 当前结果 |
| --- | --- |
| 账号权威状态 | 新增 AccountProgressData、AccountProgressService 和存储抽象，统一管理账号金币、累计金币、累计击杀、角色解锁、收藏发现、最近角色与 Seal。 |
| 版本化存档 | 账号 JSON 使用版本号、临时文件和上一份有效备份；主档损坏时保留损坏副本并恢复备份，未来版本档进入只读安全模式，不覆盖高版本数据。 |
| 跨局结算 | Restart、返回主菜单和应用退出统一提交本局金币与击杀；GameFlowManager 保证同一局只结算一次。 |
| 角色解锁 | 默认角色直接解锁；蓝衣战士按累计击杀自动解锁；金币购买型解锁已有通用扣款按钮和永久解锁能力，暂未配置正式金币角色。 |
| 锁定角色展示 | 锁定头像显示黑色剪影；鼠标悬停、键盘或手柄选中时隐藏姓名、属性、立绘、起始武器和被动，只显示解锁条件浮层。 |
| 角色详情 | 已解锁角色展示基础属性、起始武器和固有被动；最近确认角色写入账号进度。 |
| 起始武器 | 默认角色使用火球，蓝衣战士使用斧子；MainLevel 不再固定放置火球武器，LevelUpManager 按本局角色配置授予起始武器。 |
| 固有被动 | 默认角色“旅者直觉”提供 Reroll +1；蓝衣战士“不屈”提供 Revival +1；PlayerStats 使用稳定来源替换角色被动，普通重算不会重复叠加。 |
| 收藏页面 | 主菜单内提供角色、武器、升级项目三类收藏；显示账号金币和 Seal 使用量，发现状态由账号进度保存。 |
| 收藏词条 | 武器型升级复用 WeaponDataSO 的权威名称、图标和描述，消除升级资产重复文案造成的标点漂移。 |
| 跨局 Seal | 使用稳定 upgradeID 保存账号级过滤；首版可启用一项，解除后槽位可复用，普通升级与宝箱候选共用过滤入口。 |
| Seal/Banish 分域 | Seal 属于账号跨局进度；Banish 仍属于 RunState 单局状态。重置本局只清除 Banish，不影响 Seal。 |
| 主菜单 UI | 收藏按钮、固定收藏面板和三个主菜单按钮已序列化进 MainMenu 场景；VerticalLayoutGroup 管理按钮布局，退出按钮保持在主菜单框体内。 |
| QA | 增加账号存储、迁移、解锁、金币购买、角色被动、起始武器、Seal/Banish、局结算和主菜单场景结构测试。 |

## 关键运行链路

### 账号读取与保存

1. AccountProgressService 首次访问时选择正式 JSON 存储；批处理测试使用内存存储，避免污染真实用户目录。
2. JsonAccountProgressStorage 优先读取 `Application.persistentDataPath/account-progress.json`。
3. 旧版数据经 AccountProgressMigrator 逐版本迁移并归一化；空白、重复稳定 ID、负数和超出 Seal 容量的数据被安全修复。
4. 保存时先写 `.tmp` 并重新解析验证，只把可解析的旧主档复制为 `.bak`，再替换主档。
5. 主档损坏时保留 `account-progress.corrupt-时间戳.json` 并恢复备份；主档与备份都不可用时建立安全新档。
6. 读取到高于当前客户端支持版本的存档时进入只读模式，不自动降级或覆盖原档。

### 局结算与自动解锁

1. RunState 继续保存本局金币、击杀和 Banish。
2. GameFlowManager 在重开、返回主菜单或退出时调用一次 `CommitRunProgressIfNeeded`。
3. AccountProgressService 把本局金币同时加入可消费账号金币与历史累计金币，并累计击杀。
4. 角色选择页打开或账号变化时重新评估自动解锁条件；累计击杀达到 100 后永久解锁蓝衣战士。

### 角色选择、起始武器与被动

1. CharacterSelectionUI 从账号服务刷新每个槽位的解锁状态。
2. 锁定槽位只显示黑影与解锁条件；金币型条件可以在浮层中直接购买。
3. 确认已解锁角色后，CharacterSelectionSession 保存本局选择，账号服务保存最近角色 ID。
4. PlayerStats 从 CharacterDataSO 读取基础属性，并以角色稳定 passive sourceId 应用固有被动。
5. LevelUpManager 根据已选角色的 startingWeapon 授予起始武器，并把实际持有武器记录为已发现。

### 收藏与 Seal

1. GameContentCatalogSO 集中列出首版角色、武器和升级项目。
2. CollectionUI 根据账号发现状态构建数据条目；固定页面骨架已经存在于 MainMenu 场景。
3. 对已发现升级点击 Seal 时，由 AccountProgressService 检查稳定 ID、发现状态和 1 槽容量后保存。
4. LevelUpManager 在同一合法候选入口分别过滤 AccountProgressService 的跨局 Seal 和 RunState 的单局 Banish。
5. 武器型升级详情回到 WeaponDataSO 读取权威词条，UpgradeDataSO 只在非武器升级时作为后备来源。

## Unity 等价配置说明

### 角色数据

- `DefaultCharacter.asset`
  - Character ID：`character_default`。
  - 起始武器：FireBall。
  - 固有被动：“旅者直觉”，Reroll Flat +1。
  - 解锁类型：默认解锁。
- `BlueWarrior.asset`
  - Character ID：`character_blue_warrior`。
  - 起始武器：Axe。
  - 固有被动：“不屈”，Revival Flat +1。
  - 解锁类型：LifetimeKills，要求 100。

### 内容资产

- 五个 WeaponDataSO 配置稳定 weaponID 和收藏显示描述。
- 五个 UpgradeDataSO 配置收藏描述，并继续使用稳定 upgradeID。
- `GameContentCatalog.asset` 绑定首版两个角色、五种武器和五个升级项目。
- MainLevel 移除场景固定的 FireBall 对象；起始武器完全由角色数据和 LevelUpManager 初始化。

### MainMenu 场景

- MenuFrame 尺寸调整为约 440×600，以容纳三个按钮。
- ButtonGroup 挂载 VerticalLayoutGroup，按“开始游戏、收藏、退出游戏”排列。
- 三个按钮配置循环键盘/手柄导航。
- CollectionPanel 作为 Canvas 下的固定场景对象，默认停用。
- CollectionPanel 内序列化标题、账号金币/Seal 文本、角色/武器/升级页签、CollectionContent 和返回按钮。
- MainMenuController 显式绑定 CollectionButton 与 CollectionUI；运行时只绑定行为，不克隆静态按钮或创建固定页面骨架。
- CollectionUI 运行时只根据内容目录创建可变条目和 Seal 操作控件。

## 验证状态

### 老大人工验证

- 账号金币、角色解锁、角色选择详情、起始武器、固有被动、收藏和 Seal 建议复测项均通过。
- 锁定角色黑影、信息隐藏、解锁条件浮层和输入选择行为通过。
- 主菜单按钮布局修正后，收藏按钮在非运行状态可见，退出按钮保持在框体内。
- 收藏中的武器与对应武器型升级词条已经统一，复测未发现异常。

### 自动化验证

- Unity：2022.3.62f3c1。
- EditMode：60/60 Passed。
- PlayMode：20/20 Passed。
- 编译错误、空引用异常和断言异常扫描：0。
- Meta 缺失：0。
- `git diff --check`：通过。
- 功能与 UI 修正报告：`Logs/Automation/20260829-094101/summary.json`。
- 归档前完整门禁：`Logs/Automation/20260829-164404/summary.json`，质量门禁通过。

## 已知边界

- 首版只有一个 Seal 槽位，没有第二槽位、容量成长或额外获得方式。
- GoldPurchase 解锁流程已经实现和自动化验证，但当前两个正式角色没有使用金币购买条件。
- 其他角色解锁类型只保留数据扩展入口，尚未实现具体规则。
- 收藏首版不包含敌人图鉴、成就、统计详情、筛选或搜索。
- 账号存档没有设置页重置入口、云同步、平台账号合并或本地化错误提示。
- 应用异常崩溃时无法保证执行退出结算；正式长局可在未来考虑阶段性安全结算。

## 下一步 Todo

### MVP 回归

- 新 Session 开始前从已更新的 `main` 创建 Worktree，并确认账号新档、旧档迁移和角色起始武器仍通过门禁。
- 第一种正式远程敌人接入时，验证 EnemyProjectile 的来源伤害快照、Defang 和对象池回收链路。
- 后续增加角色或升级资产时，检查稳定 ID、内容目录、收藏发现和存档兼容性。

### 工程完善

- 为账号进度增加设置页查看、重置确认和更明确的存档错误提示。
- 把角色、解锁、收藏、被动和存档消息迁移为本地化键值。
- 为收藏运行时条目建立可复用 Prefab 或池化方案，避免内容规模扩大后继续由代码临时拼装大量条目。
- 评估长局阶段性账号结算和异常退出恢复策略。

### 长期扩展

- 设计 Seal 第二槽位、容量成长和获得方式，并提供版本迁移。
- 增加更多角色解锁类型、角色被动和差异化起始武器。
- 扩展敌人图鉴、成就、累计统计和收藏筛选。
- 在正式远程敌人之后规划 Boss、胜利条件和完整局结束流程。
