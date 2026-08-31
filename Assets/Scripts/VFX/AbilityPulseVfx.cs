using UnityEngine;

/// <summary>
/// 反击脉冲的池化纯表现组件。
/// 伤害已由机制运行时结算，本组件只负责缩放、淡出和按原 Prefab 键回池。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class AbilityPulseVfx : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0.05f)] private float duration = 0.35f;
    [SerializeField, Range(0.05f, 1f)] private float startScaleRatio = 0.35f;
    [SerializeField] private Color startColor = new Color(1f, 1f, 1f, 0.9f);

    private SpriteRenderer _spriteRenderer;
    private GameObject _prefabReference;
    private float _elapsed;
    private float _targetDiameter;
    private bool _playing;

    /// <summary>缓存渲染组件，避免表现更新阶段重复 GetComponent。</summary>
    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 每次池取出后以指定世界半径开始一次扩张淡出表现。
    /// 半径会被转换为 Sprite 的目标世界直径，逻辑伤害范围仍由机制层独立结算。
    /// </summary>
    public void Play(float radius)
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        _elapsed = 0f;
        _playing = true;
        _targetDiameter = Mathf.Max(0.01f, radius * 2f);
        ApplyVisualState(0f);
    }

    /// <summary>按持续时间推进扩张淡出；暂停时 deltaTime 为零，因此表现与战斗同步停止。</summary>
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

    /// <summary>
    /// 将归一化进度映射为缓出的直径和线性透明度。
    /// 仅写入已有 Transform 与 SpriteRenderer，不在逐帧路径产生托管分配。
    /// </summary>
    private void ApplyVisualState(float progress)
    {
        float clampedProgress = Mathf.Clamp01(progress);
        float inverseProgress = 1f - clampedProgress;
        float easedProgress = 1f - inverseProgress * inverseProgress;
        float clampedStartRatio = Mathf.Clamp(startScaleRatio, 0.05f, 1f);
        float diameter = Mathf.Lerp(_targetDiameter * clampedStartRatio, _targetDiameter, easedProgress);
        transform.localScale = Vector3.one * diameter;

        Color color = startColor;
        color.a *= inverseProgress;
        _spriteRenderer.color = color;
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
        _targetDiameter = 0f;
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
