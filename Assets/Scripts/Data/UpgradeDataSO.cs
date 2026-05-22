using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum WeaponRuntimeType
{
    Projectile = 0,
    Aura = 1
}

/// <summary>
/// 某一升级等级的自定义描述。level 填升到几级时显示（从1开始）。
/// 留空则自动生成数值 diff 文本。
/// </summary>
[System.Serializable]
public class LevelUpgradeDesc
{
    [Tooltip("升到第几级时使用此描述（从1开始，例如升到Lv.2填2）")]
    public int level;
    [TextArea] public string customDesc;
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "GameData/Upgrade Data")]
public class UpgradeDataSO : ScriptableObject
{
    [Header("UI 表现")]
    public string upgradeName;
    [TextArea] public string description;
    public Sprite icon; // 升级图标

    [Header("自定义升级描述（可选，留空则自动生成数值文本）")]
    [Tooltip("按等级填写自定义描述，未填写的等级自动生成数值变化文本。")]
    public List<LevelUpgradeDesc> customLevelDescs = new List<LevelUpgradeDesc>();

    [Header("奖励内容")]
    // 目前我们先只做"获得新武器"，后续可以扩展为"提升血量"、"提升移速"等枚举
    public WeaponDataSO weaponToGrant;
    [Tooltip("决定发放武器时挂载哪种运行时脚本。")]
    public WeaponRuntimeType runtimeType = WeaponRuntimeType.Projectile;
}
