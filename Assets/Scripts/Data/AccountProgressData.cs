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
    public const int CurrentVersion = 1;

    /// <summary>默认直接解锁角色的稳定 ID。</summary>
    public const string DefaultCharacterId = "character_default";

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

    /// <summary>Session 14 首版 Seal 上限；本阶段不提供第二个槽位。</summary>
    public const int MaxSealCapacity = 1;
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
        data.sealCapacity = Math.Max(
            AccountProgressRules.InitialSealCapacity,
            Math.Min(AccountProgressRules.MaxSealCapacity, data.sealCapacity));

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
