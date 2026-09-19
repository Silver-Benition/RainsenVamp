# Session 23：二十回合竞技场改造与试玩交接

## 目标与授权

本次把持续长局改为实时短回合循环，不是按行动点交替操作的回合制。
老大已确认：20 回合；战后集中属性升级，再进入局内商店；有边界的主世界竞技场；暂时禁用世界切换。
执行范围包括代码、必要场景/数据资产、测试、独立审查、QA 短期分支本地提交；尚未授权集成 main 或归档推送。

- 基线：`37f1a0fc1ca25e3657e7fa14c92e9d22b8110cd3`
- QA：`C:/Unity_Project/RainsenVampSur-QA`
- 分支：`codex/session-23-round-combat`
- 初版审查提交：`2b2aa8829d4370bbdc31eca1d4ede31095258465`
- 9 月 17 日首版运行内容及独立复审提交：`63b9500e5411af6b69afc1bc4df709ebc2800409`；此后仅修改截图测试的排序层和本文档。
- 主检出 `C:/Unity_Project/RainsenVampSur` 保持原 main。

## 玩家规则

1. 第 1–8 回合分别为 20、25、30、35、40、45、50、55 秒；第 9–19 回合为 60 秒；第 20 回合为 90 秒。全部按存活时间通关时累计战斗 17 分 30 秒，不含局间。
2. 最终回合生存满 90 秒或击败指定 Boss 即胜利；死亡先进入既有复活/失败流程。最后一击先记账，再冻结整局结果。
3. 战斗中累积经验和待选等级，不弹升级页。通过回合后依次清场、处理宝箱、四选一属性成长、商店；最终回合直接结果页。
4. 商店四个报价，支持购买、付费刷新、锁定、武器回收和同类同品质合并。锁定保留原报价和品质，下一商店的刷新次数重置。
5. 最多六把独立武器，可同种重复。品质为 1–4；满槽且有相同品质的同种武器时，购买自动合并；最高品质不能再合并。已有五种武器提供四档配置，不新增武器内容。
6. 道具沿用现有六项能力内容，允许持有种类超过六；每项叠加上限使用该项原有等级配置。
7. 材料和账号金币独立。经验掉落在此模式同时给材料与经验；未拾取材料进入储备，后续拾取时按本次基础值兑付等量储备。回收收益只给材料，不发经验、不动用储备。
8. 账号封印映射到商店候选；不扩大至宝箱。局内放逐映射属性候选。免费重投先用尽，再花材料；跳过与放逐使用现有整局次数。
9. 每回合准备时存活玩家回满血、回到中心、重置武器冷却和临时冻结；装备、材料、属性、整局资源及统计保留。回池清场不算击杀，不再发掉落。
10. 默认回合仅要求存活。额外目标架构支持最低存活时间与全部/任意目标组合，独立硬截止或加时；目标报告带回合代次，已关闭回合拒绝迟到报告。

## 实现入口与所有权

- `RoundRunConfigSO / RoundDefinition`：只存配置；`RoundRuntime`：单回合时间、目标、终态。
- `RoundController`：Preparing → Combat → Settling → Upgrades → Shop → 下一回合；整局结束进入 Finished。
- `RunDirector` 保留整局胜负权威与战斗总时钟，驱动回合时钟；暂停和局间不累计战斗时间。
- `RunMaterialWallet` 与 `RunShopService`：局内资金、报价和交易。预留余额、校验容量后提交；钱包与阶段入口拒绝重入。装备、属性和账号发现通知缓冲到提交后，观察者异常不能单边回滚款项。
- `LevelUpManager` 武器顺序表持有独立实例，旧按内容查询继续服务现有功能；`AbilityManager` 保留道具效果和来源替换机制。
- `RoundArena`：矩形位置/速度约束、安全刷怪点。原 WorldLineCoordinator 主世界与对象池链复用；非活动世界不再后台生成。
- `RoundIntermissionUI`：运行时建立属性页、商店、六槽操作和材料 HUD，逻辑由服务提供；新数据保留稳定 ID、名称键和中文回退，沿用项目尚未接入翻译表的约定。
- `RunTelemetry`：同种武器合并统计有效伤害及最高品质，生效时长只计该类型至少有一把装备的战斗时间，排除商店和全部出售后的间隔。
- `PoolManager` 跟踪活跃对象，回合批量回池，拒绝重复释放。
- 账号金币与持久化仍由 `AccountProgressService` 管理，未迁移存档格式。

