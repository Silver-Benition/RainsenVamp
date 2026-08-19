using System;
using UnityEngine;

/// <summary>
/// 玩家生命运行时组件。
/// 负责生命值、死亡状态和受击无敌帧；通过事件把结果交给 UI 与受击表现层。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("生命配置")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("受击保护")]
    [SerializeField, Min(0f)]
    [Tooltip("一次有效受击后的全局无敌时间。0 表示关闭无敌帧。")]
    private float invulnerabilityDuration = 0.5f;

    private float _currentHealth;
    private float _nextDamageAllowedTime;
    private bool _isDead;

    /// <summary>当前生命值，只允许由生命组件内部修改。</summary>
    public float CurrentHealth => _currentHealth;

    /// <summary>当前配置的最大生命值。</summary>
    public float MaxHealth => maxHealth;

    /// <summary>当前生命比例，最大生命异常时安全返回 0。</summary>
    public float NormalizedHealth => maxHealth > 0f
        ? Mathf.Clamp01(_currentHealth / maxHealth)
        : 0f;

    /// <summary>生命是否已经归零。</summary>
    public bool IsDead => _isDead;

    /// <summary>生命值发生变化时触发，参数依次为当前生命和最大生命。</summary>
    public event Action<float, float> HealthChanged;

    /// <summary>一次伤害真正生效时触发，参数为实际扣除的生命值。</summary>
    public event Action<float> Damaged;

    /// <summary>生命首次归零时触发；正式 Game Over 流程将在后续系统中订阅。</summary>
    public event Action Died;

    /// <summary>初始化本局玩家生命状态。</summary>
    private void Awake()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        _currentHealth = maxHealth;
        _nextDamageAllowedTime = 0f;
        _isDead = false;
    }

    /// <summary>承受普通伤害，转发到包含暴击标记的统一入口。</summary>
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, false);
    }

    /// <summary>
    /// 承受伤害并应用全局无敌帧。
    /// 非正数伤害、死亡状态和无敌期内的请求都会被忽略，不触发表现事件。
    /// </summary>
    public void TakeDamage(float damage, bool isCritical)
    {
        if (_isDead || damage <= 0f || Time.time < _nextDamageAllowedTime)
        {
            return;
        }

        float previousHealth = _currentHealth;
        _currentHealth = Mathf.Max(0f, _currentHealth - damage);
        float appliedDamage = previousHealth - _currentHealth;
        if (appliedDamage <= 0f)
        {
            return;
        }

        // 先写入时间门槛再通知外部，确保监听者无法在同一帧绕过保护。
        _nextDamageAllowedTime = Time.time + Mathf.Max(0f, invulnerabilityDuration);
        Damaged?.Invoke(appliedDamage);
        HealthChanged?.Invoke(_currentHealth, maxHealth);

        if (_currentHealth <= 0f)
        {
            _isDead = true;
            Died?.Invoke();
        }
    }
}
