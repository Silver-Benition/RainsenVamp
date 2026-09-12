using System;
using System.Collections.Generic;

/// <summary>
/// 账号进度的可序列化数据传输对象。
/// 仅保存跨局状态，不包含当前一局的生命、金币计数、Banish 或升级队列。
/// </summary>
[Serializable]
public sealed class AccountProgressData
{
    /// <summary>当前客户端支持的账号存档版本。</summary>
    public const int CurrentVersion = 2;

    /// <summary>默认直接解锁角色的稳定 ID。</summary>
    public const string DefaultCharacterId = "character_default";

    public List<AccountUpgradePurchaseRecord> upgradePurchases = new List<AccountUpgradePurchaseRecord>();

    public int saveVersion = CurrentVersion;
    public int accountGold;
    public long lifetimeGoldEarned;
    public int lifetimeKills;
    public string lastSelectedCharacterId = DefaultCharacterId;
    public int sealCapacity = AccountProgressRules.InitialSealCapacity;
    public List<string> unlockedCharacterIds = new List<string>();
    public List<string> discoveredCharacterIds = new List<string>();
    public List<string> discoveredWeaponIds = new List<string>();
    public List<string> discoveredUpgradeIds = new List<string>();
    public List<string> sealedUpgradeIds = new List<string>();

    /// <summary>创建满足首版默认解锁与 Seal 容量规则的新账号。</summary>
    public static AccountProgressData CreateDefault()
    {
        var data = new AccountProgressData();
        data.unlockedCharacterIds.Add(DefaultCharacterId);
        data.discoveredCharacterIds.Add(DefaultCharacterId);
        return data;
    }
}

/// <summary>集中保存首版账号进度的硬边界，避免 UI 与存档分别声明 Seal 上限。</summary>
public static class AccountProgressRules
{
    /// <summary>新账号初始可同时启用的 Seal 数量。</summary>
    public const int InitialSealCapacity = 1;

    /// <summary>容量整数安全上限；实际可购买等级由成长目录配置。</summary>
    public const int MaxSealCapacity = int.MaxValue;
}

/// <summary>负责把旧存档逐版本迁移并修复可安全纠正的数据边界。</summary>
public static class AccountProgressMigrator
{
    /// <summary>
    /// 把受支持的旧数据升级到当前版本并执行归一化。
    /// 高于当前版本的数据由存储层拒绝，不会进入本方法。
    /// </summary>
    public static AccountProgressData MigrateToCurrent(AccountProgressData data)
    {
        if (data == null)
        {
            return AccountProgressData.CreateDefault();
        }

        // 版本 0 代表首版之前没有显式版本号的兼容 JSON；当前没有更早的正式账号档。
        if (data.saveVersion <= 0)
        {
            data.saveVersion = 1;
        }

        if (data.saveVersion < 2)
        {
            data.upgradePurchases = new List<AccountUpgradePurchaseRecord>();
            data.sealCapacity = AccountProgressRules.InitialSealCapacity;
        }
        Normalize(data);
        data.saveVersion = AccountProgressData.CurrentVersion;
        return data;
    }

    /// <summary>钳制数值并清理空白、重复和超出容量的稳定 ID。</summary>
    public static void Normalize(AccountProgressData data)
    {
        if (data == null)
        {
            return;
        }

        data.accountGold = Math.Max(0, data.accountGold);
        data.lifetimeGoldEarned = Math.Max(0L, data.lifetimeGoldEarned);
        data.lifetimeKills = Math.Max(0, data.lifetimeKills);
        if (data.upgradePurchases == null) data.upgradePurchases = new List<AccountUpgradePurchaseRecord>();
        // 不裁剪购买记录：配置降上限、重复或异常记录均保留原始历史。
        // 槽位只从唯一且合法的实付记录推导，不信任可被篡改的旧容量字段。
        AccountUpgradePurchaseRecord slots = FindValidPurchase(data, AccountUpgradeCatalogSO.SealSlotId);
        data.sealCapacity = AccountProgressRules.InitialSealCapacity + (slots != null ? slots.paidCosts.Count : 0);

        data.unlockedCharacterIds = NormalizeIds(data.unlockedCharacterIds);
        data.discoveredCharacterIds = NormalizeIds(data.discoveredCharacterIds);
        data.discoveredWeaponIds = NormalizeIds(data.discoveredWeaponIds);
        data.discoveredUpgradeIds = NormalizeIds(data.discoveredUpgradeIds);
        data.sealedUpgradeIds = NormalizeIds(data.sealedUpgradeIds);

        AddUnique(data.unlockedCharacterIds, AccountProgressData.DefaultCharacterId);
        AddUnique(data.discoveredCharacterIds, AccountProgressData.DefaultCharacterId);

        if (data.sealedUpgradeIds.Count > data.sealCapacity)
        {
            data.sealedUpgradeIds.RemoveRange(
                data.sealCapacity,
                data.sealedUpgradeIds.Count - data.sealCapacity);
        }

        if (string.IsNullOrWhiteSpace(data.lastSelectedCharacterId))
        {
            data.lastSelectedCharacterId = AccountProgressData.DefaultCharacterId;
        }
        else
        {
            data.lastSelectedCharacterId = data.lastSelectedCharacterId.Trim();
        }
    }

    /// <summary>只返回唯一且逐级金额非负的记录；异常历史原样保留，拒绝在歧义记录上继续交易。</summary>
    public static AccountUpgradePurchaseRecord FindValidPurchase(AccountProgressData data, string id)
    {
        AccountUpgradePurchaseRecord found = null;
        if (data.upgradePurchases == null) return null;
        foreach (AccountUpgradePurchaseRecord record in data.upgradePurchases)
        {
            if (record == null || record.stableId != id) continue;
            if (found != null || record.paidCosts == null) return null;
            foreach (int cost in record.paidCosts) if (cost < 0) return null;
            found = record;
        }
        return found;
    }

    /// <summary>返回去除空白和重复项后的稳定 ID 列表，并保持第一次出现的顺序。</summary>
    private static List<string> NormalizeIds(List<string> source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (source == null)
        {
            return result;
        }

        for (int index = 0; index < source.Count; index++)
        {
            string id = source[index];
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string normalized = id.Trim();
            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>仅在列表尚未包含稳定 ID 时追加该项。</summary>
    private static void AddUnique(List<string> target, string id)
    {
        if (target != null && !target.Contains(id))
        {
            target.Add(id);
        }
    }
}

/// <summary>每级实付金额是退款权威；列表长度是购买等级，不随配置变化裁剪。</summary>
[Serializable]
public sealed class AccountUpgradePurchaseRecord
{
    public string stableId;
    public List<int> paidCosts = new List<int>();
}
