using System.Collections.Generic;

/// <summary>只在玩家首次初始化时解析局外累计效果，战斗读缓存且不占能力槽。</summary>
public static class AccountUpgradeResolver
{
    public const string SourceId = "account.upgrades.snapshot";

    /// <summary>复制已购买且在有效配置上限内的最终一级效果；保留超过上限的实付历史供退款。</summary>
    public static List<PlayerStatModifier> CreateSnapshot(AccountProgressService service, AccountUpgradeCatalogSO catalog)
    {
        var result = new List<PlayerStatModifier>();
        if (service == null || catalog == null || !catalog.Validate(out _)) return result;
        foreach (AccountUpgradeDataSO upgrade in catalog.upgrades)
        {
            int effectiveLevel = System.Math.Min(service.GetUpgradeLevel(upgrade.stableId), upgrade.maxLevel);
            if (effectiveLevel > 0) result.AddRange(upgrade.levels[effectiveLevel - 1].modifiers);
        }
        return result;
    }
}
