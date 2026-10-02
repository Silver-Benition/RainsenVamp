# Session 26 代码、资产与配置变更

日期：2026-10-03。基线 `9317d5691e8c1630c6adb7ec4c057f21a4d61006`。本页描述归档时状态；近战原作复刻明确延期，详见[进度与待办](../progress/session-26.md)。

## 瞄准、挂点与基础攻击

| 文件 / 入口 | 变化 |
|---|---|
| `AimController` | NearestEnemy 默认模式，目标低频刷新与生命代次；SetMode/SetManualDirection 保留手动世界方向入口。 |
| `WeaponTargeting`（新增） | 局部物理查询、有效目标过滤、根身份与生命代次、最近候选选择。 |
| `WeaponBase` | 范围内目标门槛，近战玩家中心 AttackOrigin、目标落点快照、当前视觉长度和统一近战时序；工程伤害系数。 |
| `WeaponMountLayout`（新增） | 装备变化后重排等角挂点，默认半径 .72；香炉/月牙轮排除持武布局。 |
| `WeaponHeldView` / `WeaponVisualGeometry`（新增） | 持武和池化攻击共用素材与尺寸，跟随/交还姿态，处理斜向素材投影长度。当前持武不做左右镜像，明确待办。 |
| `MeleeWeapon` / `MeleeSwingHitbox` | 独立突刺/横挥/交替；固定落点、三段姿态、主动段物理补采、生命代次去重、暂停及取消、共享表现交接。最新 ConfigureTargeted 使用目标减攻击方向×半径，取消固定世界左向。 |
| `MeleeAttackTiming`（新增） | 风格适配的前摇/挥击/回收与冷却下限，共用于运行与详情；尚非原作公式。 |
| `ProjectileBase` | 飞行朝向与贴图校正，Tracking 弹射选择下一个有效未命中目标，失效重选与池化状态重置。 |
| `LobbedWeapon` / `LobbedProjectile` | 任意方向抛投，保留重力和自转，初始 renderer bounds 就绪前避免错误判离屏。 |
| `AuraWeapon` / `OrbitWeapon` | 玩家中心常驻；持武展示与位置计算排除。 |
| `WeaponDataSO` / `LevelUpManager` / `RunShopService` | 动作类型、图像校正、退池状态、扩展类别、工程系数与独立装备接入；正式获取过滤退池武器。 |

## 第二批武器、状态与构筑物

| 文件 | 职责与边界 |
|---|---|
| `ExpansionWeapon` / `ExpansionEffect`（新增） | 十类扩展武器及射束、地雷、返回弹体、区域、余波、分裂、残影等池化攻击；来源验证、数量上限和取消清理。 |
| `EnemyCombatStatus`（新增）/ `EnemyBase` | 灼烧、减速、延迟伤害和生命代次联动；强弱减速到期独立，池化清理，长帧补齐应结算跳数。 |
| `StructureItemMechanicSO`（新增） | 道具机制配置与运行注册，区分建筑类型和专属攻速道具。 |
| `EngineeringLoadout`（新增） | 建筑副本数量、固定部署点、范围内自主行为接入，回合代次/来源移除清理，专属攻速聚合。 |
| `EngineeringStructure`（新增） | 哨塔直射、喷塔扇形减速、迫击炮快照落点、雷网未命中链；工程公式与玩家武器公式分离。 |
| `BrotatoStatRules` | 正式开放工程学，保持原有主要属性迁移与工程伤害边界。 |

`Assets/Data/EngineeringExpansion/definitions.json` 与 Weapons/Items/Upgrades 资产保存新增十把四档武器、十件固定品级道具。四件建筑 I–IV 分别对应哨塔/喷塔/迫击炮/雷网，副本上限 3/2/2/1，总计 8 座。烙火长鞭 `17_brand_whip` 数据与导入器均标记 retired，当前不出现在正式池和 F8。

## UI、导入和场景

