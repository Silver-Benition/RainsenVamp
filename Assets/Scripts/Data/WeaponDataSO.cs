using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 武器运行时类型。数值 0、1 保持与旧升级资产一致。
/// </summary>
public enum WeaponRuntimeType
{
    Projectile = 0,
    Aura = 1,
    Orbiting = 2,
    Lobbed = 3,
    Melee = 4
}

/// <summary>
/// 弹射模式枚举。
/// </summary>
public enum BounceMode
{
    None = 0,
    Directional = 1,
    Tracking = 2
}

/// <summary>
/// 功能型升级标签。
/// </summary>
public enum WeaponFeatureType
{
    None = 0,
    AuraPersistent = 1,
    MultiShot = 2,
    Ricochet = 3,
    ExplodeOnHit = 4
}

/// <summary>
/// 单个武器等级的数值快照。编辑工具会根据武器类型隐藏无关字段。
/// </summary>
[System.Serializable]
public sealed class WeaponLevelData
{
    [Header("通用数值")]
    [Min(0f)] public float damage = 10f;
    [Min(0.05f)] public float cooldown = 1f;
    [Min(1)] public int projectileCount = 1;

    [Header("直飞与投掷")]
    [Min(0f)] public float projectileSpeed = 5f;
    [Tooltip("额外穿透层数。0 表示命中首个目标后回收。")]
    [Min(0)] public int pierceCount;
    [Min(0.01f)] public float lifeTime = 3f;
    [Range(0f, 360f)] public float spreadAngle;
    [Min(0)] public int bounceCount;
    public BounceMode bounceMode = BounceMode.Directional;

    [Header("光环")]
    [Min(0.05f)] public float auraRadius = 3f;
    [Min(0.01f)] public float tickInterval = 0.5f;

    [Header("环绕")]
    [Min(0.05f)] public float orbitRadius = 1.7f;
    [Tooltip("环绕角速度，单位为度/秒。")]
    public float orbitAngularSpeed = 180f;

    [Header("抛物线投掷")]
    [FormerlySerializedAs("arcHeight")]
    [Tooltip("投掷物受到的恒定向下加速度，单位为世界单位/秒²。")]
    [Min(0.01f)] public float lobGravity = 3f;
    [Tooltip("投掷物自转速度，单位为度/秒。")]
    public float spinSpeed = 540f;

    [Header("近战挥击")]
    [Min(0.05f)] public float meleeRange = 1.5f;
    [Range(1f, 360f)] public float meleeArc = 90f;
    [Min(0.02f)] public float activeDuration = 0.18f;

    [Header("功能标签")]
    public List<WeaponFeatureType> features = new List<WeaponFeatureType>();
}

/// <summary>
/// 武器静态配置资产。运行时只读取当前等级的快照，不修改共享资产。
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "GameData/Weapon Data")]
public sealed class WeaponDataSO : ScriptableObject
{
    [Header("基础信息")]
    [Tooltip("稳定且唯一的逻辑 ID，不使用本地化显示名。")]
    public string weaponID;
    public string weaponNameKey;
    [TextArea] public string descriptionKey;
    public WeaponRuntimeType runtimeType = WeaponRuntimeType.Projectile;

    [Header("UI 表现")]
    [Tooltip("武器在升级候选和右上角持有栏中使用的统一图标。")]
    public Sprite icon;
    [Tooltip("仅影响右上角持有栏中的图标缩放，用于补偿原图透明留白；不影响战斗 Sprite。")]
    [Min(0.01f)] public float loadoutIconScale = 1f;
    [Tooltip("仅影响右上角持有栏中的图标位置，用于把放大后的可见内容重新居中。")]
    public Vector2 loadoutIconOffset;

    [Header("每级配置")]
    [Tooltip("索引 0 对应 Lv.1。每级保存完整快照，避免运行时修改 ScriptableObject。")]
    public List<WeaponLevelData> levelConfigs = new List<WeaponLevelData>
    {
        new WeaponLevelData()
    };

    [Header("运行时实体")]
    [Tooltip("对象池使用的攻击实体 Prefab；光环、环绕物和近战判定同样使用该入口。")]
    public GameObject projectilePrefab;

    /// <summary>
    /// 返回武器最大等级；空配置仍按一级处理，避免升级流程出现零级上限。
    /// </summary>
    public int MaxLevel => Mathf.Max(1, levelConfigs != null ? levelConfigs.Count : 0);

    /// <summary>
    /// 安全读取从 1 开始的等级配置；越界时回退到最近的有效等级。
    /// </summary>
    /// <param name="level">从 1 开始的目标等级。</param>
    /// <returns>可供本次攻击读取的等级快照配置。</returns>
    public WeaponLevelData GetLevelConfig(int level)
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            return new WeaponLevelData();
        }

        int index = Mathf.Clamp(level - 1, 0, levelConfigs.Count - 1);
        return levelConfigs[index];
    }
}
