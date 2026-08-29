using UnityEngine;

/// <summary>
/// 保存远程敌人的攻击参数，并在运行时提供统一的安全取值。
/// </summary>
[CreateAssetMenu(fileName = "RangedEnemyAttack", menuName = "Rainsen/Enemy/Ranged Enemy Attack Data")]
public sealed class RangedEnemyAttackDataSO : ScriptableObject
{
    [Header("Attack")]
    [SerializeField, Min(0f)] private float maxRange = 8f;
    [SerializeField, Min(0f)] private float firstShotDelay = 0.8f;
    [SerializeField, Min(0f)] private float cooldown = 2f;
    [SerializeField, Min(0f)] private float baseDamage = 12f;

    [Header("Projectile")]
    [SerializeField, Min(0f)] private float projectileSpeed = 5.5f;
    [SerializeField, Min(0f)] private float projectileLifetime = 6f;
    [SerializeField] private GameObject projectilePrefab;

    /// <summary>
    /// 获取最大攻击距离。
    /// </summary>
    public float MaxRange => Mathf.Max(0f, maxRange);

    /// <summary>
    /// 获取池化生成后的首次发射延迟。
    /// </summary>
    public float FirstShotDelay => Mathf.Max(0f, firstShotDelay);

    /// <summary>
    /// 获取成功发射后的攻击冷却。
    /// </summary>
    public float Cooldown => Mathf.Max(0f, cooldown);

    /// <summary>
    /// 获取弹体基础伤害。
    /// </summary>
    public float BaseDamage => Mathf.Max(0f, baseDamage);

    /// <summary>
    /// 获取弹体速度。
    /// </summary>
    public float ProjectileSpeed => Mathf.Max(0f, projectileSpeed);

    /// <summary>
    /// 获取弹体寿命。
    /// </summary>
    public float ProjectileLifetime => Mathf.Max(0f, projectileLifetime);

    /// <summary>
    /// 获取远程攻击使用的弹体预制体。
    /// </summary>
    public GameObject ProjectilePrefab => projectilePrefab;
}
