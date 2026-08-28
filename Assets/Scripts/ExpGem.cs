using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class ExpGem : MonoBehaviour, IPoolable, IMagneticPickup
{
    [Header("配置")]
    public float expValue = 1f;       // 提供的经验值
    public float baseFlySpeed = 5f;   // 初始飞行速度
    public float acceleration = 15f;  // 飞行加速度（营造越飞越快的吸附感）

    private GameObject prefabReference;
    private Transform targetPlayer;
    private bool isFlying = false;
    private float currentSpeed;

    /// <summary>保存对象池使用的原始经验球 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    /// <summary>每次从对象池取出时重置磁吸目标与移动速度。</summary>
    private void OnEnable()
    {
        isFlying = false;
        targetPlayer = null;
        currentSpeed = baseFlySpeed;
    }

    /// <summary>
    /// 被玩家磁铁捕获时调用
    /// </summary>
    public void StartFlyingTowards(Transform player)
    {
        if (isFlying) return; // 防止重复触发
        targetPlayer = player;
        isFlying = true;
    }

    /// <summary>被磁吸后按加速度向玩家靠近；静止经验球不会执行位移计算。</summary>
    private void Update()
    {
        if (!isFlying || targetPlayer == null) return;

        // 加速飞行逻辑
        currentSpeed += acceleration * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, targetPlayer.position, currentSpeed * Time.deltaTime);
    }

    /// <summary>碰到玩家本体时授予经验并归还对象池。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 只有碰到挂有 PlayerStats 的物体（即玩家本体），才会被吸收
        if (collision.TryGetComponent<PlayerStats>(out var stats))
        {
            stats.AddExp(expValue);
            PoolManager.Instance.Release(prefabReference, gameObject);
        }
    }
}
