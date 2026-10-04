using UnityEngine;

/// <summary>暂停与商店共用的当前属性效果；使用可翻译模板，避免展示计算过程。</summary>
public static class PlayerStatDescriptions
{
    /// <summary>读取悬停时的最终属性，负值与封顶使用实际战斗规则，不改变玩家状态。</summary>
    public static string Get(PlayerStatType stat, PlayerStats player = null)
    {
        float value = player != null ? player.GetFinalStat(stat) : 0;
        string number = value.ToString("0.#"), signed = value.ToString("+0.#;-0.#;0");
        string key = "stat.current." + stat, template;
        switch (stat)
        {
            case PlayerStatType.MaxHealth: template = "你的生命上限为 {0}。"; break;
            case PlayerStatType.Armor:
                float difference = (1 - BrotatoStatRules.ArmorMultiplier(value)) * 100;
                number = Mathf.Abs(difference).ToString("0.#");
                bool reduced = difference >= 0; key += reduced ? ".reduced" : ".increased";
                template = reduced ? "你受到的伤害减少 {0}%。" : "你受到的伤害增加 {0}%。"; break;
            case PlayerStatType.HpRegeneration:
                float interval = BrotatoStatRules.RegenerationInterval(value);
                if (float.IsPositiveInfinity(interval)) { key += ".inactive"; template = "当前无法自动恢复生命。"; }
                else { number = interval.ToString("0.##"); template = "每 {0} 秒恢复 1 点生命。"; }
                break;
            case PlayerStatType.Dodge: number = Mathf.Clamp(value, 0, 60).ToString("0.#"); template = "有 {0}% 概率避开攻击。"; break;
            case PlayerStatType.LifeSteal: number = signed; template = "武器生命窃取率 {0} 个百分点。"; break;
            case PlayerStatType.CritChance: number = signed; template = "武器暴击率 {0} 个百分点。"; break;
            case PlayerStatType.DamagePercent: number = signed; template = "武器伤害加成 {0}%。"; break;
            case PlayerStatType.MeleeDamage: number = signed; template = "近战伤害 {0}，按武器系数生效。"; break;
            case PlayerStatType.RangedDamage: number = signed; template = "远程伤害 {0}，按武器系数生效。"; break;
            case PlayerStatType.ElementalDamage: number = signed; template = "元素伤害 {0}，按武器系数生效。"; break;
            case PlayerStatType.Engineering: number = signed; template = "工程伤害 {0}，按工程系数生效。"; break;
            case PlayerStatType.AttackSpeed: number = signed; template = "武器攻击速度加成 {0}%。"; break;
            case PlayerStatType.Range: number = signed; template = "武器范围 {0}，近战获得一半增量。"; break;
            case PlayerStatType.SpeedPercent: number = signed; template = "移动速度加成 {0}%。"; break;
            case PlayerStatType.LuckPoints: template = "幸运 {0}，影响高品质与掉落机会。"; break;
            case PlayerStatType.Harvesting: number = signed; template = "每波结束时材料和经验各 {0}。"; break;
            case PlayerStatType.ExperienceGain: number = signed; template = "经验获取加成 {0}%。"; break;
            case PlayerStatType.PickupRange: number = signed; template = "自动拾取范围加成 {0}%。"; break;
            case PlayerStatType.Revival: number = Resource(stat, value); template = "本局剩余 {0} 次复活。"; break;
            case PlayerStatType.Reroll: number = Resource(stat, value); template = "本局剩余 {0} 次免费升级重投。"; break;
            case PlayerStatType.Skip: number = Resource(stat, value); template = "本局剩余 {0} 次跳过升级。"; break;
            case PlayerStatType.Banish: number = Resource(stat, value); template = "本局剩余 {0} 次放逐。"; break;
            default: template = "当前数值为 {0}。"; break;
        }
        return string.Format(RoundShopPresentation.Text(key, template), number);
    }

    /// <summary>资源只读当前剩余次数；未建立局内状态时以属性容量回退。</summary>
    private static string Resource(PlayerStatType stat, float fallback)
    {
        RunState state = RunState.Instance;
        if (state == null) return Mathf.Max(0, Mathf.FloorToInt(fallback)).ToString();
        int count = stat == PlayerStatType.Revival ? state.RemainingRevivals
            : stat == PlayerStatType.Reroll ? state.RemainingRerolls
            : stat == PlayerStatType.Skip ? state.RemainingSkips : state.RemainingBanishes;
        return count.ToString();
    }
}
