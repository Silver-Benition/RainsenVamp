using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家受击表现组件。
/// 监听 PlayerHealth 的有效伤害事件，通过 SpriteRenderer 顶点色短暂染色，
/// 保留玩家现有的 URP Sprite-Lit 材质与 2D 光照表现。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerDamageFeedback : MonoBehaviour
{
    [Header("组件引用")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("受击颜色")]
    [SerializeField] private Color damageTint = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField, Min(0.01f)] private float tintDuration = 0.1f;

    private Color _baseColor = Color.white;
    private Coroutine _tintCoroutine;

    /// <summary>缓存生命组件、角色渲染器及其原始颜色。</summary>
    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        if (spriteRenderer != null)
        {
            _baseColor = spriteRenderer.color;
        }
    }

    /// <summary>组件启用时订阅有效受击事件。</summary>
    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Damaged += HandleDamaged;
        }
    }

    /// <summary>组件禁用时取消订阅并恢复原始颜色，避免状态残留。</summary>
    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandleDamaged;
        }

        if (_tintCoroutine != null)
        {
            StopCoroutine(_tintCoroutine);
            _tintCoroutine = null;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = _baseColor;
        }
    }

    /// <summary>收到有效伤害后重新开始一次受击染色。</summary>
    private void HandleDamaged(float appliedDamage)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (_tintCoroutine != null)
        {
            StopCoroutine(_tintCoroutine);
        }

        _tintCoroutine = StartCoroutine(PlayTintRoutine());
    }

    /// <summary>短暂应用受击颜色，并在游戏时间经过配置时长后恢复。</summary>
    private IEnumerator PlayTintRoutine()
    {
        spriteRenderer.color = damageTint;
        yield return new WaitForSeconds(tintDuration);
        spriteRenderer.color = _baseColor;
        _tintCoroutine = null;
    }
}
