using UnityEngine;

/// <summary>不依赖场景与碰撞的局部握柄姿势；调用方叠加挂点位移与瞄准旋转。</summary>
public readonly struct MeleeMotionPose
{
    public readonly Vector2 Position;
    public readonly float Angle;

    /// <summary>保存局部位置和绕握柄旋转角，单位分别为世界单位和度。</summary>
    public MeleeMotionPose(Vector2 position, float angle) { Position = position; Angle = angle; }
}

/// <summary>原作近战运动规则的 Unity 坐标实现；Y 向上，左右动作在出手时确定。</summary>
public static class MeleeAttackMotion
{
    public const float SweepHalfAngle = 162f;

    /// <summary>突刺使用完整范围；横挥取最大范围与至少 250 范围点目标距离中的较小值。</summary>
    public static float Distance(bool thrust, float range, float targetDistance)
    {
        return thrust ? range : Mathf.Min(range, Mathf.Max(250f * BrotatoStatRules.RangeUnits, targetDistance));
    }

    /// <summary>严格以瞄准角判左右；正上、正下归左侧，不依赖玩家行走朝向。</summary>
    public static bool FacesRight(float angle) { return Mathf.Abs(Mathf.DeltaAngle(0f, angle)) < 90f; }

    /// <summary>指数缓出并在端点归一，避免回收末尾残留偏移引起持武交接跳动。</summary>
    private static float ExpoOut(float value)
    {
        float t = Mathf.Clamp01(value);
        return t >= 1f ? 1f : t <= 0f ? 0f : 1f - Mathf.Pow(2f, -10f * t);
    }

    /// <summary>
    /// 计算完整局部姿势：前摇/回收缓出，横挥两段线性，突刺缓出。
    /// 初始姿势仅用于连续接手持武图，不改变主动段终点；无分配，可用于碰撞补采。
    /// </summary>
    public static MeleeMotionPose Evaluate(bool thrust, bool facesRight, float distance, float recoil,
        MeleeAttackTiming timing, float elapsed, MeleeMotionPose initial)
    {
        // Godot 的向下 Y 转为 Unity 向上 Y：朝右先向上蓄力，朝左取相反局部偏移。
        float side = facesRight ? 1f : -1f;
        Vector2 start = new Vector2(-recoil, thrust ? 0f : side * distance * .5f);
        float startAngle = thrust ? 0f : side * SweepHalfAngle;
        if (elapsed < timing.Windup)
        {
            float blend = ExpoOut(elapsed / timing.Windup);
            return new MeleeMotionPose(Vector2.Lerp(initial.Position, start, blend),
                Mathf.Lerp(initial.Angle, startAngle, blend));
        }

        float active = (elapsed - timing.Windup) / timing.Swing;
        Vector2 end = thrust ? new Vector2(distance, 0f) : new Vector2(-recoil, -side * distance * .5f);
        float endAngle = thrust ? 0f : -startAngle;
        if (active <= 1f)
        {
            if (thrust) return new MeleeMotionPose(Vector2.Lerp(start, end, ExpoOut(active)), 0f);
            Vector2 middle = new Vector2(.75f * distance, 0f);
            return active <= .5f
                ? new MeleeMotionPose(Vector2.Lerp(start, middle, active * 2f), Mathf.Lerp(startAngle, 0f, active * 2f))
                : new MeleeMotionPose(Vector2.Lerp(middle, end, active * 2f - 1f), Mathf.Lerp(0f, endAngle, active * 2f - 1f));
        }

        float recovery = ExpoOut((elapsed - timing.Windup - timing.Swing) / timing.Recovery);
        return new MeleeMotionPose(Vector2.Lerp(end, Vector2.zero, recovery), Mathf.Lerp(endAngle, 0f, recovery));
    }
}
