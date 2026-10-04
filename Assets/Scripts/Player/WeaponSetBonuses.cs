using System;
using System.Collections.Generic;

/// <summary>只在装备变更时重建羁绊，并作为单一来源替换属性；没有逐帧扫描。</summary>
public sealed class WeaponSetBonuses
{
    private const string Source = "weapons.sets";
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, WeaponSetSO> _sets = new Dictionary<string, WeaponSetSO>(StringComparer.Ordinal);
    private readonly HashSet<string> _oneWeapon = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<PlayerStatModifier> _modifiers = new List<PlayerStatModifier>();
    private PlayerStats _target;

    /// <summary>读取装备副本数；一把武器的重复标签只计一次，品质不参与计数。</summary>
    public int Count(WeaponSetSO set)
        => set != null && _counts.TryGetValue(set.GetStableId(), out int count) ? count : 0;

    /// <summary>装备清单已完成变更后调用；先建立最终计数，再一次性发布属性变化。</summary>
    public void Rebuild(IReadOnlyList<WeaponBase> weapons, PlayerStats player)
    {
        if (_target != null && _target != player) _target.RemoveModifiers(Source);
        _target = player; _counts.Clear(); _sets.Clear(); _modifiers.Clear();
        if (weapons != null)
            for (int i = 0; i < weapons.Count; i++)
            {
                WeaponDataSO data = weapons[i] != null ? weapons[i].weaponData : null;
                if (data == null || data.weaponSets == null) continue;
                _oneWeapon.Clear();
                foreach (WeaponSetSO set in data.weaponSets)
                {
                    if (set == null) continue;
                    string id = set.GetStableId();
                    if (string.IsNullOrWhiteSpace(id) || !_oneWeapon.Add(id)) continue;
                    _counts.TryGetValue(id, out int count); _counts[id] = count + 1; _sets[id] = set;
                }
            }
        foreach (KeyValuePair<string, int> pair in _counts)
            _modifiers.AddRange(_sets[pair.Key].GetModifiers(pair.Value));
        if (_target != null)
        {
            if (_target.UsesBrotatoStats) _target.SetModifiers(Source, _modifiers);
            else _target.RemoveModifiers(Source);
        }
    }

    /// <summary>管理器退出时清除本来源，不触碰道具、升级和角色自身属性。</summary>
    public void Clear()
    {
        if (_target != null) _target.RemoveModifiers(Source);
        _target = null; _counts.Clear(); _sets.Clear(); _oneWeapon.Clear(); _modifiers.Clear();
    }
}