## Unity 配置与编辑入口

打开 QA 工程的 `Assets/Scenes/MainLevel.unity` 即可试玩，无需手动补挂。

- 原 RunDirector 对象挂 `RoundController`，引用 `Assets/Data/Rounds/Standard20.asset`。
- `Standard20.asset` 引用 `ShopCatalog.asset` 和 `Waves/Wave01.asset` 至 `Wave20.asset`。数值第一版用于建立循环，仍需平衡试玩。
- 场景 `RoundArena` 挂边界权威；子对象 Grid/Tilemap 使用既有草地，四个边缘对象使用 BoxCollider2D 与已有 Sprite。矩形尺寸 24×16；未新增 Layer 或修改全局碰撞矩阵。
- 两个世界的流式地图组件关闭；模式初始化固定主世界并关闭子世界生成对象。
- 现有 Canvas 挂 `RoundIntermissionUI`，引用已有中文 TMP 字体；GameTimer 使用回合编号和倒计时。
- 五种 WeaponDataSO 新增四档品质配置，原等级数据保留给现有独立组件/测试场景。
- `RainsenVampSur/Rounds/Build Session 23` 是首版模板生成工具，会重建默认表和竞技场。已有手工平衡修改后不要直接重建；普通调参直接编辑对应 SO。变更场地尺寸时同时更新场景边界、墙与地面。
- 敌人、弹体、掉落和效果继续使用正式 Prefab 对象池；没有生成替代美术或修改全局物理方案。

## 2026-09-17 首版验证记录

最终验证已完成：

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 静态检查/编译 | 通过 | Git diff --check；两类 Unity 测试实际编译成功 |
| 完整 EditMode | 171/171，0 失败/跳过 | Logs/Automation/20260917-234756/EditMode.xml |
| 完整 PlayMode | 70/70，0 失败/跳过 | Logs/Automation/20260917-234756/PlayMode.xml |
| 完整门禁 | passedQualityGate=true | Logs/Automation/20260917-234756/summary.json |
| 有图形设备回合测试 | 7/7，0 失败/跳过；1080p、720p | Logs/Session23/graphics-overlay.xml |
| 独立增量审查 | PASS / APPROVED | 固定版本 63b9500；原购买原子性缺陷闭合 |
| 人工验收 | 待老大试玩 | 下方清单 |

完整门禁覆盖 63b9500 的所有运行代码、正式资产与配置。之后仅将离屏截图 Canvas 的排序层设到最高以模拟正式 ScreenSpaceOverlay，不改变玩家界面或玩法；改动后重跑上述 7 项图形测试，不重复无关测试。

最终图像位于 `Logs/Session23/ScreenshotsOverlay/`：combat、upgrades、shop 各有 1280×720 与 1920×1080 两张。已检查材料显示、图标、文字和六槽商店布局。截图复核不替代真实输入、音效、手感或性能验收。

本轮历史失败报告保留，不能当作最终通过证据：

- `Logs/Automation/20260917-232524/summary.json`：EditMode 170/170，PlayMode 62/67；五个旧流程断言迁移为批准的回合规则。
- `Logs/Automation/20260917-233637/summary.json`：EditMode 171/171；PlayMode 无 XML，失败。新增离屏测试在无图形环境触发 Unity Tilemap 原生渲染崩溃；测试已对 Null 图形设备关闭场景渲染，仅验证布局。
- `Logs/Session23/graphics-tests.xml`：中间版本 7/7；截图用于发现透明图标问题，最终截图另行生成。
- 独立审查初版发现购买发货早于扣款的同步重入缺陷，已修复并增加真实购买重入及观察者异常用例，最终增量审查已通过。
- 自动化完整 20 回合用例通过加速推进时钟覆盖生命周期，不是完整自然时长试玩或性能采样。

## 人工验收清单