- `PlayerAttributeDebugPanel`（F9）改用当前正式属性；工程学可调整。`PlayerStatBoardUI`、`PlayerStatDescriptions`、`StatIconPresentation`、`PauseInventoryView` 和 `RoundShopPresentation` 适配第 16 项属性及混合/建筑详情。
- `RoundIntermissionUI` 缓存商品背景，刷新时按品质恢复对应色，复用卡片不会遗留颜色；调整长说明区域。`WeaponDebugPanel` 过滤退池武器。
- `EngineeringExpansionSetup.Build` 新增可重复导入菜单 `RainsenVampSur/Content/Import Engineering Expansion`，复用 GUID，登记正式目录、商店与 MainLevel 升级池。
- `ContentExpansionSetup`、`RoundCombatSetup`、`WeaponConfigAssetImporter` 同步新字段与退池状态；`GameContentCatalog.asset`、`Rounds/ShopCatalog.asset`、`MainLevel.unity` 登记正式内容，工程属性四档 +2/+3/+4/+5。
- `Aura.asset`、`Axe.asset`、`Knife.asset` 从正式池退休，旧对象及历史兼容保留；第一批武器定义、长枪/飞梭描述与相关 Prefab 同步修正。BlueWarrior 的瞄准序列化配置同步自动模式。
- `MainLevel` 恢复 Cinemachine OrthographicSize=5 与 Pixel Perfect 480×270、32 PPU；实际图形相机取整后的视野以图形报告为准。没有修改全局碰撞矩阵、账号商城或存档格式。

### 等价 Unity 配置与操作

1. 第二批 22 张透明素材导入 `Assets/Art/EngineeringExpansion`，Sprite/Single，PPU=256，Point、无 Mipmap、无压缩、最大纹理 256。原件为高分辨率像素风生成图，不是原生 48×48；不得把导入限制称为最终像素美术验收。
2. 在 `Assets/Data/EngineeringExpansion` 建立武器、道具、升级与机制 SO，保留稳定 ID、本地化键、四档数值/固定道具品级；各对象绑定对应图标、池化攻击/建筑 Prefab。
3. `Assets/Prefab/EngineeringExpansion` 中效果体使用 SpriteRenderer、LineRenderer 和 ExpansionEffect，表现不作为实体阻挡；近战模板沿用 Rigidbody2D/CapsuleCollider2D 与子视觉，正式命中由主动段查询负责。建筑无身体阻挡与建筑生命系统。
4. 目录/商店/场景引用由导入器保存；获取装备后运行时创建挂点展示组件，建筑道具运行时注册 EngineeringLoadout。无需额外手工向场景堆放常驻武器或炮台。
5. 打开 MainLevel，F7 获取道具，F8 获取武器，F9 调整当前属性。手动方向仍是接口预留，不声称已实现鼠标/手柄操作产品流程。

## 测试与证据

新增 `WeaponCombatTests`、`WeaponRevisionTests`、`WeaponCombatPlayModeTests`、`EngineeringExpansionTests`、`EngineeringExpansionPlayModeTests`；同步内容、重载、回合、道具 Debug、主菜单和镜头相关测试。图形夹具隔离真实输入，40 组方向测试重置每组回合计时，避免进入局间误判攻击。

最后代码检查覆盖五类近战八方向轴线、握柄/刀尖、碰撞与实际命中，尚不覆盖左右镜像与原作轨迹一致性。现有结果范围与首轮失败原因完整记录在[进度归档](../progress/session-26.md#验证与复用依据)。本次归档仅修改 Markdown，不启动额外 Unity 测试。

### 被测文件与报告指纹

以下 SHA256 用于对应 QA 证据；Logs 不进 Git。完整 250 个变更资产指纹保留在 QA `Logs/Session26/delivery-evidence.json`。

- `Assets/Scripts/Weapon/MeleeSwingHitbox.cs`：`b0760c4249fc371c20ad0972ab8270fa060d9fddcc019cffc5a7222d8070bde1`
- `Assets/Tests/PlayMode/WeaponCombatPlayModeTests.cs`：`03bcff99ae3644635a9ca6abd5d38b974d8e95fc855846ff961c1d73a359de8c`
- `Logs/Automation/20261002-234502/EditMode.xml`：`c142a6993937bd34a66ab1bb14a16efccd2a67c44bc4aebf8e2cc4355800966b`
- `Logs/Automation/20261002-234502/PlayMode.xml`：`afa0140279e11289e6fba292c480a42bb0266bbceef018cac569b7bc32d2e91a`
- `Logs/Session26/MeleeDirectionFix/graphics.xml`：`625d230db93b7b7a30cde6e6e418328e3cca3bdd538b4252e1537e462fa74309`

归档静态检查：暂存范围共 255 个文件，代码与 Markdown 的严格空白检查通过。新增 Unity 序列化文件的 435 处行尾空格均为 `.meta/.asset/.prefab/.mat` 的空字段（例如 `key: `），逐项确认后仅对此类文件豁免行尾空格检查，保留被测资产原始内容；未发现其他空白错误。
