using System;
using UnityEngine;

/// <summary>一个标签在指定装备数量下提供的总加成；各档互相替换，不累加低档。</summary>
[Serializable]
public sealed class WeaponSetTier
{
    public PlayerStatModifier[] modifiers = Array.Empty<PlayerStatModifier>();
}

/// <summary>独立于伤害系数与攻击形态的武器分类，同时驱动商店偏好与羁绊。</summary>
[CreateAssetMenu(fileName = "WeaponSet", menuName = "GameData/Weapon Set")]
public sealed class WeaponSetSO : ScriptableObject
{
    public string setId;
    public string nameKey;
    public string displayName;
    [Tooltip("依次配置 2、3、4、5、6 把时的总加成；一把只有标签，不提供羁绊属性。")]
    public WeaponSetTier[] tiers = new WeaponSetTier[5];

    /// <summary>获取不依赖显示语言的稳定身份；旧测试资产以名称回退。</summary>
    public string GetStableId() => string.IsNullOrWhiteSpace(setId) ? name : setId;

    /// <summary>通过既有翻译入口读取标签名。</summary>
    public string GetDisplayName() => RoundShopPresentation.Text(nameKey, displayName);

    /// <summary>取当前数量对应的整档加成；不足两把无收益，超过六把封顶。</summary>
    public PlayerStatModifier[] GetModifiers(int count)
    {
        if (count < 2 || tiers == null) return Array.Empty<PlayerStatModifier>();
        int index = Mathf.Min(count, 6) - 2;
        return index < tiers.Length && tiers[index] != null && tiers[index].modifiers != null
            ? tiers[index].modifiers : Array.Empty<PlayerStatModifier>();
    }
}
