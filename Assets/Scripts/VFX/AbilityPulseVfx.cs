using UnityEngine;

/// <summary>
/// 反击脉冲的池化纯表现组件。
/// 伤害已由机制运行时结算，本组件只负责缩放、淡出和按原 Prefab 键回池。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class AbilityPulseVfx : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0.05f)] private float duration = 0.3f;
    [SerializeField] private Color startColor = new Color(0.35f, 0.9f, 1f, 0.75f);

    private SpriteRenderer _spriteRenderer;
    private GameObject _prefabReference;
    private float _elapsed;
    private bool _playing;

    /// <summary>缓存渲染组件，避免表现更新阶段重复 GetComponent。</summary>
    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>每次池取出后以指定世界半径开始一次淡出表现。</summary>
    public void Play(float radius)
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        _elapsed = 0f;
        _playing = true;
        transform.localScale = Vector3.one * Mathf.Max(0.01f, radius * 2f);
        _spriteRenderer.color = startColor;
    }

    /// <summary>按缩放时间推进淡出；暂停时 deltaTime 为零，因此表现与战斗同步停止。</summary>
    private void Update()
    {
        if (!_playing)
        {
            return;
        }

        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.05f, duration));
        Color color = startColor;
        color.a *= 1f - progress;
        _spriteRenderer.color = color;
        if (progress >= 1f)
        {
            ReleaseToPool();
        }
    }

    /// <summary>接收 PoolManager 创建实例时提供的原始 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>停用时清理上一次生命周期状态，等待下一次池取出重新初始化。</summary>
    private void OnDisable()
    {
        _playing = false;
        _elapsed = 0f;
        transform.localScale = Vector3.one;
    }

    /// <summary>以稳定 Prefab 键归还对象池；异常独立实例则安全停用。</summary>
    private void ReleaseToPool()
    {
        _playing = false;
        if (_prefabReference != null && PoolManager.Instance != null)
        {
            PoolManager.Instance.Release(_prefabReference, gameObject);
            return;
        }

        gameObject.SetActive(false);
    }
}
