/// <summary>暂停与商店共用的属性说明，描述项目实际消费规则并通过稳定键预留本地化。</summary>
public static class PlayerStatDescriptions
{
    /// <summary>取得属性用途、负值效果与必要上限；未开放属性不给予虚构的游戏效果。</summary>
    public static string Get(PlayerStatType stat)
    {
        string value;
        switch (stat)
        {
            case PlayerStatType.MaxHealth: value = "决定生命上限，最低为 1。每次升级增加 1 点上限，并恢复 1 点生命。新回合默认回满，特殊条件可指定开局生命。"; break;
            case PlayerStatType.HpRegeneration: value = "战斗中定期恢复 1 点生命。正值越高，恢复越快；零或负值不恢复。每次恢复间隔为 11.25 /（属性 + 1.25）秒。"; break;
            case PlayerStatType.LifeSteal: value = "有效命中时有概率恢复 1 点生命。与该武器自带的生命窃取相加，负值会抵消武器自带概率。最终概率为 0% 至 100%；所有武器共用 0.1 秒恢复间隔。"; break;
            case PlayerStatType.DamagePercent: value = "按百分比影响武器最终伤害。先计算基础伤害和近战、远程、元素附加伤害，再应用伤害加成；负值降低伤害，最终命中伤害最低为 1。"; break;
            case PlayerStatType.MeleeDamage: value = "按武器标注的近战系数增加伤害。100% 系数时，每点增加 1 点基础计算伤害；负值会抵消伤害。只影响带近战系数的武器。"; break;
            case PlayerStatType.RangedDamage: value = "按武器标注的远程系数增加伤害。100% 系数时，每点增加 1 点基础计算伤害；负值会抵消伤害。只影响带远程系数的武器。"; break;
            case PlayerStatType.ElementalDamage: value = "按武器标注的元素系数增加伤害。100% 系数时，每点增加 1 点基础计算伤害；负值会抵消伤害。只影响带元素系数的武器。"; break;
            case PlayerStatType.AttackSpeed: value = "与武器自带攻速相加。正值缩短攻击间隔，负值延长间隔；也影响光环伤害间隔和环绕转速。近战攻击间隔还会随实际范围变化。"; break;
            case PlayerStatType.CritChance: value = "与武器自带暴击率相加，负值会抵消武器概率，最终概率为 0% 至 100%。暴击伤害倍率由每把武器自身决定。"; break;
            case PlayerStatType.Range: value = "与武器自带范围相加，100 点对应 1 世界单位。近战只获得一半范围增量；负值缩短范围。光环和环绕也受影响，最终范围最低为 25 点。"; break;
            case PlayerStatType.Armor: value = "正护甲降低受到的伤害，负护甲增加受到的伤害。例如 15 护甲减伤 50%，-15 护甲使伤害增加 50%。有效受击最低扣除 1 点生命。"; break;
            case PlayerStatType.Dodge: value = "有概率完全避开一次攻击。有效闪避率最高为 60%；零或负值不提供闪避。成功闪避不扣血，也不产生受伤数字。"; break;
            case PlayerStatType.SpeedPercent: value = "按百分比改变角色基础移动速度。正值加速，负值减速；不会变为负速度。"; break;
            case PlayerStatType.LuckPoints: value = "提高升级与商店高品质出现机会，以及掉落概率。负值降低这些机会；高品质仍受波次或等级门槛限制，不保证每次获得高品质。"; break;
            case PlayerStatType.Harvesting: value = "成功完成一波时获得等量材料和经验。正收获每波增长 5% 并向上取整；负收获扣除材料与当前经验，但不会掉级。"; break;
            case PlayerStatType.ExperienceGain: value = "按百分比影响获得的经验。正值增加经验，负值减少经验，最终获得量最低为零。"; break;
            case PlayerStatType.PickupRange: value = "按百分比改变自动拾取触发范围。正值扩大、负值缩小，最低为零；不改变必须近身触碰的特殊拾取物规则。"; break;
            case PlayerStatType.Revival: value = "显示本局剩余复活次数。死亡后确认复活时消耗一次；回合开始回血不会代替复活。"; break;
            case PlayerStatType.Reroll: value = "显示本局剩余免费重投次数，用于重新抽取升级候选。免费次数耗尽后按界面报价消耗材料；商店刷新另行计价。"; break;
            case PlayerStatType.Skip: value = "显示本局剩余跳过次数。消耗一次可以放弃当前升级选择。"; break;
            case PlayerStatType.Banish: value = "显示本局剩余放逐次数，用于禁用本局后续的对应候选。仅影响当前局，与局外封印设置分别管理。"; break;
            default: value = "旧体系保留属性；正式新体系不提供该属性的成长入口。"; break;
        }
        return RoundShopPresentation.Text("stat.description." + stat, value);
    }
}
