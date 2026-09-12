using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>主菜单与玩家显式共用的成长目录，同时保存额外排除槽逐级价格。</summary>
[CreateAssetMenu(menuName = "RainsenVampSur/Account Upgrade Catalog")]
public sealed class AccountUpgradeCatalogSO : ScriptableObject
{
    public const string SealSlotId = "account_seal_slots";
    public List<AccountUpgradeDataSO> upgrades = new List<AccountUpgradeDataSO>();
    public int maxSealSlotLevel = 4;
    public List<int> sealSlotCosts = new List<int> { 100, 250, 500, 1000 };

    /// <summary>验证全 21 项、稳定 ID 唯一性和所有可购买等级；失败时禁止新购买。</summary>
    public bool Validate(out string error)
    {
        error = "成长目录配置不可用";
        if (upgrades == null || upgrades.Count != Enum.GetValues(typeof(PlayerStatType)).Length) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var stats = new HashSet<PlayerStatType>();
        foreach (AccountUpgradeDataSO upgrade in upgrades)
        {
            if (upgrade == null || !upgrade.Validate(out _) || upgrade.stableId == SealSlotId ||
                !ids.Add(upgrade.stableId) || !stats.Add(upgrade.statType)) return false;
        }
        if (maxSealSlotLevel < 0 || sealSlotCosts == null || sealSlotCosts.Count < maxSealSlotLevel) return false;
        for (int i = 0; i < maxSealSlotLevel; i++) if (sealSlotCosts[i] < 0) return false;
        error = string.Empty;
        return true;
    }

    /// <summary>根据稳定 ID 查找定义；重复 ID 不返回任意一个定义以避免错扣价格。</summary>
    public AccountUpgradeDataSO Find(string id)
    {
        AccountUpgradeDataSO result = null;
        if (upgrades == null) return null;
        foreach (AccountUpgradeDataSO upgrade in upgrades)
        {
            if (upgrade == null || upgrade.stableId != id) continue;
            if (result != null) return null;
            result = upgrade;
        }
        return result;
    }
}
