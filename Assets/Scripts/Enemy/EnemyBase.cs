using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class EnemyBase : MonoBehaviour, IDamageable, IPoolable
{
    public EnemyDataSO enemyData;

    private float currentHealth;
    private Transform playerTransform;
    private Rigidbody2D rb;
    private GameObject prefabReference;

    // 受击反馈组件缓存（Awake 时自动获取，不强制依赖）
    private HitFlash hitFlash;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 尝试获取闪白组件（可能挂在自身或子物体上）
        hitFlash = GetComponent<HitFlash>();
        if (hitFlash == null)
        {
            hitFlash = GetComponentInChildren<HitFlash>();
        }
    }

    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    private void OnEnable()
    {
        if (enemyData != null)
        {
            currentHealth = enemyData.maxHealth;
        }

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }
    }

    private void FixedUpdate()
    {
        if (playerTransform == null || enemyData == null) return;

        // 计算朝向玩家的方向
        Vector2 direction = (playerTransform.position - transform.position).normalized;

        // 使用 Rigidbody2D.MovePosition 移动（适合处理物理挤压）
        Vector2 newPosition = rb.position + direction * enemyData.moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(newPosition);

        // 美术翻转（适配默认朝左的素材）
        if (direction.x != 0)
        {
            transform.localScale = new Vector3(direction.x > 0 ? -1 : 1, 1, 1);
        }
    }

    // =====================================================================
    // IDamageable 实现
    // =====================================================================

    /// <summary>
    /// 简易版受击（向后兼容，默认非暴击）。
    /// </summary>
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, false);
    }

    /// <summary>
    /// 完整版受击：扣血 + 触发受击闪白 + 弹出伤害飘字。
    /// </summary>
    /// <param name="damage">伤害值</param>
    /// <param name="isCritical">是否暴击</param>
    public void TakeDamage(float damage, bool isCritical)
    {
        currentHealth -= damage;

        // --- 受击闪白 ---
        if (hitFlash != null)
        {
            hitFlash.TriggerFlash();
        }

        // --- 伤害飘字 ---
        if (DamagePopupManager.Instance != null)
        {
            DamagePopupManager.Instance.Show(damage, transform.position, isCritical);
        }

        // --- 死亡判定 ---
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // 掉落经验球
        if (enemyData.dropExpPrefab != null)
        {
            PoolManager.Instance.Spawn(enemyData.dropExpPrefab, transform.position, Quaternion.identity);
        }

        // 归还对象池
        PoolManager.Instance.Release(prefabReference, gameObject);
    }
}
