# Session 14 复盘：跨局状态、单局状态与静态 UI 所有权必须分清

- 日期：2026-08-29
- 主题：账号持久化、角色内容、收藏与 Seal/Banish 分域、主菜单 UI 作者方式，以及 Codex Worktree 收尾流程。

## 本次最重要的架构决策

### 账号进度与单局状态使用不同权威来源

Session 13 的 RunState 适合保存当前一局的击杀、金币、可消耗次数和 Banish，但不能直接跨场景或跨进程持久化。Session 14 新增 AccountProgressService，把以下状态收敛到账号域：

- 可消费账号金币。
- 历史累计金币与累计击杀。
- 永久角色解锁。
- 角色、武器和升级项目发现。
- 最近确认角色。
- Seal 容量与已 Seal 升级。

RunState 继续拥有单局金币、击杀和 Banish。局结束时只把允许跨局的统计结算到账号服务。Seal 与 Banish 即使都过滤升级候选，也不能共用列表、重置逻辑或 UI 语义。

### 存档的安全性不仅是“能写 JSON”

首版账号存档同时处理四类风险：

1. 版本迁移：无版本兼容 JSON 迁移到版本 1。
2. 数据归一化：负数、重复 ID、空白 ID 和超出容量的 Seal 被安全纠正。
3. 写入中断：先写临时文件并重新解析验证，再替换主档。
4. 版本倒退：高于当前客户端版本的存档只读，不让旧客户端覆盖新数据。

备份只从可解析的旧主档生成。主档损坏时先保留损坏副本，再读取上一份有效备份，避免错误写入把最后一份好数据一起破坏。

### 角色差异必须来自角色数据，而不是场景偶然状态

阶段二已有 CharacterDataSO 和 PlayerStats 稳定修改器，但 MainLevel 仍固定放置 FireBall。这会让角色的起始武器只是 UI 文案，不是真正的数据驱动。

Session 14 删除场景固定武器，让 CharacterDataSO 同时提供：

- 稳定角色 ID。
- 起始武器。
- 固有被动。
- 解锁条件。

LevelUpManager 只消费本局确认角色的配置；PlayerStats 使用稳定 passive sourceId 替换被动。这样切换角色不会残留上一角色效果，普通重算也不会重复叠加。

### 锁定状态属于信息权限，不只是按钮不可点击

锁定角色如果仍显示属性、立绘、武器和被动，只把确认按钮禁用，玩家仍然能提前看到全部内容。最终交互明确为：

- 槽位头像显示黑影。
- 鼠标悬停、键盘或手柄选中都进入锁定状态。
- 详情区域隐藏全部角色信息。
- 同一位置显示解锁条件浮层。
- 只有金币型条件显示购买按钮。

这要求 CharacterSelectionSlotUI 同时处理指针与 EventSystem 选择事件，CharacterSelectionUI 则必须把“当前角色”和“是否有权展示详情”分成两个判断。

### 固定 UI 骨架应由场景持有

收藏首版最初通过运行时克隆 StartButton，并把 QuitButton 向下推。功能可运行，但产生三个直接问题：

- 非运行状态看不到收藏入口和固定页面。
- 第三个按钮超出原主菜单框体。
- CollectionUI 只是运行时叠加在 MainMenu 上，静态结构无法通过 Scene 直接审查。

修正后采用“场景固定骨架 + 运行时数据条目”：

- CollectionButton、CollectionPanel、页签、内容根节点和返回按钮序列化进 MainMenu。
- VerticalLayoutGroup 负责三个主菜单按钮的稳定布局。
- MainMenuController 只绑定行为。
- CollectionUI 只生成数量随内容变化的条目和 Seal 操作。

收藏当前仍适合留在 MainMenu Scene：它依赖同一账号服务、内容目录和主菜单返回链路，首版规模不足以支付独立 Scene 的加载、焦点和生命周期成本。若以后包含大量分类、预览动画、筛选或异步资源，再评估拆分。

### 收藏词条必须有唯一权威来源

武器分类最初读取 WeaponDataSO 描述，升级项目分类读取 UpgradeDataSO 描述。两份内容语义相同，却只差一个中文句号，说明数据所有权已经漂移。

最终规则为：武器型升级在收藏中复用对应 WeaponDataSO 的名称、图标和描述；UpgradeDataSO 只为真正独立的非武器升级提供后备词条。新增场景结构测试把这条规则固定下来。

## 人工反馈揭示的问题

### 功能正确不代表 Scene 作者方式正确

自动化可以验证点击收藏按钮能打开页面，却不会自动说明该按钮是否在 Unity 编辑状态存在、是否超出美术框体。老大的截图直接暴露了运行时克隆静态控件的缺陷。

改进后测试不只进入 PlayMode 点击按钮，还在 EditMode 打开 MainMenu 场景并检查：

