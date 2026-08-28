using UnityEngine;

/// <summary>池化宝箱拾取物；拾取后请求升级系统即时授予一个合法武器奖励。</summary>
[RequireComponent(typeof(Collider2D))]
public sealed class TreasureChestPickup : MonoBehaviour, IPoolable
{
    private GameObject _prefabReference;
    private bool _consumed;

    /// <summary>保存对象池使用的原始宝箱 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>池取出时清除上次已消费标记。</summary>
    private void OnEnable()
    {
        _consumed = false;
    }

    /// <summary>玩家接触后申请一次即时奖励；没有合法奖励时仍回收宝箱，避免重复碰撞。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_consumed || !collision.TryGetComponent(out PlayerStats _))
        {
            return;
        }

        _consumed = true;
        if (LevelUpManager.Instance != null)
        {
            UpgradeDataSO reward = LevelUpManager.Instance.GrantRandomChestReward();
            if (reward != null)
            {
                Debug.Log($"[TreasureChest] 获得奖励：{reward.GetStableId()}", this);
            }
        }

        ReleaseToPool();
    }

    /// <summary>通过原始 Prefab 键归还宝箱；缺少池依赖时禁用对象作为安全降级。</summary>
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
