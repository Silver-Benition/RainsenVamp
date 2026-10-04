using UnityEngine;

/// <summary>持武与攻击共用的素材几何；尺寸沿校正后的攻击轴测量，避免斜向素材切换时跳变。</summary>
public static class WeaponVisualGeometry
{
    /// <summary>测量素材包围盒在攻击轴上的投影长度，空素材使用单位长度。</summary>
    public static float ProjectedLength(Sprite sprite, float angle)
    {
        Vector2 size = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
        float radians = angle * Mathf.Deg2Rad;
        return Mathf.Max(.01f, Mathf.Abs(Mathf.Cos(radians)) * size.x + Mathf.Abs(Mathf.Sin(radians)) * size.y);
    }

    /// <summary>把旧版基础范围换算为原有显示长度，供资产迁移保留初始尺寸；不得传入动态范围。</summary>
    public static float MeleeLength(float range) { return Mathf.Max(.05f, range * .6f - .05f); }

    /// <summary>旧版配置几何换算；现行近战休息位置改用固定 heldSize 与 meleeGripOffset。</summary>
    public static float MeleeCenter(float range) { return .05f + MeleeLength(range) * .5f; }

    /// <summary>把发射时的同款武器素材调整为持武长度，整体同步碰撞尺寸；枪弓弹药维持各自素材。</summary>
    public static void MatchThrownSize(Transform root, SpriteRenderer renderer, WeaponDataSO data, float length)
    {
        if (renderer == null || data == null || renderer.sprite != data.icon) return;
        float current = ProjectedLength(renderer.sprite, data.visualAngleOffset) * Mathf.Abs(renderer.transform.lossyScale.x);
        float factor = length / Mathf.Max(.001f, current);
        root.localScale = new Vector3(root.localScale.x * factor, root.localScale.y * factor, root.localScale.z);
    }
}
