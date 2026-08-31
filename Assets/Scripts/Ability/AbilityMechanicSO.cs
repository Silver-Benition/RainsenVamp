using UnityEngine;

/// <summary>
/// 能力机制的静态工厂基类。
/// 派生资产只保存配置，必须为每一局创建独立 IAbilityMechanicRuntime。
/// </summary>
public abstract class AbilityMechanicSO : ScriptableObject
{
    /// <summary>
    /// 为一次能力获得创建独立运行时。
    /// </summary>
    /// <param name="context">玩家及其权威属性、生命依赖。</param>
    /// <param name="abilityData">拥有该机制的能力静态数据。</param>
    /// <param name="initialLevel">首次创建时的能力等级。</param>
    /// <returns>成功创建的本局机制；依赖无效时允许返回 null。</returns>
    public abstract IAbilityMechanicRuntime CreateRuntime(
        AbilityRuntimeContext context,
        AbilityDataSO abilityData,
        int initialLevel);
}
