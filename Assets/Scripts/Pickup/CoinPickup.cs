using UnityEngine;

/// <summary>池化金币拾取物；磁吸移动属于表现，最终价值在碰到玩家时按 Greed 结算。</summary>
[RequireComponent(typeof(Collider2D), typeof(MagneticPickupMotion))]
public sealed class CoinPickup : MonoBehaviour, IPoolable
{
    private GameObject _prefabReference;
    private int _baseValue = 1;
    private bool _consumed;

    /// <summary>保存对象池使用的原始金币 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>敌人生成金币后注入基础价值；取整与 Greed 计算延迟到实际拾取。</summary>
    public void ConfigureValue(int baseValue)
    {
        _baseValue = Mathf.Max(1, baseValue);
    }

    /// <summary>池取出时重置本生命周期的金币基础价值。</summary>
    private void OnEnable()
    {
        _baseValue = 1;
        _consumed = false;
    }

    /// <summary>碰到玩家时按最终 Greed 结算金币，并归还对象池。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!RoundController.AllowsCombat || !collision.TryGetComponent(out PlayerStats playerStats))
        {
            return;
        }

        Consume(playerStats);
    }

    /// <summary>波末自动收取金币仍走账号金币统计，不进入材料钱包或储备。</summary>
    public bool CollectForSettlement(PlayerStats player)
    {
        if (!RoundController.Enabled || RoundController.Instance.Phase != RoundPhase.Settling) return false;
        return Consume(player);
    }

    /// <summary>碰撞与结算共享单次消费守卫，事件重入和已回池对象不会重复记账。</summary>
    private bool Consume(PlayerStats playerStats)
    {
        if (_consumed || !gameObject.activeInHierarchy || playerStats == null) return false;
        _consumed = true;
        int awardedGold = Mathf.Max(0, Mathf.RoundToInt(_baseValue * playerStats.Greed));
        RunState.GetOrCreate(playerStats)?.AddGold(awardedGold);
        ReleaseToPool();
        return true;
    }

    /// <summary>通过原始 Prefab 键归还金币；缺少池依赖时禁用对象作为安全降级。</summary>
    private void ReleaseToPool()
    {
        if (PoolManager.Instance != null && _prefabReference != null)
        {
            PoolManager.Instance.Release(_prefabReference, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