- 从主菜单开局，移动至四面边界；检查镜头、墙体、像素清晰度和有限场地空间感。
- 自然打完数个回合：战斗升级不打断，结束后升级→商店→下一回合，材料/储备与金币区分清楚。
- 鼠标及键盘/手柄导航：选择、重投、放逐、跳过、锁定、购买、满槽合并、回收；没有重复提交或焦点丢失。
- 开下一回合后血量、位置、攻击恢复，场内无上一回合敌人、子弹、冻结和掉落残留。
- 检查复活后的继续战斗、最终 Boss 提前击杀和超时存活胜利、失败结果及再次开局。
- 重点评价敌人密度、回合压力、材料收入/商品价格、四档武器强度。当前数值是可调基线，不宣称已平衡。
- 真实设备帧率、声音、长时间手感和最终美术仍由人工验收。

通过人工门槛后，再按老大明确指令执行 main 集成和三份 DevLog 正式归档/推送。

## 2026-09-19：局间 UI 按参考图调整

本节为最新 UI 迭代；前面的 9 月 17 日报告保留为首版历史证据。

- 布局以老大提供的几何图为准：左上四个商品卡、右侧属性栏、左下道具图标、下方靠右三列两行武器图标、右下下一波按钮。几何图中的色块仅作为分区，不用作成品配色。
- 深色商品卡、顶部材料/刷新、卡底价格和卡外锁定参考提供的 Brotato 截图；采用现有游戏素材和中文字体。
- 已持有武器与道具只显示图标及品质/叠加角标。悬停或键盘/手柄聚焦打开详情；退出后延时关闭，鼠标进入详情按钮区或焦点进入按钮可保持显示；点击武器图标可以固定详情。
- 武器详情提供名称、品质、明确标注的基础品质属性和描述；不会把基础值冒充玩家全局加成后的伤害。道具详情显示已获得层数对应的说明；商品卡则显示下一次购买的等级说明。
- 武器详情保留合并与回收，操作绑定具体武器引用；回收后图标重排不改变本次操作对象。原交易服务与经济规则不变。
- 属性栏按主要/次要展示项目实际拥有的属性；复活、重投、跳过、放逐显示本局剩余次数。未添加 Brotato 独有但本项目尚无的属性。
- 道具图标视口可滚动，导航聚焦到不可见图标时自动将它滚入视口；已验证 31 个临时道具的溢出场景。
- 详情高度随内容变化，位置限制在屏幕内。属性成长页复用同一布局，跳过位于右下区域。

修改入口：

- `Assets/Scripts/UI/RoundIntermissionUI.cs`：视图布局、属性栏、图标库存、浮窗及实例操作。
- `Assets/Scripts/UI/RoundHoverTarget.cs`：鼠标和导航焦点转发，不拥有游戏状态。
- `Assets/Scripts/UI/RoundShopPresentation.cs`：共用详情格式与可替换的文本解析入口。
- `Assets/Tests/PlayMode/RoundCombatPlayModeTests.cs`：真实悬停/焦点、移入浮窗、精确回收、滚动与布局回归。
- 无需重挂场景组件；正式 UI 仍由 MainLevel 既有 RoundIntermissionUI 运行时生成。本次没有改动场景、数值表、交易服务或存档。

最新验证：

| 检查 | 结果 | 报告 |
| --- | --- | --- |
| 静态检查、Unity 编译 | 通过 | git diff --check；Unity 两平台实际编译 |
| 完整 EditMode | 171/171，零失败/跳过 | Logs/Automation/20260919-122607/EditMode.xml |
| 完整 PlayMode | 72/72，零失败/跳过 | Logs/Automation/20260919-122607/PlayMode.xml |
| 完整门禁 | passedQualityGate=true | Logs/Automation/20260919-122607/summary.json |
| 图形模式回合集成 | 9/9，零失败/跳过 | Logs/Session23UI/final.xml |
| 双分辨率截图复核 | 1280×720、1920×1080，无文字溢出或屏外按钮 | Logs/Session23UI/FinalScreenshots/ |

截图包括 combat、upgrades、shop、weapon-details、item-details、secondary-stats。商店截图使用测试构造的六把武器与六个道具，用于展示满槽效果，不代表真实账号库存。

本次 UI 由当前主 Agent 实现与静态自审；前文的独立审查结论针对 9 月 17 日首版。审美、真实鼠标/控制器手感仍待老大确认。实现继续保留在 QA 的 `codex/session-23-round-combat` 分支，未集成 main、未推送。


