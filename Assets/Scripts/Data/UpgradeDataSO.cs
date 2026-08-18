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
    [Header("UI 表现")]
    public string upgradeName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("自定义升级描述")]
    [Tooltip("未填写的等级由升级 UI 自动生成数值差异。")]
    public List<LevelUpgradeDesc> customLevelDescs = new List<LevelUpgradeDesc>();

    [Header("奖励内容")]
    public WeaponDataSO weaponToGrant;
}
