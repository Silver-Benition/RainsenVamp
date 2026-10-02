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

    /// <summary>近战剑身固定占射程六成，突刺和挥击共用；余下四成由手臂前伸提供。</summary>
    public static float MeleeLength(float range) { return Mathf.Max(.05f, range * .6f - .05f); }

    /// <summary>近战休息姿势把握柄放在挂点外侧，贴图中心随剑身长度移动。</summary>
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
