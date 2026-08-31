using UnityEngine;

/// <summary>
/// 首领径向弹幕预警的池化表现。
/// 只负责 Sprite/缩放/生命周期，不参与伤害与阶段逻辑；WorldEnemySimulation 负责世界归属显隐。
/// </summary>
public sealed class BossWarningVfx : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0.05f)] private float duration = 0.45f;
    [SerializeField, Min(0.01f)] private float startScaleRatio = 0.35f;
    [SerializeField] private Color startColor = new Color(1f, 0.24f, 0.08f, 0.85f);

    private GameObject _prefabReference;
    private WorldEnemySimulation _worldSimulation;
    private SpriteRenderer _renderer;
    private Vector3 _baseScale;
    private Color _baseColor;
    private float _remaining;

    /// <summary>缓存表现组件和 Prefab 初始值，避免池化缩放与透明度累乘。</summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        if (_renderer == null)
        {
            _renderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        _baseScale = transform.localScale;
        _baseColor = _renderer != null ? _renderer.color : Color.white;
    }

    /// <summary>对象池取出时恢复短生命周期初始状态。</summary>
    private void OnEnable()
    {
        _remaining = Mathf.Max(0.05f, duration);
        transform.localScale = _baseScale * Mathf.Max(0.01f, startScaleRatio);
        if (_renderer != null)
        {
            _renderer.color = startColor;
        }
    }

    /// <summary>回池时恢复引用、缩放和颜色，避免下一次预警继承旧表现。</summary>
    private void OnDisable()
    {
        _remaining = 0f;
        transform.localScale = _baseScale;
        if (_renderer != null)
        {
            _renderer.color = _baseColor;
        }

        _worldSimulation = null;
    }

    /// <summary>保存原始 Prefab 引用，供预警结束时归还对象池。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>绑定预警所属世界，供世界切换时隐藏 Renderer 但保持生命周期推进。</summary>
    public void SetWorldSimulation(WorldEnemySimulation simulation)
    {
        _worldSimulation = simulation;
    }

    /// <summary>推进淡出动画；预警只占用极短生命周期，不进入战斗统计。</summary>
    private void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
        {
            ReleaseToPool();
            return;
        }

        float normalized = Mathf.Clamp01(_remaining / Mathf.Max(0.05f, duration));
        transform.localScale = _baseScale * Mathf.Lerp(1f, Mathf.Max(0.01f, startScaleRatio), normalized);
        if (_renderer != null)
        {
            Color color = _renderer.color;
            color.a = _baseColor.a * normalized;
            _renderer.color = color;
        }
    }

    /// <summary>将预警实例归还对象池；缺失池时仅禁用作为安全降级。</summary>
    private void ReleaseToPool()
    {
        if (!gameObject.activeSelf)
        {
            return;
        }

        if (_prefabReference != null && PoolManager.Instance != null)
        {
            PoolManager.Instance.Release(_prefabReference, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
