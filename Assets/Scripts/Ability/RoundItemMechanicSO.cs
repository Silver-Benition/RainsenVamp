using UnityEngine;

/// <summary>按回合触发的道具机制类型；配置与玩家运行状态分离。</summary>
public enum RoundItemTrigger { SurvivedLowHealthDamage, CombatTime }

/// <summary>应急糖块与余烬沙漏的数据配置；每次持有创建独立运行时。</summary>
[CreateAssetMenu(menuName = "GameData/Ability Mechanics/Round Item")]
public sealed class RoundItemMechanicSO : AbilityMechanicSO
{
    public RoundItemTrigger trigger;
    [Range(0, 1)] public float healthThreshold = .25f;
    [Range(0, 1)] public float healRatio = .15f;
    [Min(0)] public float combatSeconds = 15;
    public float damagePercent = 15;
    public float speedPercent = 5;

    /// <summary>绑定本局生命与回合事件；没有回合模式时不启用限定回合的机制。</summary>
    public override IAbilityMechanicRuntime CreateRuntime(AbilityRuntimeContext context, AbilityDataSO abilityData, int initialLevel)
    {
        if (context?.PlayerStats == null || context.PlayerHealth == null || abilityData == null || !RoundController.Enabled)
            return null;
        return new Runtime(this, context, abilityData.GetStableId(), RoundController.Instance);
    }

    /// <summary>运行时只监听权威事件；不扫描场景、不自行累计暂停时间。</summary>
    private sealed class Runtime : IAbilityMechanicRuntime
    {
        private readonly RoundItemMechanicSO _config;
        private readonly AbilityRuntimeContext _context;
        private readonly RoundController _round;
        private readonly string _source;
        private readonly PlayerStatModifier[] _modifiers;
        private int _generation = -1;
        private bool _used, _active, _disposed;

        /// <summary>建立独立订阅，并在中途获得时立即同步当前回合。</summary>
        public Runtime(RoundItemMechanicSO config, AbilityRuntimeContext context, string id, RoundController round)
        {
            _config = config; _context = context; _round = round; _source = "ability:" + id + ":round";
            _modifiers = new[] {
                new PlayerStatModifier(PlayerStatType.DamagePercent, PlayerStatModifierMode.Flat, config.damagePercent),
                new PlayerStatModifier(PlayerStatType.SpeedPercent, PlayerStatModifierMode.Flat, config.speedPercent) };
            _round.Changed += SyncRound;
            if (config.trigger == RoundItemTrigger.CombatTime) _round.CombatTimeAdvanced += OnCombatTime;
            else _context.PlayerHealth.Damaged += OnDamage;
            SyncRound();
        }

        /// <summary>两项机制均为唯一道具；等级通知仅重新同步，不重置触发次数。</summary>
        public void SetLevel(int level) { SyncRound(); }

        /// <summary>退出时解除订阅并移除临时属性，重复释放安全。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_round != null) { _round.Changed -= SyncRound; _round.CombatTimeAdvanced -= OnCombatTime; }
            if (_context.PlayerHealth != null) _context.PlayerHealth.Damaged -= OnDamage;
            RemoveBuff();
        }

        /// <summary>只在新回合代次重置使用次数；结算、失败和离开战斗时立即移除沙漏增益。</summary>
        private void SyncRound()
        {
            if (_disposed || _round == null) return;
            int generation = _round.Current?.Generation ?? -1;
            if (generation != _generation) { _generation = generation; _used = false; RemoveBuff(); }
            if (_round.Phase != RoundPhase.Combat) { RemoveBuff(); return; }
            if (_config.trigger == RoundItemTrigger.CombatTime) OnCombatTime(_round.Current?.Elapsed ?? 0);
        }

        /// <summary>伤害扣血后检查存活与阈值，先消费本回合次数再治疗，避免生命事件重入。</summary>
        private void OnDamage(float amount)
        {
            if (_disposed || _used || amount <= 0 || _round == null || _round.Phase != RoundPhase.Combat) return;
            PlayerHealth health = _context.PlayerHealth;
            // Damaged 先于死亡标记广播，因此同时检查实际生命，不能挽救致死伤害。
            if (health.IsDead || health.CurrentHealth <= 0 || health.NormalizedHealth > _config.healthThreshold) return;
            _used = true;
            // 生命组件原生保存浮点值，按最大生命比例精确治疗，不额外取整。
            health.RestoreHealth(health.MaxHealth * _config.healRatio);
        }

        /// <summary>达到累计战斗秒数后仅写一次增益；暂停和局间不会收到计时推进。</summary>
        private void OnCombatTime(float elapsed)
        {
            if (_disposed || _active || _round == null || _round.Phase != RoundPhase.Combat
                || elapsed < _config.combatSeconds || _context.PlayerHealth.CurrentHealth <= 0) return;
            _active = true;
            _context.PlayerStats.SetModifiers(_source, _modifiers);
        }

        /// <summary>仅在存在增益时移除来源，避免普通回合通知引起无意义的属性重算。</summary>
        private void RemoveBuff()
        {
            if (!_active) return;
            _active = false;
            if (_context.PlayerStats != null) _context.PlayerStats.RemoveModifiers(_source);
        }
    }
}
