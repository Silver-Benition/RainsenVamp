using TMPro;
using UnityEngine;

/// <summary>共享临时伤害属性图集；只绑定展示资源，不参与属性计算。</summary>
public static class StatIconPresentation
{
    private static TMP_SpriteAsset _icons;
    /// <summary>按需加载项目内持久化图集，所有详情文本共用同一材质。</summary>
    public static TMP_SpriteAsset Icons => _icons != null ? _icons : (_icons = Resources.Load<TMP_SpriteAsset>("UI/StatDamageIcons"));

    /// <summary>为详情文本绑定图标；资源缺失时格式器自动使用属性名称。</summary>
    public static void Bind(TMP_Text label) { label.spriteAsset = Icons; }

    /// <summary>以稳定序号输出内嵌图标，纯文本环境或资源缺失时返回本地化名称。</summary>
    public static string Token(PlayerStatType stat, bool richText)
    {
        int index = stat == PlayerStatType.MeleeDamage ? 0 : stat == PlayerStatType.RangedDamage ? 1 : 2;
        return richText && Icons != null ? "<sprite index=" + index + ">" : PlayerStatPresentation.GetDisplayName(stat);
    }
}
