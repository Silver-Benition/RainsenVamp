# Session 24 代码与资产归档：新属性规则及玩家反馈

- 日期：2026-09-23；实现范围：`36b5ec8..d46a4b7`，117 个文件，新增与修改共 2975 行插入、460 行删除。
- 最终规则、验收和证据见[进度归档](../progress/session-24.md)；本文件记录类、方法、资源及等价 Unity 配置。

## 新增模块

| 类 / 路径 | 关键职责与入口 |
| --- | --- |
| `Assets/Scripts/Player/BrotatoStatRules.cs` | IsAvailable/LegacyNeutral 管可用性和旧值隔离；Probability 合并正负概率；Damage、ArmorMultiplier、RegenerationInterval、AttackIntervalMultiplier、WeaponRange 统一公式；RollTier、GuaranteedUpgradeTier、DropChance 管品质与幸运；EngineeringPower 预留工程学。 |
| `Assets/Scripts/Weapon/WeaponHitSnapshot.cs` | 值类型构造时保存该武器独立暴击率/倍率、吸血和拥有者；Apply 每次命中独立暴击，目标接受有效伤害后才尝试吸血，避免来源间互相污染。 |
| `Assets/Scripts/UI/PauseInventoryView.cs` | Refresh/SetVisible/Layout/Dispose 管暂停道具滚动栏与只读装备详情，复用既有武器槽和持有实例。 |
| `Assets/Scripts/UI/StatIconPresentation.cs` | Icons 按需加载 TMP 图集；Bind 绑定文本；Token 输出属性图标，缺资源时回退为本地化名称。 |
| `Assets/Scripts/UI/PlayerHealthHudUI.cs` | Initialize 创建经验条下的数字生命条；Refresh 响应生命事件；LayoutCounters 调整旁侧统计；禁用解除订阅，销毁恢复布局。 |
| `Assets/Scripts/UI/PlayerStatDescriptions.cs` | Get 提供 15 核心、2 次要、4 资源的共用说明，稳定键进入现有本地化入口。 |
| `Assets/Scripts/UI/StatTooltipView.cs` | Show/HideFrom/Hide/Dispose 管说明窗；按行定位、高度随文本、约束页面边界，不拦截射线。 |

## 修改的逻辑及数据契约

- `PlayerStatType` 保留原 0—20 编号，新增 21—36；`CharacterDataSO` 显式选择新规则；`PlayerStats` 聚合有符号来源、过滤旧属性、更新经验曲线与升级生命；旧角色及兼容测试路径保留。
- `PlayerHealth` 接通整数护甲、闪避、间隔再生与 TryLifeSteal 的全角色 0.1 秒节流。HandleLevelGained 仅针对新体系升级恢复 1 点；普通 ApplyMaxHealth 不治疗。
- `SetNextRoundHealth(float)` 接受有限正数并覆盖未消费请求；`ClearNextRoundHealth()` 撤销；`PrepareRound()` 对存活玩家默认回满或采用一次性绝对值，钳制到 1..MaxHealth，通知前消费并阻止重入。没有对应新道具或存档字段。
- `WeaponDataSO/WeaponLevelData` 增加近战/远程/元素系数、自带伤害%、暴击率/倍率、生命窃取、攻速、范围加值与射程。`WeaponBase` 及直飞、投掷、近战、光环、环绕链读取共用规则和命中快照；池化攻击体回收时清理来源及效果状态。
- `RoundController` 接入升级品质和波末收获；钱包事务延迟通知，材料、经验、收获增长一起提交，失败和重复结算不再发奖。`RunShopService` 接通合法池与幸运品质，`EnemyBase` 调整幸运恢复物/宝箱掉落。
- `AbilityDataSO`、`AbilityManager`、`LowHealthBuffMechanicSO`、`RetaliationPulseMechanicSO` 接入新属性与新伤害规则；`RunShopCatalogSO` 配置四档成长。
- `AccountUpgradeCatalogSO`、`AccountUpgradeResolver`、`AccountProgressService` 过滤旧项目的购买、启用和开局快照；保留原稳定 ID、上限、实付退款及保存事务。停用项目只在有历史购买时提供退款入口，不重新估价。

## 表现层

