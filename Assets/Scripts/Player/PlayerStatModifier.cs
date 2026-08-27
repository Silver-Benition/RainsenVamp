using System;
using UnityEngine;

/// <summary>属性修改值的计算方式。</summary>
public enum PlayerStatModifierMode
{
    Flat = 0,
    AdditivePercent = 1,
    Multiplicative = 2
}

/// <summary>
/// 单项玩家属性修改器。
/// 来源身份由 PlayerStats 的 sourceId 管理，因此同一来源可以同时提供多项修改。
/// </summary>
[Serializable]
public struct PlayerStatModifier
{
    [SerializeField] private PlayerStatType statType;
    [SerializeField] private PlayerStatModifierMode mode;
    [SerializeField] private float value;

    /// <summary>被修改的属性类型。</summary>
    public PlayerStatType StatType => statType;

    /// <summary>修改器参与最终计算的方式。</summary>
    public PlayerStatModifierMode Mode => mode;

    /// <summary>修改值；百分比使用小数，例如 0.2 表示增加 20%。</summary>
    public float Value => value;

    /// <summary>创建一项不可变语义的运行时属性修改。</summary>
    public PlayerStatModifier(
        PlayerStatType statType,
        PlayerStatModifierMode mode,
        float value)
    {
        this.statType = statType;
        this.mode = mode;
        this.value = value;
    }
}
