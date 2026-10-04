using System;
using UnityEngine;

/// <summary>普通二十波的原作价格公式；商品基础价仍由项目目录逐档配置，无账号货币副作用。</summary>
public static class RunEconomyRules
{
    /// <summary>基础价加波次通胀后截断，最低一材料；整数运算避免 0.1 浮点误差，多乘项使用长整数。</summary>
    public static int Price(int basePrice, int wave)
    {
        long value = Math.Max(1, basePrice), progress = Math.Max(0, wave);
        return (int)Math.Min(int.MaxValue, (10 * value + 10 * progress + value * progress) / 10);
    }

    /// <summary>同波付费重投逐次加价；计数由商店和属性奖励分别维护，免费次数不推进此计数。</summary>
    public static int RerollPrice(int wave, int paidRerolls)
    {
        long progress = Math.Max(1, wave), step = Math.Max(1, progress * 2 / 5);
        return (int)Math.Min(int.MaxValue, progress * 3 / 4 + step * (1L + Math.Max(0, paidRerolls)));
    }

    /// <summary>按当前波商品价回收，向下取整且至少一材料；一材料基础商品固定回收一，保持原作特殊边界。</summary>
    public static int Recycle(int basePrice, int wave, float ratio)
    {
        if (basePrice <= 1) return 1;
        int price = Price(basePrice, wave);
        return Math.Min(price, Math.Max(1, (int)Math.Floor(price * (double)Mathf.Clamp01(ratio))));
    }
}
