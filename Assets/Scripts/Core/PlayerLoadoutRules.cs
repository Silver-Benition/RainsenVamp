/// <summary>
/// 定义单局玩家可持有的装备种类上限。
/// 武器和未来的能力系统必须共用此规则，避免逻辑容量与 HUD 槽位数量不一致。
/// </summary>
public static class PlayerLoadoutRules
{
    /// <summary>玩家最多同时持有的不同武器种类数。</summary>
    public const int MaxWeaponCount = 6;

    /// <summary>玩家最多同时持有的不同能力种类数。</summary>
    public const int MaxAbilityCount = 6;
}
