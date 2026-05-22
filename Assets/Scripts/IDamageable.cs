using UnityEngine;

/// <summary>
/// 可受击接口。
/// 所有能承受伤害的实体（怪物、Boss、可破坏物）必须实现此接口。
/// 配合 TryGetComponent 使用，彻底解耦攻击方与受击方。
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// 承受伤害（简易版，默认非暴击）。
    /// </summary>
    /// <param name="damage">伤害值</param>
    void TakeDamage(float damage);

    /// <summary>
    /// 承受伤害（完整版，支持暴击标记）。
    /// 默认实现委托给简易版，保持向后兼容。
    /// </summary>
    /// <param name="damage">伤害值</param>
    /// <param name="isCritical">是否暴击（影响飘字颜色与大小）</param>
    void TakeDamage(float damage, bool isCritical)
    {
        // C# 8.0 接口默认实现：未覆写的实现者自动走这里
        TakeDamage(damage);
    }
}
