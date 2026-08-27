using UnityEngine;

/// <summary>玩家拾取磁吸触发器；半径由角色最终 Magnet 属性驱动。</summary>
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerMagnet : MonoBehaviour
{
    private CircleCollider2D _trigger;
    private PlayerStats _playerStats;

    /// <summary>缓存触发器与父级玩家属性，并应用初始半径。</summary>
    private void Awake()
    {
        _trigger = GetComponent<CircleCollider2D>();
        _playerStats = GetComponentInParent<PlayerStats>(true);
        ApplyMagnetRadius();
    }

    /// <summary>订阅低频属性重算通知，避免每帧重复设置 Collider。</summary>
    private void OnEnable()
    {
        if (_playerStats == null)
        {
            _playerStats = GetComponentInParent<PlayerStats>(true);
        }

        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= ApplyMagnetRadius;
            _playerStats.StatsChanged += ApplyMagnetRadius;
        }

        ApplyMagnetRadius();
    }

    /// <summary>取消属性订阅，避免重复启用后积累监听。</summary>
    private void OnDisable()
    {
        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= ApplyMagnetRadius;
        }
    }

    /// <summary>经验球进入最终磁吸范围时，令其飞向玩家根节点。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.TryGetComponent<ExpGem>(out var gem))
        {
            gem.StartFlyingTowards(transform.parent);
        }
    }

    /// <summary>把缓存的最终 Magnet 世界单位值同步到圆形触发器。</summary>
    private void ApplyMagnetRadius()
    {
        if (_trigger == null)
        {
            _trigger = GetComponent<CircleCollider2D>();
        }

        if (_trigger != null && _playerStats != null)
        {
            _trigger.radius = Mathf.Max(0f, _playerStats.Magnet);
        }
    }
}
