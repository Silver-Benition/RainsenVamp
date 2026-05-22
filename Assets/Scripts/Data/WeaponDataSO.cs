using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹射模式枚举：区分指向性弹射和追踪性弹射。
/// 指向性：命中后找最近未命中敌人并转向飞去。
/// 追踪性：全程锁定单一目标，命中后换下一个目标继续追踪（需派生 TrackingProjectile 实现）。
/// </summary>
public enum BounceMode
{
    None = 0,    // 不弹射
    Directional = 1,    // 指向性弹射：命中后瞬间转向最近未命中目标
    Tracking = 2,    // 追踪性弹射：全程跟踪目标，命中后切换下一目标（预留，需 TrackingProjectile 支持）
}

/// <summary>
/// 功能型升级标签（非纯数值）。
/// 当前先打通数据与运行时开关，具体行为由各武器脚本自行解释。
/// </summary>
public enum WeaponFeatureType
{
    None = 0,
    AuraPersistent = 1,   // 光环常驻（示例：AuraWeapon 可据此决定常驻逻辑）
    MultiShot = 2,        // 多重发射
    Ricochet = 3,         // 反弹
    ExplodeOnHit = 4      // 命中爆炸
}

[System.Serializable]
public class WeaponLevelData
{
    [Header("该等级数值")]
    public float damage = 10f;
    public float cooldown = 1f;
    public float projectileSpeed = 5f;
    [Tooltip("额外穿透层数。0=命中1次即消失（不穿透）；1=能额外穿透1个目标（共命中2次消失）；以此类推。")]
    public int pierceCount = 0;         // 额外穿透层数（0=不穿透）
    public float lifeTime = 3f;
    public float auraRadius = 3f;       // 光环类武器可用，非光环武器可忽略

    [Header("多发 & 弹射")]
    [Tooltip("同时发射的子弹数量（1=单发，2+=多发）。")]
    public int projectileCount = 1;     // 多发数量
    [Tooltip("多发时子弹之间的总扩散角（度）。0=平行射出，30=总扇形30度。")]
    public float spreadAngle = 0f;      // 多发扩散角（总角度，由 projectileCount 等分）
    [Tooltip("弹射次数（0=不弹射，1=命中后再弹1次，以此类推）。")]
    public int bounceCount = 0;         // 弹射次数
    [Tooltip("弹射模式：指向性（瞬间转向）或追踪性（全程锁定，预留）。")]
    public BounceMode bounceMode = BounceMode.Directional; // 弹射类型

    [Header("该等级功能标签")]
    public List<WeaponFeatureType> features = new List<WeaponFeatureType>();
}

// 添加到创建菜单，方便在 Project 窗口右键创建数据文件
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "GameData/Weapon Data")]
public class WeaponDataSO : ScriptableObject
{
    [Header("基础信息 (Base Info)")]
    public string weaponID;             // 武器唯一ID
    public string weaponNameKey;        // 多语言字典Key（预留本地化接口）
    [TextArea] public string descriptionKey; // 描述文本Key

    [Header("每级配置 (Per-Level Config)")]
    [Tooltip("按等级顺序配置（索引0=Lv1，索引1=Lv2 ...）。每级可完全不同，不走固定公式。")]
    public List<WeaponLevelData> levelConfigs = new List<WeaponLevelData>()
    {
        new WeaponLevelData()
    };

    [Header("表现与预制体 (Visuals & Prefabs)")]
    public GameObject projectilePrefab; // 对应的子弹预制体
    // public AudioClip fireSound;      // 预留音效接口

    public int MaxLevel => Mathf.Max(1, levelConfigs != null ? levelConfigs.Count : 0);

    /// <summary>
    /// 安全读取指定等级配置（等级从1开始）。
    /// 若索引越界，自动回退到最后一级，避免运行时空引用。
    /// </summary>
    public WeaponLevelData GetLevelConfig(int level)
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            // 兜底配置，避免运行时崩溃
            return new WeaponLevelData();
        }

        int index = Mathf.Clamp(level - 1, 0, levelConfigs.Count - 1);
        return levelConfigs[index];
    }
}
