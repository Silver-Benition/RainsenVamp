# 20 件道具与 10 把武器 · 本轮交付

游戏实现位于 `C:/Unity_Project/RainsenVampSur-QA`，分支 `codex/session-25-items-weapons`。

## 查看与试玩

打开 `review.html` 可以离线查看十把武器的图像、效果与四档品质数值，并切换到 48px 图标预览。完整参数在 `presets.json`，每张图的原始提示词与生成模式在 `generation-record.json`。图像由内置 image_gen 生成，透明原图保存在 `icons/`。

在 Unity Hub 中打开上述 QA 工程，进入 `Assets/Scenes/MainLevel.unity`，运行后通过局间商店购买新增内容。新武器沿用六槽、同名同品质合并与回收规则；道具数量不限，单件持有上限按审阅结果执行。

## 已实现规则

- 20 件道具采用 `ArtReview/items-20-v2/proposal.json` 中的审阅结果；普通属性的正面与负面数值均随数量累计。
- 商店和宝箱展示单件收益与持有上限，背包详情展示累计收益。
- 应急糖块：每回合一次，受伤后存活且生命不高于最大生命的 25% 时，恢复最大生命的 15%。遵循现有浮点生命值，不额外取整，不能抵消致死伤害。
- 余烬沙漏：本回合累计战斗 15 秒后获得伤害 +15%、速度 +5%；暂停不计时，回合结束清除，下回合重新计时。
- 新武器包括铜针细剑、锯齿砍刀、潮纹长枪、荆棘弩矢、铆钉枪、棱晶飞梭、采血针、余火香炉、月牙轮、陨铁锤。每把均有品质 1～4 的完整配置和独立攻击预设。
- 近战、直射、光环、环绕和抛物线攻击复用既有对象池与伤害系统。弩与枪使用新图标，实际发射小型弹体；其余武器使用对应的新攻击图像。

## 游戏资产位置

- 道具与武器配置：`Assets/Data/ContentExpansion/`
- Unity 图像：`Assets/Art/ContentExpansion/`
- 武器攻击预设：`Assets/Prefab/Weapon/ContentExpansion/`
- 可重复导入工具：`Assets/Editor/ContentExpansionSetup.cs`

本轮作为 Session 25 阶段归档；最终美术、手感与数值平衡仍需试玩验收。最终状态与下次待办见 [Session 25](../../DevLog/progress/session-25.md)。

## 验证记录

- 完整回归：EditMode **202/202**、PlayMode **90/90**，见 [完整报告](C:/Unity_Project/RainsenVampSur-QA/Logs/Automation/20260925-095508/summary.json)。
- 图形核对发现陨铁锤的隐藏根渲染器会影响离屏回收，已改为保留可见根渲染器，并反向校正碰撞半径以维持世界判定尺寸。其余正式武器运行逻辑未改动。
- 上述修正后复测：EditMode **202/202**，见 [最终资产与逻辑报告](C:/Unity_Project/RainsenVampSur-QA/Logs/Automation/20260925-100357/summary.json)；图形专项 **4/4**，见 [最终图形报告](C:/Unity_Project/RainsenVampSur-QA/Logs/Session25/GraphicsVerified/results.xml)。
- 图形专项涵盖逐件实际购买、武器攻击生成与持续飞行、回收、道具背包末行滚动可见、真实销毁后清除沙漏增益，以及 720p/1080p 布局。
- 实际截图：[道具商店](C:/Unity_Project/RainsenVampSur-QA/Logs/Session25/GraphicsVerified/Screenshots/items-shop.png)、[背包末行](C:/Unity_Project/RainsenVampSur-QA/Logs/Session25/GraphicsVerified/Screenshots/items-inventory-last-row.png)、[陨铁锤飞行](C:/Unity_Project/RainsenVampSur-QA/Logs/Session25/GraphicsVerified/Screenshots/10_meteor_hammer.png)。
- 预览页：10 张图片全部加载，4 档切换、48px 显示和 390px 手机布局通过；没有页面脚本错误。
- 弩与枪当前复用着色的小型弹体表现，香炉沿用圆形范围表现；专属攻击动画与最终特效尚未制作。

自动化和截图核对已完成，不替代实际操作手感、音效、平衡或高密度性能验收。

## 后续核对发现的待修项

2026-09-25 核对武器瞄准与 F7 时发现：棱晶飞梭的 `Tracking` 弹射配置对应的运行时分支目前仅消耗次数，没有实际转向。预设中的“追踪弹射”是设计目标，当前版本尚未实现；上一轮的生成与回收测试未覆盖这个命中行为。此项需另行修复，不能计作已完成的武器效果。

## 归档时最终验证

加入 F7 道具面板修复后，最终完整回归为 EditMode **203/203**、PlayMode **92/92**，均无失败或跳过。报告位于 QA 的 `Logs/Automation/20260925-110759/summary.json`；F7 实际窗口专项 **2/2**，见 `Logs/Session25/F7-Windowed/results.xml`。此前报告及 implementation-validation.json 是各阶段快照，不能作为最后 F7 改动的完整证据。武器挂点、自动瞄准、近战动作、Tracking 转向及道具 Debug 进一步验收留到下一会话。
