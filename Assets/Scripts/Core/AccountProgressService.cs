using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 账号跨局进度的唯一运行时权威来源。
/// UI 和玩法系统只通过本服务查询或请求变更，不直接修改可序列化列表。
/// </summary>
public sealed class AccountProgressService
{
    private static AccountProgressService _current;

    private readonly IAccountProgressStorage _storage;
    private readonly HashSet<string> _unlockedCharacterIds;
    private readonly HashSet<string> _discoveredCharacterIds;
    private readonly HashSet<string> _discoveredWeaponIds;
    private readonly HashSet<string> _discoveredUpgradeIds;
    private readonly HashSet<string> _sealedUpgradeIds;
    private AccountProgressData _data;
    private bool _isReadOnly;

    /// <summary>账号金币、解锁、发现、选择或 Seal 变化后触发。</summary>
    public event Action Changed;

    /// <summary>返回当前进程的账号服务；批处理模式使用内存后端避免污染真实存档。</summary>
    public static AccountProgressService Current
    {
        get
        {
            if (_current == null)
            {
                IAccountProgressStorage storage = Application.isBatchMode
                    ? new InMemoryAccountProgressStorage()
                    : new JsonAccountProgressStorage(Application.persistentDataPath);
                _current = new AccountProgressService(storage);
            }

            return _current;
        }
    }

    /// <summary>使用指定后端载入账号；公开构造用于纯逻辑和临时目录测试。</summary>
    public AccountProgressService(IAccountProgressStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        AccountProgressLoadResult loadResult = _storage.Load();
        _data = loadResult.Data ?? AccountProgressData.CreateDefault();
        _isReadOnly = loadResult.IsReadOnly;
        AccountProgressMigrator.Normalize(_data);

        _unlockedCharacterIds = CreateIdSet(_data.unlockedCharacterIds);
        _discoveredCharacterIds = CreateIdSet(_data.discoveredCharacterIds);
        _discoveredWeaponIds = CreateIdSet(_data.discoveredWeaponIds);
        _discoveredUpgradeIds = CreateIdSet(_data.discoveredUpgradeIds);
        _sealedUpgradeIds = CreateIdSet(_data.sealedUpgradeIds);

        if (!string.IsNullOrWhiteSpace(loadResult.Message))
        {
            if (_isReadOnly)
            {
                Debug.LogError($"[AccountProgress] {loadResult.Message}");
            }
            else
            {
                Debug.LogWarning($"[AccountProgress] {loadResult.Message}");
            }
        }

        if (loadResult.ShouldPersist && !_isReadOnly)
        {
            Save();
        }
    }

    /// <summary>当前可消费账号金币。</summary>
    public int Gold => _data.accountGold;

    /// <summary>账号历史累计获得金币，不随消费减少。</summary>
    public long LifetimeGoldEarned => _data.lifetimeGoldEarned;

    /// <summary>账号历史累计结算击杀。</summary>
    public int LifetimeKills => _data.lifetimeKills;

    /// <summary>最近确认的已解锁角色稳定 ID。</summary>
    public string LastSelectedCharacterId => _data.lastSelectedCharacterId;

    /// <summary>当前可同时启用的 Seal 槽位数。</summary>
    public int SealCapacity => _data.sealCapacity;

    /// <summary>当前已经启用的 Seal 数量。</summary>
    public int ActiveSealCount => _sealedUpgradeIds.Count;

    /// <summary>未来版本存档是否使当前服务进入禁止覆盖的只读模式。</summary>
    public bool IsReadOnly => _isReadOnly;

    /// <summary>查询角色是否已经永久解锁。</summary>
    public bool IsCharacterUnlocked(string characterId)
    {
        return IsValidId(characterId) && _unlockedCharacterIds.Contains(characterId.Trim());
    }

    /// <summary>查询角色是否已经进入账号收藏发现记录。</summary>
    public bool IsCharacterDiscovered(string characterId)
    {
        return IsValidId(characterId) && _discoveredCharacterIds.Contains(characterId.Trim());
    }

    /// <summary>查询武器是否曾被当前账号实际持有。</summary>
    public bool IsWeaponDiscovered(string weaponId)
    {
        return IsValidId(weaponId) && _discoveredWeaponIds.Contains(weaponId.Trim());
    }

    /// <summary>查询升级项目是否曾出现在当前账号的候选中。</summary>
    public bool IsUpgradeDiscovered(string upgradeId)
    {
        return IsValidId(upgradeId) && _discoveredUpgradeIds.Contains(upgradeId.Trim());
    }

