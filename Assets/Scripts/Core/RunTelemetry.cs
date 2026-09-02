using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单局战斗统计权威容器。
/// 只在武器获得/升级等低频时更新清单，命中热路径只做稳定字典查找与浮点累加。
/// </summary>
public sealed class RunTelemetry
{
    private sealed class WeaponRecord
    {
        public WeaponDataSO data;
        public string stableId;
        public float firstEffectTime;
        public float actualDamage;
    }

    private sealed class PickupRecord
    {
        public MapInstantEffectPickupDataSO data;
        public string stableId;
        public int count;
    }

    private readonly Dictionary<string, WeaponRecord> _weaponsById =
        new Dictionary<string, WeaponRecord>(StringComparer.Ordinal);
    private readonly List<WeaponRecord> _weaponOrder = new List<WeaponRecord>(PlayerLoadoutRules.MaxWeaponCount);
    private readonly Dictionary<string, PickupRecord> _pickupsById =
        new Dictionary<string, PickupRecord>(StringComparer.Ordinal);
    private readonly List<PickupRecord> _pickupOrder = new List<PickupRecord>(8);
    private bool _frozen;
    private float _lastRuntimeWeaponAcquisitionTime;
    private bool _hasRuntimeWeaponAcquisitionTime;

    private const float RuntimeWeaponAcquisitionEpsilon = 0.000001f;

    /// <summary>当前场景被 CombatDamageResolver 使用的遥测实例。</summary>
    public static RunTelemetry Active { get; private set; }

    /// <summary>统计是否已经冻结；冻结后迟到的命中和拾取上报全部忽略。</summary>
    public bool IsFrozen => _frozen;

    /// <summary>把当前局遥测注册为全局战斗结算目标。</summary>
    public static void Activate(RunTelemetry telemetry)
    {
        Active = telemetry;
    }

    /// <summary>
    /// 同步正式持有的武器列表。
    /// 初始扫描使用 0 秒；后续新武器按调用方提供的正式获得时间登记，升级不会重置获得时间。
    /// </summary>
    public void SyncOwnedWeapons(IReadOnlyList<WeaponBase> ownedWeapons, float officialTimeSeconds, bool initialScan)
    {
        if (_frozen || ownedWeapons == null)
        {
            return;
        }

        float safeTime = initialScan
            ? 0f
            : RunResultValueSanitizer.SanitizeNonNegative(officialTimeSeconds);
        for (int index = 0; index < ownedWeapons.Count; index++)
        {
            WeaponBase weapon = ownedWeapons[index];
            if (weapon != null && weapon.weaponData != null)
            {
                RegisterWeapon(weapon.weaponData, safeTime);
            }
        }
    }

    /// <summary>登记一把武器的首次正式获得时间；重复登记不会覆盖旧时间。</summary>
    public bool RegisterWeapon(WeaponDataSO data, float officialTimeSeconds)
    {
        if (_frozen || data == null)
        {
            return false;
        }

        string stableId = data.GetStableId();
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        if (_weaponsById.ContainsKey(stableId))
        {
            return false;
        }

        var record = new WeaponRecord
        {
            data = data,
            stableId = stableId,
            firstEffectTime = RunResultValueSanitizer.SanitizeNonNegative(officialTimeSeconds),
            actualDamage = 0f
        };
        _weaponsById.Add(stableId, record);
        _weaponOrder.Add(record);
        return true;
    }

    /// <summary>
    /// 登记运行期间新获得的武器，并强制其获得时间严格晚于起始扫描和上一把运行时武器。
    /// 该时序由事件语义保证，不依赖首帧时长或任意时间窗口启发式。
    /// </summary>
    public bool RegisterRuntimeWeapon(WeaponDataSO data, float observedTimeSeconds)
    {
        if (_frozen || data == null)
        {
            return false;
        }

        float safeObservedTime = RunResultValueSanitizer.SanitizeNonNegative(observedTimeSeconds);
        if (!_hasRuntimeWeaponAcquisitionTime && safeObservedTime <= 0f)
        {
            safeObservedTime = RuntimeWeaponAcquisitionEpsilon;
        }
        else if (_hasRuntimeWeaponAcquisitionTime && safeObservedTime <= _lastRuntimeWeaponAcquisitionTime)
        {
            safeObservedTime = RunResultValueSanitizer.SaturatingAdd(
                _lastRuntimeWeaponAcquisitionTime,
                RuntimeWeaponAcquisitionEpsilon);
        }

        bool registered = RegisterWeapon(data, safeObservedTime);
        if (registered)
        {
            _lastRuntimeWeaponAcquisitionTime = safeObservedTime;
            _hasRuntimeWeaponAcquisitionTime = true;
        }

        return registered;
    }