## 2026-09-19：试玩反馈第二轮（升级品质与商店放逐）

老大本轮要求覆盖前文相应旧规则：

- 删除局间页面右下区域的“返回主菜单”按钮。
- 普通属性升级逐卡独立抽品质，因此同页可以混合品质，也允许随机结果恰好相同。沿用此前 Luck 品质阈值，取消波数解锁限制；Luck=1 时品质 1/2/3/4 概率为 60%/25%/10%/5%。
- 每逢玩家升级至 10、20、30 等级，整页共用一次品质抽取，最低品质 3；重投仍保持此规则。连续升级以 currentLevel - PendingLevelUps + 1 还原当前待领取等级，避免玩家已经达到 12 级时漏掉队列中的第 10 级奖励。标题显示待领取等级。
- 每张属性卡保存自己的品质，显示与实际修改值均读取相同结果。属性页移除放逐按钮及对应控制器操作，属性候选不再按本局放逐集合筛除。
- 商店仅在有剩余放逐次数时，为道具报价并列显示“锁定”和“放逐 N”。武器报价不提供放逐。
- 放逐一次消耗一个本局次数，并立即移除该道具全部同 ID 报价（包括锁定报价）；不免费补货，之后刷新及跨波商店持续排除该 ID。既有道具、材料、账号封印与宝箱规则不受影响，新局清空排除。
- RunState.TryBanishUpgrade 原子更新次数与集合；商店复用现有交易事件延迟机制，在报价移除后发布变化，同步回调不能重新购买、放逐或进入下一波。

修改入口：RoundController、RoundUpgradeRollRules（新增纯规则类）、RunShopService、RunState、RoundIntermissionUI。无需场景手工搭建或 Inspector 新引用；新增规则类不需要挂载。更新 RoundCombatTests、RoundCombatPlayModeTests 以及受放逐语义影响的 AccountShopPlayModeTests。

验证对应本节代码版本，后续仅补充此文档并规范新增 meta 的行尾空白：

| 检查 | 结果 | 报告 |
| --- | --- | --- |
| Unity 编译与完整 EditMode | 176/176，零失败/跳过 | Logs/Automation/20260919-220952/EditMode.xml |
| 完整 PlayMode | 74/74，零失败/跳过 | Logs/Automation/20260919-220952/PlayMode.xml |
| 完整门禁 | passedQualityGate=true | Logs/Automation/20260919-220952/summary.json |
| 图形模式回合集成 | 11/11，零失败/跳过 | Logs/Session23UI/iteration2.xml |
| 720p / 1080p 截图 | 已复核普通升级、十级保底、商店放逐；无文本溢出和屏外按钮 | Logs/Session23UI/Iteration2Screenshots/ |

截图目录额外包含 milestone-upgrades 页面；显示的等级、道具、放逐次数由测试构造，不改变真实账号数据。当前主 Agent 完成静态自审，人工视觉及操作手感仍待老大体验。继续保留在 QA 的 codex/session-23-round-combat，不集成 main、不推送、不执行正式三份归档。


## 2026-09-19：试玩反馈第三轮（通关过渡与宝箱选择）

本节覆盖前文“通过后立即授予宝箱奖励并进入升级”的旧行为；本轮由老大明确确认计划后执行。

### 最终流程与边界