    /// <summary>查询升级项目是否被账号级 Seal 长期过滤。</summary>
    public bool IsUpgradeSealed(string upgradeId)
    {
        return IsValidId(upgradeId) && _sealedUpgradeIds.Contains(upgradeId.Trim());
    }

    /// <summary>
    /// 结算一局的金币与击杀。调用方必须保证同一局只调用一次；本方法执行饱和加法并立即保存。
    /// </summary>
    public bool RecordRunResults(int gold, int kills)
    {
        if (_isReadOnly || (gold <= 0 && kills <= 0))
        {
            return false;
        }

        int safeGold = Math.Max(0, gold);
        int safeKills = Math.Max(0, kills);
        _data.accountGold = AddClamped(_data.accountGold, safeGold);
        _data.lifetimeGoldEarned = AddClamped(_data.lifetimeGoldEarned, safeGold);
        _data.lifetimeKills = AddClamped(_data.lifetimeKills, safeKills);
        PersistAndNotify();
        return true;
    }

    /// <summary>按当前累计统计评估无需玩家点击购买的角色解锁条件。</summary>
    public int EvaluateAutomaticUnlocks(IReadOnlyList<CharacterDataSO> characters)
    {
        if (_isReadOnly || characters == null)
        {
            return 0;
        }

        int unlockedCount = 0;
        for (int index = 0; index < characters.Count; index++)
        {
            CharacterDataSO character = characters[index];
            if (character == null || IsCharacterUnlocked(character.characterID))
            {
                continue;
            }

            CharacterUnlockDefinition unlock = character.unlock;
            bool shouldUnlock = unlock == null || unlock.conditionType == CharacterUnlockConditionType.None;
            if (unlock != null && unlock.conditionType == CharacterUnlockConditionType.LifetimeKills)
            {
                shouldUnlock = _data.lifetimeKills >= Math.Max(0, unlock.requiredAmount);
            }

            if (shouldUnlock && UnlockCharacterInternal(character.characterID))
            {
                unlockedCount++;
            }
        }

        if (unlockedCount > 0)
        {
            PersistAndNotify();
        }

        return unlockedCount;
    }

    /// <summary>尝试消耗账号金币解锁指定角色；其他条件不会通过本入口自动扣款。</summary>
    public bool TryPurchaseCharacter(CharacterDataSO character)
    {
        if (_isReadOnly || character == null || !IsValidId(character.characterID) ||
            IsCharacterUnlocked(character.characterID) || character.unlock == null ||
            character.unlock.conditionType != CharacterUnlockConditionType.GoldPurchase)
        {
            return false;
        }

        int cost = Math.Max(0, character.unlock.requiredAmount);
        if (_data.accountGold < cost)
        {
            return false;
        }

        _data.accountGold -= cost;
        UnlockCharacterInternal(character.characterID);
        PersistAndNotify();
        return true;
    }

