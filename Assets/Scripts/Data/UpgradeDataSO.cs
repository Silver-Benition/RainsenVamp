using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 某次升级的自定义描述；等级从 1 开始，留空时由 UI 自动生成数值差异。
/// </summary>
[System.Serializable]
public sealed class LevelUpgradeDesc
{
    [Tooltip("升到第几级时使用此描述，例如升到 Lv.2 填 2。")]
    public int level;
    [TextArea] public string customDesc;
}

/// <summary>
/// 升级候选的 UI 数据与奖励引用。武器运行类型统一由 WeaponDataSO 决定。
/// </summary>
[CreateAssetMenu(fileName = "NewUpgrade", menuName = "GameData/Upgrade Data")]
public sealed class UpgradeDataSO : ScriptableObject
{
    [Header("系统识别")]
    [Tooltip("升级内容的稳定 ID。存档、Seal 与 Banish 只依赖该字段，不依赖本地化名称。")]
    public string upgradeID;

    [Min(0f), Tooltip("候选抽取的基础权重。0 表示禁用该候选。")]
    public float baseWeight = 100f;

    [Min(0f), Tooltip("Luck 对该候选的影响指数。普通内容填 0，越稀有可配置得越高。")]
    public float luckInfluence;

    [Header("UI 表现")]
    public string upgradeName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("自定义升级描述")]
    [Tooltip("未填写的等级由升级 UI 自动生成数值差异。")]
    public List<LevelUpgradeDesc> customLevelDescs = new List<LevelUpgradeDesc>();

    [Header("奖励内容")]
    public WeaponDataSO weaponToGrant;

    /// <summary>读取升级稳定 ID；旧资产缺少 ID 时使用资产名兼容，避免当前内容突然失效。</summary>
    public string GetStableId()
    {
        return !string.IsNullOrWhiteSpace(upgradeID) ? upgradeID : name;
    }
}
