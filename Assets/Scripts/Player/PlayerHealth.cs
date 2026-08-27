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
    [Tooltip("未找到 PlayerStats 时使用的后备最大生命；正式玩家由角色属性覆盖。")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("受击保护")]
    [SerializeField, Min(0f)]
    [Tooltip("一次有效受击后的全局无敌时间。0 表示关闭无敌帧。")]
    private float invulnerabilityDuration = 0.5f;

    private float _currentHealth;
    private float _nextDamageAllowedTime;
    private bool _isDead;
    private PlayerStats _playerStats;

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

    /// <summary>解析属性来源，并以角色最终最大生命初始化本局生命状态。</summary>
    private void Awake()
    {
        ResolvePlayerStats();
        if (_playerStats != null)
        {
            maxHealth = _playerStats.MaxHealth;
        }

        maxHealth = Mathf.Max(1f, maxHealth);
        _currentHealth = maxHealth;
        _nextDamageAllowedTime = 0f;
        _isDead = false;
    }

    /// <summary>订阅低频属性变化，以便能力升级后同步最大生命。</summary>
    private void OnEnable()
    {
        ResolvePlayerStats();
        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= HandleStatsChanged;
            _playerStats.StatsChanged += HandleStatsChanged;
            ApplyMaxHealth(_playerStats.MaxHealth);
        }
    }

    /// <summary>取消属性订阅，避免场景卸载或组件开关后重复监听。</summary>
    private void OnDisable()
    {
        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= HandleStatsChanged;
        }
    }

    /// <summary>按最终 Recovery 每秒恢复生命；暂停时 Time.deltaTime 为零，因此自然停止。</summary>
    private void Update()
    {
        if (_isDead || _playerStats == null || _currentHealth >= maxHealth)
        {
            return;
        }

        float recoveryPerSecond = _playerStats.Recovery;
        if (recoveryPerSecond > 0f)
        {
            Heal(recoveryPerSecond * Time.deltaTime);
        }
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

        // 《吸血鬼幸存者》式护甲采用固定平减，但任何一次有效攻击至少造成 1 点伤害。
        float armor = _playerStats != null ? _playerStats.Armor : 0f;
        float damageAfterArmor = Mathf.Max(1f, damage - armor);
        float previousHealth = _currentHealth;
        _currentHealth = Mathf.Max(0f, _currentHealth - damageAfterArmor);
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

    /// <summary>恢复指定生命；死亡状态、非正数和满血请求不会产生事件。</summary>
    public void Heal(float amount)
    {
        if (_isDead || amount <= 0f || _currentHealth >= maxHealth)
        {
            return;
        }

        float previousHealth = _currentHealth;
        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        if (_currentHealth > previousHealth)
        {
            HealthChanged?.Invoke(_currentHealth, maxHealth);
        }
    }

    /// <summary>缓存同对象上的权威玩家属性组件；独立测试对象允许缺失。</summary>
    private void ResolvePlayerStats()
    {
        if (_playerStats == null)
        {
            _playerStats = GetComponent<PlayerStats>();
        }
    }

    /// <summary>接收属性重算通知，并同步可能变化的最大生命。</summary>
    private void HandleStatsChanged()
    {
        if (_playerStats != null)
        {
            ApplyMaxHealth(_playerStats.MaxHealth);
        }
    }

    /// <summary>
    /// 更新最大生命；增加上限不会额外治疗，降低上限时只把当前生命钳制到新上限。
    /// </summary>
    private void ApplyMaxHealth(float newMaxHealth)
    {
        float normalizedMaxHealth = Mathf.Max(1f, newMaxHealth);
        if (Mathf.Approximately(maxHealth, normalizedMaxHealth))
        {
            return;
        }

        maxHealth = normalizedMaxHealth;
        _currentHealth = Mathf.Min(_currentHealth, maxHealth);
        HealthChanged?.Invoke(_currentHealth, maxHealth);
    }
}