    /// <summary>记录最近确认角色；锁定或空 ID 不会覆盖已有选择。</summary>
    public bool SetLastSelectedCharacter(string characterId)
    {
        if (_isReadOnly || !IsCharacterUnlocked(characterId))
        {
            return false;
        }

        string normalized = characterId.Trim();
        if (string.Equals(_data.lastSelectedCharacterId, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        _data.lastSelectedCharacterId = normalized;
        PersistAndNotify();
        return true;
    }

    /// <summary>登记实际持有的武器；重复发现不产生磁盘写入。</summary>
    public bool DiscoverWeapon(string weaponId)
    {
        return DiscoverId(weaponId, _discoveredWeaponIds, _data.discoveredWeaponIds);
    }

    /// <summary>登记已经展示过的升级候选；重复发现不产生磁盘写入。</summary>
    public bool DiscoverUpgrade(string upgradeId)
    {
        return DiscoverId(upgradeId, _discoveredUpgradeIds, _data.discoveredUpgradeIds);
    }

    /// <summary>
    /// 启用或解除账号级 Seal。启用时要求升级已发现且仍有槽位；解除会立即归还槽位。
    /// </summary>
    public bool TrySetUpgradeSealed(string upgradeId, bool sealedState)
    {
        if (_isReadOnly || !IsValidId(upgradeId))
        {
            return false;
        }

        string normalized = upgradeId.Trim();
        if (sealedState)
        {
            if (!_discoveredUpgradeIds.Contains(normalized) ||
                _sealedUpgradeIds.Contains(normalized) ||
                _sealedUpgradeIds.Count >= _data.sealCapacity)
            {
                return false;
            }

            _sealedUpgradeIds.Add(normalized);
            _data.sealedUpgradeIds.Add(normalized);
        }
        else
        {
            if (!_sealedUpgradeIds.Remove(normalized))
            {
                return false;
            }

            _data.sealedUpgradeIds.Remove(normalized);
        }

        PersistAndNotify();
        return true;
    }

    /// <summary>把账号恢复为首版默认值，并通过正式存储路径保留旧主档备份。</summary>
    public bool ResetToDefaults()
    {
        if (_isReadOnly)
        {
            return false;
        }

        _data = AccountProgressData.CreateDefault();
        RebuildIndexes();
        PersistAndNotify();
        return true;
    }

#if UNITY_EDITOR
    /// <summary>仅供 Editor 自动化为静态入口注入隔离存储，防止用例之间共享账号状态。</summary>
    public static void SetStorageForTests(IAccountProgressStorage storage)
    {
        _current = new AccountProgressService(storage ?? new InMemoryAccountProgressStorage());
    }
#endif

    /// <summary>把角色稳定 ID 同时写入解锁与收藏发现集合。</summary>
    private bool UnlockCharacterInternal(string characterId)
    {
        if (!IsValidId(characterId))
        {
            return false;
        }

        string normalized = characterId.Trim();
        if (!_unlockedCharacterIds.Add(normalized))
        {
            return false;
        }

        _data.unlockedCharacterIds.Add(normalized);
        if (_discoveredCharacterIds.Add(normalized))
        {
            _data.discoveredCharacterIds.Add(normalized);
        }

        return true;
    }

    /// <summary>把新发现 ID 写入对应集合和序列化列表，并只在首次发现时保存。</summary>
    private bool DiscoverId(string id, HashSet<string> index, List<string> serializedIds)
    {
        if (_isReadOnly || !IsValidId(id))
        {
            return false;
        }

        string normalized = id.Trim();
        if (!index.Add(normalized))
        {
            return false;
        }

        serializedIds.Add(normalized);
        PersistAndNotify();
        return true;
    }

    /// <summary>保存当前账号并发布低频变化事件；保存失败时保留内存状态并报告错误。</summary>
    private void PersistAndNotify()
    {
        Save();
        Changed?.Invoke();
    }

    /// <summary>把当前账号快照写入存储后端。</summary>
    private bool Save()
    {
        if (_isReadOnly)
        {
            return false;
        }

        if (_storage.Save(_data))
        {
            return true;
        }

        Debug.LogError("[AccountProgress] 账号进度未能写入存储后端。");
        return false;
    }

    /// <summary>在整份账号数据被替换后重建全部运行时 O(1) 查询索引。</summary>
    private void RebuildIndexes()
    {
        ReplaceSet(_unlockedCharacterIds, _data.unlockedCharacterIds);
        ReplaceSet(_discoveredCharacterIds, _data.discoveredCharacterIds);
        ReplaceSet(_discoveredWeaponIds, _data.discoveredWeaponIds);
        ReplaceSet(_discoveredUpgradeIds, _data.discoveredUpgradeIds);
        ReplaceSet(_sealedUpgradeIds, _data.sealedUpgradeIds);
    }

    /// <summary>从序列化稳定 ID 列表建立区分大小写的运行时集合。</summary>
    private static HashSet<string> CreateIdSet(List<string> source)
    {
        return new HashSet<string>(source ?? new List<string>(), StringComparer.Ordinal);
    }

    /// <summary>用序列化列表完整替换已有查询集合。</summary>
    private static void ReplaceSet(HashSet<string> target, List<string> source)
    {
        target.Clear();
        if (source == null)
        {
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    /// <summary>判断稳定 ID 是否包含可用的非空白内容。</summary>
    private static bool IsValidId(string id)
    {
        return !string.IsNullOrWhiteSpace(id);
    }

    /// <summary>执行不会溢出为负数的 int 饱和加法。</summary>
    private static int AddClamped(int current, int amount)
    {
        long total = (long)current + amount;
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>执行不会溢出为负数的 long 饱和加法。</summary>
    private static long AddClamped(long current, long amount)
    {
        if (amount > 0L && current > long.MaxValue - amount)
        {
            return long.MaxValue;
        }

        return current + amount;
    }
}