- `RoundShopPresentation.WeaponDetails/ItemDetails` 共用战斗最终数值及逐行属性格式；非零缩放保留，最终概率在武器和角色负值合并后显示。不存在的可选效果隐藏，自带吸血被抵消至零时仍显示 0%。
- `RoundIntermissionUI` 为商品和持有详情绑定图集，各属性行增加悬停说明；切页、刷新、禁用和销毁时关闭/释放说明。
- `PlayerLoadoutDisplayUI` 接入暂停背包的事件刷新和生命周期；`PlayerStatBoardUI.SelectPage/RefreshModern` 创建两页独立属性行，特殊资源读取本局剩余数量，保留旧体系展示分支。
- `PlayerStatPresentation`、`CharacterSelectionUI`、`AccountShopEffectPresentation`、`AccountShopUI` 更新属性名称、单位、分组及停用退款提示。名称与说明继续走本地化解析入口。
- `ExpBarUI` 在初始化时关联新生命 HUD；血条为经验外框的子节点，宽度 1/6，使用独立内轨道避免低血量负宽度；当前/最大生命随事件更新。
- `PlayerDamageFeedback.HandleDamaged` 在精灵世界边界顶部请求玩家受伤飘字；`DamagePopupManager.ShowPlayerDamage` 复用现有对象池；`DamagePopup.InitializePlayerDamage` 重置表现后显示红色负数和实际扣血量，敌人再次使用时正常重置。

## 关键数值与资产

### 2. 已接通的计算规则

| 内容 | 当前实现 |
| --- | --- |
| 武器伤害 | 基础伤害 + 近战/远程/元素点数×该武器独立系数，再乘合并后的伤害百分比，向下取整，最低1 |
| 暴击 | 武器暴击率 + 角色有符号暴击率，合并后限制0—100%；命中逐次判定，采用武器独立暴击倍率 |
| 生命窃取 | 武器吸血 + 角色有符号吸血，合并后限制0—100%；有效命中后概率恢复1点，全角色共享0.1秒间隔 |
| 护甲 | 正护甲承伤倍率15/(15+A)；负护甲为(15−2A)/(15−A)。伤害采用整数取整，最低1 |
| 闪避 | 保留原始正负点数，受伤时按0—60%判定；成功闪避不触发实际受伤事件 |
| 生命再生 | 正值R每11.25/(R+1.25)秒恢复1点；R≤0禁用，暂停/局间不计时 |
| 速度 | 基础世界移动速度×max(0,(100+速度点数)/100) |
| 范围 | 100点=1世界单位，挥击近战只接受半量增量，最低0.25世界单位；原有弹体尺寸不由范围点数放大 |
| 经验 | 现有等级显示从1开始，下级需求为(level+3)²；每升1级增加1点最大生命 |
| 收获 | 成功波结束只结算一次；正值增加材料与经验，之后向上取整增长5%；负值只扣已有材料和本级经验，不降级 |
| 幸运 | 商店/升级按波次或等级抽品质，负幸运可降低概率；恢复物与宝箱线性修正掉率，宝箱随当波已掉数量递减 |

收获使用局内钱包事务延迟通知，观察者只能看到材料、经验和属性增长全部完成的状态。失败或重复结算不重新发奖。

### 武器独立属性与负值

这是本次关键契约：角色属性保持原始负值，各把武器独立合并，最后才限制有效概率。

例如医疗武器自带50%生命窃取：角色−20%时该武器30%，普通无自带吸血武器0%；角色−60%时医疗武器也为0%。武器属性不写回角色，也不污染其它武器。

`WeaponLevelData` 新增三类伤害系数、武器伤害%、暴击率/倍率、生命窃取、攻击速度、范围加值、射程。`WeaponHitSnapshot` 在发射/刷新时保存命中参数，直飞、光环、环绕、投掷、近战五条命中链均已接入；对象回池清除概率和角色引用。

目前5把既有武器的自带吸血仍为0；本次建立该配置能力，没有增加新的医疗武器内容。

### 3. 四档升级

| 属性 | I | II | III | IV |
| --- | ---: | ---: | ---: | ---: |
| 最大生命 | 3 | 6 | 9 | 12 |
| 生命再生 | 2 | 3 | 4 | 5 |
| 生命窃取 | 1 | 2 | 3 | 4 |
| 伤害% | 5 | 8 | 12 | 16 |
| 近战伤害 | 2 | 4 | 6 | 8 |
| 远程/元素伤害 | 1 | 2 | 3 | 4 |
| 攻击速度% | 5 | 10 | 15 | 20 |
| 暴击率 | 3 | 5 | 7 | 9 |
| 范围 | 15 | 30 | 45 | 60 |
| 护甲 | 1 | 2 | 3 | 4 |
| 闪避%/速度% | 3 | 6 | 9 | 12 |
| 幸运 | 5 | 10 | 15 | 20 |
| 收获 | 5 | 8 | 10 | 12 |