- 回合通过 → Settling（默认 1.5 秒）→ 全部属性升级 → Crates（逐个宝箱）→ Shop。第 20 波在升级与宝箱处理完后提交胜利结果，不再进入商店。
- 通过时立即停止战斗，并出现灰色半透明“通过！”蒙版。敌人、子弹及攻击对象按非死亡路径回池，不增加击杀或产生死亡掉落。
- 地面材料按价值累加储备，当前余额与经验不增加。地面金币按原金币统计链收取，不写入材料钱包。
- 地面回血道具保留约 0.45 秒飞向玩家后触发回血，并通过单次消费入口回池；与碰撞共享守卫。水晶球等非回血即时道具不在结算时触发，正式弱敌掉落表中的水晶球权重置 0，保留其资产与代码。
- 战斗拾取宝箱只增加待处理数量；波末剩余宝箱也加入同一队列，均不即时授予武器或道具。
- 升级期间显示持有道具与武器图标，悬停详情沿用已有逻辑。灰色蒙版后保留清空的竞技场；旧经验条、统计和装备 HUD 在局间隐藏，防止重叠。
- 右上升级箭头每获得一级增加一个，奖励处理时减少；宝箱显示图标和待处理数量。图标复用，数量较多时换行并适配保留区域。战斗中的原装备栏向下让位。
- 宝箱只抽道具，当前卡固定至处理完成；下一箱再按最新持有上限与本局禁用名单抽取。拿取授予或升级道具；回收按本波道具价格的 25% 向下取整；禁用消耗一次共享放逐次数并发相同回收材料，同时排除后续商店与宝箱。回收不增加经验或消耗储备。
- 宝箱禁用与商店放逐共用 RunState 的名单和次数，上限读取 BanishCapacity。上波锁定报价若被宝箱禁用，进入商店时也会被清除；账号永久 Seal 仍为独立机制。
- 合法宝箱道具池耗尽，每箱补偿 10 材料。回收/拿取/禁用均阻止同帧重复和同步事件重入。失败或退出会取消延迟结算，不继续回血或发奖。

### 修改与等价 Unity 配置

- RoundController：分阶段结算、自动收取、真实时间过渡、逐箱奖励与最终胜利出口。
- RunCrateReward（新增）：固定当前宝箱商品及回收报价；CrateRewardAction 定义三种选择。运行时状态不写回共享资产。
- PoolManager：清场可暂留回血拾取物，之后由统一拾取入口消费；默认完整清场行为保留。
- TreasureChestPickup、MapInstantEffectPickup、CoinPickup：新增受阶段保护的单次结算拾取入口。PlayerStats 增加 LevelGained 事件供提示栏读取。
- RunShopService：移除旧的宝箱即时授予入口；刷新候选前清除已被宝箱禁用的锁定道具。RunState 暴露当前放逐总容量。
- RoundIntermissionUI：通关蒙版、宝箱单卡三按钮、升级/宝箱 HUD、局间旧 HUD 显隐。RoundUpgradeIcon（新增）通过 UI 顶点绘制箭头，显式依赖 CanvasRenderer，无新位图纹理。
- PlayerLoadoutDisplayUI：回合模式装备栏下移，避免战斗 HUD 重叠。
- Standard20.asset：settlementSeconds=1.5；crateIcon 绑定现有 TreasureChest Sprite。WeakEnemyDropTable.asset：CrystalBallPickup 权重从 1 改为 0，CaptainPickup 权重保持 4。
- 无需手工挂载新场景组件；新视图和箭头由既有 RoundIntermissionUI 自动构建。未修改正式 Scene、Prefab 或账号存档结构。

### 验证与人工待验

| 检查 | 结果 | 报告 |
| --- | --- | --- |
| 完整 EditMode 与 Unity 编译 | 177/177，零失败/跳过 | Logs/Automation/20260919-233327/EditMode.xml |
| 完整 PlayMode | 77/77，零失败/跳过 | Logs/Automation/20260919-233327/PlayMode.xml |
| 完整门禁 | passedQualityGate=true | Logs/Automation/20260919-233327/summary.json |
| 图形回合集成 | 14/14；随后进一步修正升级箭头和旧统计栏显示，并由完整回归及下列布局测试覆盖 | Logs/Session23UI/settlement-verified.xml |
| 最终图形布局 | 2/2，720p / 1080p，无屏外按钮或文本溢出，升级箭头存在实际绘制组件 | Logs/Session23UI/settlement-layout-final.xml |
| 最终截图 | 已查看 passed、upgrades、crate-reward 等页面；道具与等级由测试构造 | Logs/Session23UI/SettlementFinalScreenshots/ |

完整报告后的运行时代码和数据未改；另强化了二十波测试中“最后一波带升级与宝箱”的顺序断言，定向验证 1/1 通过（零失败/跳过），报告：Logs/Session23UI/final-wave-rewards.xml。新增 meta 仅规范行尾空白。

本轮由当前主 Agent 实现和静态自审；通关停顿长度、吸收动画与实际鼠标/手柄体验仍待老大试玩。继续保留在 QA 的 codex/session-23-round-combat，未集成 main、未推送，不触发三份正式归档。
