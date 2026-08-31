using System.Collections.Generic;
using UnityEngine;

/// <summary>逆境本能单级的条件属性配置。</summary>
[System.Serializable]
public sealed class LowHealthBuffLevelConfig
{
    [Min(0f)] public float mightAdditivePercent = 0.2f;
    [Min(0f)] public float moveSpeedAdditivePercent = 0.06f;
}

/// <summary>
/// 生命比例低于阈值时启用、离开阈值时立即移除的机制能力配置。
/// 判定由 PlayerHealth 事件驱动，不进行每帧轮询。
/// </summary>
[CreateAssetMenu(fileName = "LowHealthBuffMechanic", menuName = "GameData/Ability Mechanics/Low Health Buff")]
public sealed class LowHealthBuffMechanicSO : AbilityMechanicSO
{
    [Range(0.01f, 0.99f)] public float healthThreshold = 0.4f;
    public List<LowHealthBuffLevelConfig> levelConfigs = new List<LowHealthBuffLevelConfig>
    {
        new LowHealthBuffLevelConfig()
    };

    /// <summary>创建并立即评估当前生命状态的独立机制运行时。</summary>
    public override IAbilityMechanicRuntime CreateRuntime(
        AbilityRuntimeContext context,
        AbilityDataSO abilityData,
        int initialLevel)
    {
        if (context == null || context.PlayerHealth == null || context.PlayerStats == null ||
            abilityData == null)
        {
            return null;
        }

        return new Runtime(this, context, abilityData.GetStableId(), initialLevel);
    }

    /// <summary>安全读取从 1 开始的机制等级配置。</summary>
    private LowHealthBuffLevelConfig GetConfig(int level)
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            return new LowHealthBuffLevelConfig();
        }

        return levelConfigs[Mathf.Clamp(level - 1, 0, levelConfigs.Count - 1)];
    }

    /// <summary>保存单局条件状态与事件订阅，不修改共享机制资产。</summary>
    private sealed class Runtime : IAbilityMechanicRuntime
    {
        private readonly LowHealthBuffMechanicSO _configSource;
        private readonly AbilityRuntimeContext _context;
        private readonly string _sourceId;
        private readonly List<PlayerStatModifier> _activeModifiers =
            new List<PlayerStatModifier>(2);
        private int _level;
        private bool _isActive;
        private bool _disposed;

        /// <summary>建立事件订阅，并根据当前生命比例完成首次条件同步。</summary>
        public Runtime(
            LowHealthBuffMechanicSO configSource,
            AbilityRuntimeContext context,
            string abilityId,
            int initialLevel)
        {
            _configSource = configSource;
            _context = context;
            _sourceId = $"ability:{abilityId}:mechanic";
            _level = initialLevel;
            _context.PlayerHealth.HealthChanged += HandleHealthChanged;
            EvaluateCondition(true);
        }

        /// <summary>切换配置等级；条件已激活时立即替换为新等级累计数值。</summary>
        public void SetLevel(int level)
        {
            if (_disposed)
            {
                return;
            }

            _level = Mathf.Max(1, level);
            EvaluateCondition(true);
        }

        /// <summary>解除生命事件并移除条件属性来源。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.PlayerHealth.HealthChanged -= HandleHealthChanged;
            _context.PlayerStats.RemoveModifiers(_sourceId);
            _isActive = false;
        }

        /// <summary>生命变化时重新评估阈值；参数仅来自事件，不另行读取传入快照。</summary>
        private void HandleHealthChanged(float currentHealth, float maxHealth)
        {
            EvaluateCondition(false);
        }

        /// <summary>
        /// 按权威 PlayerHealth 状态启停来源。
        /// forceRefresh 用于升级时替换已激活来源，普通生命事件只有跨越阈值才重算属性。
        /// </summary>
        private void EvaluateCondition(bool forceRefresh)
        {
            PlayerHealth health = _context.PlayerHealth;
            bool shouldBeActive = health.CurrentHealth > 0f &&
                health.MaxHealth > 0f &&
                health.NormalizedHealth <= _configSource.healthThreshold;

            if (!forceRefresh && shouldBeActive == _isActive)
            {
                return;
            }

            _isActive = shouldBeActive;
            if (!_isActive)
            {
                _context.PlayerStats.RemoveModifiers(_sourceId);
                return;
            }

            LowHealthBuffLevelConfig config = _configSource.GetConfig(_level);
            _activeModifiers.Clear();
            _activeModifiers.Add(new PlayerStatModifier(
                PlayerStatType.Might,
                PlayerStatModifierMode.AdditivePercent,
                config.mightAdditivePercent));
            _activeModifiers.Add(new PlayerStatModifier(
                PlayerStatType.MoveSpeed,
                PlayerStatModifierMode.AdditivePercent,
                config.moveSpeedAdditivePercent));
            _context.PlayerStats.SetModifiers(_sourceId, _activeModifiers);
        }
    }
}