    /// <summary>
    /// 记录一笔武器有效命中伤害。
    /// 未提前登记的来源会以当前效果时间补登记，兼容测试夹具和动态创建武器。
    /// </summary>
    public void RecordWeaponDamage(WeaponDataSO data, float actualDamage, float effectTimeSeconds)
    {
        float safeDamage = RunResultValueSanitizer.SanitizeNonNegative(actualDamage);
        if (_frozen || data == null || safeDamage <= 0f)
        {
            return;
        }

        string stableId = data.GetStableId();
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        if (!_weaponsById.TryGetValue(stableId, out WeaponRecord record))
        {
            RegisterWeapon(data, effectTimeSeconds);
            _weaponsById.TryGetValue(stableId, out record);
        }

        if (record == null)
        {
            return;
        }

        record.actualDamage = RunResultValueSanitizer.SaturatingAdd(record.actualDamage, safeDamage);
    }

    /// <summary>在地图即时效果真正成功后累计一次拾取；相同稳定 ID 聚合为一个结果行。</summary>
    public bool ReportInstantEffectPickup(MapInstantEffectPickupDataSO data)
    {
        if (_frozen || data == null)
        {
            return false;
        }

        string stableId = data.GetStableId();
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        if (!_pickupsById.TryGetValue(stableId, out PickupRecord record))
        {
            record = new PickupRecord
            {
                data = data,
                stableId = stableId,
                count = 0
            };
            _pickupsById.Add(stableId, record);
            _pickupOrder.Add(record);
        }

        if (record.count < int.MaxValue)
        {
            record.count++;
        }

        return true;
    }

    /// <summary>冻结统计容器，使所有迟到的池化回调无法改变最终快照。</summary>
    public void Freeze()
    {
        _frozen = true;
    }

    /// <summary>
    /// 按武器首次获得顺序生成结果行，并冻结从首次生效到结算时刻的有效时长与 DPS。
    /// 时间列和 DPS 必须共同使用这里计算出的 activeDurationSeconds，不能各自重新推导。
    /// </summary>
    public List<RunResultWeaponSnapshot> CreateWeaponSnapshots(
        float finalTimeSeconds,
        IReadOnlyList<WeaponBase> ownedWeapons)
    {
        var snapshots = new List<RunResultWeaponSnapshot>(_weaponOrder.Count);
        float safeFinalTime = RunResultValueSanitizer.SanitizeNonNegative(finalTimeSeconds);
        for (int index = 0; index < _weaponOrder.Count; index++)
        {
            WeaponRecord record = _weaponOrder[index];
            if (record == null || record.data == null)
            {
                continue;
            }

            int currentLevel = 1;
            if (ownedWeapons != null)
            {
                for (int weaponIndex = 0; weaponIndex < ownedWeapons.Count; weaponIndex++)
                {
                    WeaponBase weapon = ownedWeapons[weaponIndex];
                    if (weapon != null && weapon.weaponData != null &&
                        string.Equals(weapon.weaponData.GetStableId(), record.stableId, StringComparison.Ordinal))
                    {
                        currentLevel = Mathf.Max(1, weapon.CurrentLevel);
                        break;
                    }
                }
            }

            float activeDurationSeconds = RunResultValueSanitizer.CalculateActiveDuration(
                safeFinalTime,
                record.firstEffectTime);
            float dps = RunResultValueSanitizer.CalculateDamagePerSecond(
                record.actualDamage,
                activeDurationSeconds);
            snapshots.Add(new RunResultWeaponSnapshot(
                record.stableId,
                record.data.weaponNameKey,
                record.data.GetDisplayName(),
                record.data.icon,
                currentLevel,
                record.data.MaxLevel,
                record.actualDamage,
                record.firstEffectTime,
                activeDurationSeconds,
                dps));
        }

        return snapshots;
    }

    /// <summary>按数据资产排序权重和稳定 ID 生成拾取结果，保证多次运行展示顺序稳定。</summary>
    public List<RunResultPickupSnapshot> CreatePickupSnapshots()
    {
        var snapshots = new List<RunResultPickupSnapshot>(_pickupOrder.Count);
        for (int index = 0; index < _pickupOrder.Count; index++)
        {
            PickupRecord record = _pickupOrder[index];
            if (record == null || record.data == null)
            {
                continue;
            }

            snapshots.Add(new RunResultPickupSnapshot(
                record.stableId,
                record.data.nameKey,
                record.data.GetDisplayName(),
                record.data.icon,
                record.data.sortOrder,
                record.count));
        }

        // 结果冻结时才做小规模稳定排序，不把排序成本放入拾取或伤害热路径。
        for (int index = 1; index < snapshots.Count; index++)
        {
            RunResultPickupSnapshot current = snapshots[index];
            int insertIndex = index - 1;
            while (insertIndex >= 0 && IsAfter(snapshots[insertIndex], current))
            {
                snapshots[insertIndex + 1] = snapshots[insertIndex];
                insertIndex--;
            }

            snapshots[insertIndex + 1] = current;
        }

        return snapshots;
    }

    /// <summary>比较两个已冻结拾取行的排序键。</summary>
    private static bool IsAfter(RunResultPickupSnapshot left, RunResultPickupSnapshot right)
    {
        if (left.SortOrder != right.SortOrder)
        {
            return left.SortOrder > right.SortOrder;
        }

        return string.CompareOrdinal(left.PickupId, right.PickupId) > 0;
    }
}
