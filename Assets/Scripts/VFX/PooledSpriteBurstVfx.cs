using UnityEngine;

/// <summary>
/// 可复用的池化 Sprite 爆闪表现。
/// 通过缩放、旋转与淡出完成低频奖励反馈，不参与任何奖励或伤害结算。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class PooledSpriteBurstVfx : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0.05f)] private float duration = 0.45f;
    [SerializeField, Min(0.01f)] private float startScale = 0.35f;
    [SerializeField, Min(0.01f)] private float endScale = 1.55f;
    [SerializeField] private float rotationDegrees = 45f;
    [SerializeField] private Color startColor = new Color(1f, 1f, 1f, 0.95f);

    private SpriteRenderer _spriteRenderer;
    private GameObject _prefabReference;
    private float _elapsed;
    private bool _playing;

    /// <summary>缓存渲染组件，避免播放阶段重复 GetComponent。</summary>
    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>从初始缩放与颜色开始一次完整爆闪播放。</summary>
    public void Play()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        _elapsed = 0f;
        _playing = true;
        ApplyVisualState(0f);
    }

    /// <summary>按游戏时间推进缩放、旋转和淡出；暂停时与战斗同步停止。</summary>
    private void Update()
    {
        if (!_playing)
        {
            return;
        }

        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.05f, duration));
        ApplyVisualState(progress);
        if (progress >= 1f)
        {
            ReleaseToPool();
        }
    }

    /// <summary>把归一化进度映射为爆闪视觉状态，不产生逐帧托管分配。</summary>
    private void ApplyVisualState(float progress)
    {
        float clampedProgress = Mathf.Clamp01(progress);
        float easedProgress = 1f - (1f - clampedProgress) * (1f - clampedProgress);
        float scale = Mathf.Lerp(startScale, endScale, easedProgress);
        transform.localScale = Vector3.one * scale;
        transform.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees * clampedProgress);

        Color color = startColor;
        color.a *= 1f - clampedProgress;
        _spriteRenderer.color = color;
    }

    /// <summary>接收 PoolManager 创建实例时使用的稳定 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>停用时恢复确定性初始状态，保证下一次池取出不会继承旧帧。</summary>
    private void OnDisable()
    {
        _elapsed = 0f;
        _playing = false;
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = startColor;
        }
    }

    /// <summary>按原始 Prefab 键归还对象池；独立测试实例则安全停用。</summary>
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
