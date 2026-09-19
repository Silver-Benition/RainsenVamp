using System;
using UnityEngine;

/// <summary>宝箱奖励处理方式；不包含重复刷新或重掷。</summary>
public enum CrateRewardAction { Take, Recycle, Banish }

/// <summary>当前宝箱的固定报价快照；展示期间不会重新抽取或改变回收价值。</summary>
public sealed class RunCrateReward
{
    public RunShopProduct Product { get; }
    public int RecycleValue { get; }
    /// <summary>按本波道具价格计算向下取整的回收价值；长整型价格避免溢出。</summary>
    public RunCrateReward(RunShopProduct product, int wave, float ratio)
    {
        Product = product;
        double price = Math.Min(int.MaxValue, (long)product.basePrice + Math.Max(0, wave) * 2L);
        RecycleValue = (int)Math.Floor(price * Mathf.Clamp01(ratio));
    }
}
