using UnityEngine;

/// <summary>新模式属性策略与纯数值公式；旧属性编号及计算路径保留，正式角色显式选择新规则。</summary>
public static class BrotatoStatRules
{
    public const int StatCount = 37;
    public const float RangeUnits = .01f;
    public static readonly PlayerStatType[] Primary = {
        PlayerStatType.MaxHealth, PlayerStatType.HpRegeneration, PlayerStatType.LifeSteal,
        PlayerStatType.DamagePercent, PlayerStatType.MeleeDamage, PlayerStatType.RangedDamage,
        PlayerStatType.ElementalDamage, PlayerStatType.AttackSpeed, PlayerStatType.CritChance,
        PlayerStatType.Range, PlayerStatType.Armor, PlayerStatType.Dodge, PlayerStatType.SpeedPercent,
        PlayerStatType.LuckPoints, PlayerStatType.Harvesting };
    public static readonly PlayerStatType[] Secondary = { PlayerStatType.ExperienceGain, PlayerStatType.PickupRange };
    public static readonly PlayerStatType[] Resources = { PlayerStatType.Revival, PlayerStatType.Reroll, PlayerStatType.Skip, PlayerStatType.Banish };

    /// <summary>判断玩家正式内容可以修改的属性；工程学仅保留接口，不能获取。</summary>
    public static bool IsAvailable(PlayerStatType stat)
    {
        return stat == PlayerStatType.MaxHealth || stat == PlayerStatType.Armor ||
            (stat >= PlayerStatType.Revival && stat <= PlayerStatType.Banish) ||
            ((int)stat >= 21 && (int)stat < StatCount && stat != PlayerStatType.Engineering);
    }

    /// <summary>旧系统消费端使用的中性值；基础移动/拾取尺寸由角色配置提供，不能通过旧修改器提升。</summary>
    public static float LegacyNeutral(PlayerStatType stat, float baseline)
    {
        switch (stat)
        {
            case PlayerStatType.MoveSpeed: case PlayerStatType.Magnet: return Mathf.Max(0, baseline);
            case PlayerStatType.Might: case PlayerStatType.Area: case PlayerStatType.ProjectileSpeed:
            case PlayerStatType.Duration: case PlayerStatType.Cooldown: case PlayerStatType.Growth:
            case PlayerStatType.Greed: case PlayerStatType.Curse: case PlayerStatType.Luck: return 1;
            default: return 0;
        }
    }

    /// <summary>先合并武器自带概率和角色有符号点数，最后钳制；不得提前清除角色负值。</summary>
    public static float Probability(float weaponPoints, float playerPoints)
    { return Mathf.Clamp01((weaponPoints + playerPoints) * .01f); }

    /// <summary>正负护甲分别减伤和增伤，负护甲不使用正护甲公式避免奇点。</summary>
    public static float ArmorMultiplier(float armor)
    { return armor >= 0 ? 15f / (15f + armor) : (15f - 2f * armor) / (15f - armor); }

    /// <summary>生命再生每次恢复一点的间隔；零和负值禁用再生但保留原始属性。</summary>
    public static float RegenerationInterval(float points)
    { return points > 0 ? 11.25f / (points + 1.25f) : float.PositiveInfinity; }

    /// <summary>项目连续时间攻速适配：正值缩短间隔，负值延长间隔，最低间隔在武器消费端限定。</summary>
    public static float AttackIntervalMultiplier(float points)
    { return points >= 0 ? 1f / (1f + points * .01f) : 1f - points * .01f; }

    /// <summary>按波次或等级计算累计品质阈值；负幸运可降至零，高档从同一随机样本优先判定。</summary>
    public static int RollTier(int progress, float luckPoints, float sample)
    {
        float factor = Mathf.Max(0, (100 + luckPoints) * .01f);
        if (sample < Mathf.Min(.08f, Mathf.Max(0, progress - 7) * .0023f * factor)) return 4;
        if (sample < Mathf.Min(.25f, Mathf.Max(0, progress - 3) * .02f * factor)) return 3;
        return sample < Mathf.Min(.60f, Mathf.Max(0, progress - 1) * .06f * factor) ? 2 : 1;
    }

    /// <summary>本体升级保底；返回零表示该等级独立抽取四张品质。</summary>
    public static int GuaranteedUpgradeTier(int level)
    { return level == 1 ? 1 : level % 5 != 0 ? 0 : level == 5 ? 2 : level < 25 ? 3 : 4; }

    /// <summary>本体掉率使用线性幸运倍率，零以下没有负概率；宝箱按本波已掉落数量递减。</summary>
    public static float DropChance(float baseChance, float luckPoints, int cratesDropped = 0)
    { return Mathf.Clamp01(baseChance * Mathf.Max(0, (100 + luckPoints) * .01f) / (1 + Mathf.Max(0, cratesDropped))); }

    /// <summary>统一计算工程效果；建筑不自动继承普通伤害、攻速和暴击加成。</summary>
    public static float EngineeringPower(float baseValue, float coefficient, float engineering)
    { return Mathf.Max(1, Mathf.Floor(baseValue + coefficient * engineering)); }

    /// <summary>统一向下取整直接伤害且至少一点；保留负值抵消到最终消费点。</summary>
    public static float Damage(WeaponLevelData weapon, PlayerStats player)
    {
        float flat = weapon.damage + weapon.meleeScaling * player.GetFinalStat(PlayerStatType.MeleeDamage)
            + weapon.rangedScaling * player.GetFinalStat(PlayerStatType.RangedDamage)
            + weapon.elementalScaling * player.GetFinalStat(PlayerStatType.ElementalDamage);
        return Mathf.Max(1, Mathf.Floor(Mathf.Max(0, flat) * Mathf.Max(0, (100 + weapon.damagePercent + player.GetFinalStat(PlayerStatType.DamagePercent)) * .01f)));
    }

    /// <summary>范围点转换为世界单位，近战只接受一半增量，最低距离 25 点。</summary>
    public static float WeaponRange(float baseline, float weaponPoints, float playerPoints, bool melee)
    { return Mathf.Max(.25f, baseline + (weaponPoints + playerPoints) * RangeUnits * (melee ? .5f : 1f)); }
}
