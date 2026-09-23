/// <summary>
/// 玩家局内属性类型。
/// Seal 属于局外候选池配置，不进入本枚举与角色运行时属性快照。
/// </summary>
public enum PlayerStatType
{
    MaxHealth = 0,
    Recovery = 1,
    Armor = 2,
    MoveSpeed = 3,
    Might = 4,
    Area = 5,
    ProjectileSpeed = 6,
    Duration = 7,
    Amount = 8,
    Cooldown = 9,
    Luck = 10,
    Growth = 11,
    Greed = 12,
    Curse = 13,
    Magnet = 14,
    Revival = 15,
    Reroll = 16,
    Skip = 17,
    Banish = 18,
    Charm = 19,
    Defang = 20,
    // 旧编号必须保持稳定；新体系使用点数，百分比 20 表示 20%。
    HpRegeneration = 21,
    LifeSteal = 22,
    DamagePercent = 23,
    MeleeDamage = 24,
    RangedDamage = 25,
    ElementalDamage = 26,
    AttackSpeed = 27,
    CritChance = 28,
    Engineering = 29,
    Range = 30,
    Dodge = 31,
    SpeedPercent = 32,
    LuckPoints = 33,
    Harvesting = 34,
    ExperienceGain = 35,
    PickupRange = 36
}
