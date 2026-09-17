using UnityEngine;

/// <summary>有限竞技场的几何权威，提供常数开销边界约束与低频安全刷怪查询。</summary>
public sealed class RoundArena : MonoBehaviour
{
    public Vector2 size = new Vector2(24, 16);
    public static RoundArena Instance { get; private set; }
    private static readonly Collider2D[] SpawnHits = new Collider2D[32];

    /// <summary>登记场景竞技场，不生成战斗对象。</summary>
    private void Awake() { Instance = this; }
    /// <summary>场景卸载时释放引用。</summary>
    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>把实体中心约束到边界以内，预留碰撞半径；不改变已有 Layer Matrix。</summary>
    public static Vector2 Clamp(Vector2 position, float radius)
    {
        if (!RoundController.Enabled || Instance == null) return position;
        Vector2 center = Instance.transform.position;
        Vector2 half = Instance.size * .5f - Vector2.one * radius;
        return new Vector2(Mathf.Clamp(position.x, center.x - half.x, center.x + half.x),
            Mathf.Clamp(position.y, center.y - half.y, center.y + half.y));
    }

    /// <summary>限制下一物理步的位置和速度，高速移动也不会穿出竞技场。</summary>
    public static Vector2 ConstrainVelocity(Rigidbody2D body, Vector2 velocity, float radius)
    {
        if (!RoundController.Enabled || Instance == null) return velocity;
        body.position = Clamp(body.position, radius);
        return (Clamp(body.position + velocity * Time.fixedDeltaTime, radius) - body.position) / Time.fixedDeltaTime;
    }

    /// <summary>最多尝试十六个场内位置；避开玩家、实体和掩体，饱和时延后生成。</summary>
    public static bool TrySpawnPosition(Vector3 player, out Vector3 position)
    {
        position = player;
        if (Instance == null) return false;
        Vector2 half = Instance.size * .5f - Vector2.one;
        Vector2 center = Instance.transform.position;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 candidate = center + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            if (((Vector2)player - candidate).sqrMagnitude < 16f) continue;
            int count = Physics2D.OverlapCircleNonAlloc(candidate, .65f, SpawnHits);
            bool blocked = count == SpawnHits.Length;
            for (int i = 0; i < count && !blocked; i++)
                if (SpawnHits[i] != null && !SpawnHits[i].isTrigger) blocked = true;
            if (blocked) continue;
            position = candidate; return true;
        }
        return false;
    }
}