显示与领取共同读取逐档表。5级固定II档，10/15/20级固定III档，25级及之后每5级固定IV档，其余逐张随机。

### 4. 已迁移的正式内容

- 默认角色最大生命100→10，基础移速仍为3，保留额外重投。
- 蓝衣战士最大生命140→14，护甲2，生命再生2，伤害+25%，速度−13.333333%（实际移速仍约2.6），保留额外复活。
- 五把武器保留当前四品质基础配置，新增系数：火球元素100%、飞刀远程100%、飞斧近战100%、光环元素50%、雨伞近战100%。伤害属性与运动形态分开配置。
- 力量训练改为伤害点数；疾跑改为速度点数；冷却优化改名攻速训练；磁力核心改为拾取范围点数。低血量增益和受伤反击接入新伤害体系。
- 普通接触伤害10→2，远程敌人接触5→1、弹体12→2，首领接触18→4、两阶段弹体10/12→2；回血拾取物45→3。这是配合小生命量级的初始尺度调整，尚不能视为20波最终平衡。

## 等价 Unity 配置与编辑入口

本 Session 未修改正式 Scene、Prefab、Layer、Collider 或全局物理设置，也无需手工新增场景物体。现有脚本在运行时建立 UI，沿用 Canvas、EventSystem、中文 TMP 字体和 RoundHoverTarget；暂停按键仍为 ESC。

1. 在 `Assets/Data/Characters/DefaultCharacter.asset` 与 `BlueWarrior.asset` 选择新体系并填写对应生命、基础移动和初始修正；不改变原玩家组件挂载关系。
2. 在 `Assets/Data/FireBall.asset`、`Knife.asset`、`Axe.asset`、`Aura.asset`、`Umbrella.asset` 的四档 roundTierConfigs 填写基础参数、缩放、自带概率等。生命窃取 50% 填 50，100% 伤害系数填 1；目前五把既有武器自带吸血仍为 0，尚未新增医疗武器。
3. 在 `Assets/Data/Rounds/ShopCatalog.asset` 调整四档属性奖励；现有道具资产转为新属性类型及点数。回合时长、地图和基本流程延续 Session 23。
4. 在 `Assets/Data/AccountUpgrades/` 新增近战伤害、远程伤害、元素伤害、暴击、生命窃取、范围、闪避、收获 8 个定义，加入 AccountUpgradeCatalog，保留原 ID 和实付记录映射。存档权限与保存失败处理仍由账号服务负责。
5. 新图集 `Assets/Art/UI/StatDamageAtlas.png` 为 2172×724 RGBA，三格各 724×724；Point 过滤、无 Mipmap、无压缩并保留尺寸。`Assets/Resources/UI/StatDamageIcons.asset` 保存三个 TMP 字形及材质引用，运行时 Resources.Load 读取。图标使用 imagegen 生成，原件和副本经哈希核验；属于临时美术，不是声称源图已制作为严格 32×32。
6. HUD 与暂停视图在既有 Canvas 下动态创建：生命条接在 ExpBarFrame 下；暂停左下道具由 ScrollRect/GridLayoutGroup 承载；右侧属性逐行命中，说明窗复用。没有新增需手动拖拽的 Inspector 依赖。
7. 玩家受伤仍使用已有 DamagePopup Prefab 和 PoolManager；按 PlayerHealth.Damaged 的实际扣血事件请求红字，不依赖动画判断伤害。

## 测试与归档差异

新增 `BrotatoStatMigrationTests` 与 `ItemDetailPresentationTests`，并调整账号权益、旧兼容、场景重载、选角/主菜单、回合流程、物品展示及结局测试。覆盖武器正负值、命中快照/回池、吸血节流、收获事务、历史退款、逐级治疗、一次性开局生命、悬停、滚动和 720p/1080p 布局。

最终完整报告：EditMode 197/197、PlayMode 88/88；最终图形专项 3/3。路径与使用边界见进度归档。d46a4b7 提交与最终源码清单逐项一致；归档阶段只增加三份 DevLog。Unity 空字段末尾空格保持原样，未为格式改变被测资源。
