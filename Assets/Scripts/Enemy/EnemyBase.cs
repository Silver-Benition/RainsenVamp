using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class EnemyBase : MonoBehaviour, IDamageable, IPoolable
{
    public EnemyDataSO enemyData;

    private float currentHealth;
    private Transform playerTransform;
    private Rigidbody2D rb;
    private GameObject prefabReference;

    private HitFlash hitFlash;

    /// <summary>缓存移动刚体与可选受击表现组件，避免在战斗热路径中重复查找。</summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        hitFlash = GetComponent<HitFlash>();
        if (hitFlash == null)
        {
            hitFlash = GetComponentInChildren<HitFlash>();
        }
    }

    /// <summary>保存对象池使用的原始 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    /// <summary>对象池取出时重置生命、速度和玩家目标。</summary>
    private void OnEnable()
    {
        if (enemyData != null)
        {
            currentHealth = enemyData.maxHealth;
        }

        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }
    }

    /// <summary>对象池回收时清空刚体动量，避免下一次取出继承旧速度。</summary>
    private void OnDisable()
    {
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    /// <summary>
    /// 计算朝向玩家的期望速度并交给 Dynamic Rigidbody2D。
    /// Physics2D 随后求解 Enemy 间实体接触，使敌群保持追踪的同时互相滑开。
    /// </summary>
    private void FixedUpdate()
    {
        if (rb == null || playerTransform == null || enemyData == null)
        {
            if (rb != null) rb.velocity = Vector2.zero;
            return;
        }

        Vector2 direction = (playerTransform.position - transform.position).normalized;
        rb.velocity = direction * Mathf.Max(0f, enemyData.moveSpeed);

        // 美术翻转（适配默认朝左的素材）
        if (direction.x != 0)
        {
            transform.localScale = new Vector3(direction.x > 0 ? -1 : 1, 1, 1);
        }
    }

    /// <summary>
    /// 与玩家持续接触时请求一次接触伤害。
    /// PlayerHealth 负责无敌帧，多敌人或多 Collider 同帧接触也只会生效一次。
    /// </summary>
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (enemyData == null || enemyData.collisionDamage <= 0f)
        {
            return;
        }

        if (DamageTargetFilter.TryGetPlayerDamageable(collision.collider, out IDamageable player))
        {
            player.TakeDamage(enemyData.collisionDamage);
        }
    }

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
        if (damage <= 0f || currentHealth <= 0f)
        {
            return;
        }

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

    /// <summary>生成经验掉落并把敌人归还对应对象池。</summary>
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
