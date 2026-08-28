using UnityEngine;

/// <summary>可被玩家磁吸范围捕获并飞向指定目标的拾取物。</summary>
public interface IMagneticPickup
{
    /// <summary>开始向玩家目标飞行；重复调用必须保持幂等。</summary>
    void StartFlyingTowards(Transform player);
}