- CollectionButton 和 CollectionPanel 已序列化。
- ButtonGroup 使用 VerticalLayoutGroup。
- 三个按钮矩形位于 MenuFrame 内。
- 收藏固定引用完整且默认停用。

### 重复文案即使只有一个标点差异，也说明数据源不唯一

这次差异不是排版偶然，而是同一个概念从两个 ScriptableObject 字段读取。修复重点不是删除一个句号，而是明确武器词条所有权，并用测试保证所有武器型升级都复用同一来源。

## QA 与实现过程

### 自动化覆盖扩大到账号与编辑状态场景

阶段三新增了以下高风险测试：

- JSON 迁移、归一化、损坏主档和备份恢复。
- 高版本档只读保护。
- 累计击杀解锁和金币购买扣款。
- Seal 容量、持久化和 Banish 分域。
- 角色起始武器、被动稳定来源和局结算幂等。
- MainMenu 固定收藏骨架、按钮边界和权威词条。

功能与 UI 优化后的门禁达到 EditMode 60/60、PlayMode 20/20。

### 一次性作者工具不应留在正式项目

MainMenu 固定骨架通过一次性 Editor 作者流程生成并保存到场景。完成序列化和测试后，作者脚本已经删除，避免把仅服务一次迁移的入口长期暴露给项目。保留下来的 CollectionUI Editor 方法受 `UNITY_EDITOR` 保护，正式运行不会调用。

## Worktree 与 Git 的工程认识

Session 14 是第一次完整使用 Codex 管理 Worktree。当前 Worktree 从 `main@8fd979b` 创建，默认处于 detached HEAD；原始项目的 `main` 同时保持干净。

本次明确了：

- Worktree 是隔离工作目录，不是长期 QA 副本。
- detached HEAD 上的未提交修改不会自动推进 main。
- 短期分支为 Session 提交提供名称、审查范围和恢复锚点。
- 单开发方不必强制 Pull Request；验证通过后可以 fast-forward 合入原始 main。
- Handoff 是移动同一个会话和工作环境，不是把执行模型的隐式上下文交给另一个规划会话。

后续采用 Sol 规划与审查、Luna 执行时，以冻结方案、分支提交、测试证据和结构化偏差清单传递状态，详见 `DevLog/plans/sol-luna-worktree-session-workflow.md`。

## 做得好的地方

- AccountProgressService 与 RunState 的数据域清晰，没有为实现 Seal 而复用 Banish 存档。
- 存档从首版开始具备版本、临时文件、备份、损坏副本和高版本只读保护。
- 角色起始武器和被动真正接入玩法，不只是补齐选择页文案。
- 锁定角色对鼠标、键盘和手柄保持一致的信息隐藏规则。
- 人工反馈修复后补充了场景结构与权威词条测试。
- 收藏固定 UI 最终可在 Unity 编辑状态检查，动态逻辑只保留给内容条目。
- 所有阶段三功能和 UI 反馈均经过老大人工复测。

## 仍然存在的风险与不足

- OnApplicationQuit 只能尽力结算；进程崩溃或系统强制终止仍可能丢失本局尚未提交的数据。
- 账号错误消息当前写入日志，尚无玩家可见的存档恢复、只读或重置界面。
- GoldPurchase 只有测试角色覆盖，没有正式内容验证实际按钮文案和平衡。
- Seal 只有一个固定槽位，尚无容量成长、获得途径和迁移后的策划验证。
- 收藏只覆盖首版小规模内容；运行时条目未做对象池或虚拟列表。
- UI 文本仍以中文硬编码为主，本地化架构尚未落地。
- 其他解锁条件、敌人图鉴、成就和云存档均不在本阶段范围。

## 后续 Session 交接重点

### 必须复用的底座

- AccountProgressData / Migrator / Storage / Service。
- CharacterDataSO 的 characterID、startingWeapon、passive、unlock。
- GameContentCatalogSO 与稳定 weaponID、upgradeID。
- LevelUpManager 的统一合法候选入口。
- MainMenu 场景固定收藏骨架和 CollectionUI 数据条目边界。

### 下一阶段建议候选

1. 接入第一种正式远程敌人，完成 EnemyProjectile、Defang 和对象池的真实玩法验证。
2. 再规划 Boss、胜利条件和完整局结束流程。
3. 根据优先级补账号设置/重置、存档提示与本地化。
4. Seal 第二槽位与获得方式必须先完成独立策划确认，不能直接扩大版本 1 容量。

### 跨模型启动要求

- Sol 先冻结实施契约与偏差边界。
- Luna 从准确 main 基线创建 Worktree，只修改批准范围。
- Luna 用结构化审查包交付分支和证据。
- Sol 独立读取真实 Diff、复跑门禁并决定是否合入。
