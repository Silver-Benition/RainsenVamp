using System.Collections.Generic;
using UnityEngine;

/// <summary>每个敌人复用的减速与灼烧容器；来源失效和敌人回池立即清空，不让状态跨生命代次。</summary>
public sealed class EnemyCombatStatus : MonoBehaviour
{
    private struct Slow { public Transform Source; public float Strength, Until; }
    private struct Burn { public Transform Source; public float Damage, Until; public WeaponDataSO Weapon; public WeaponHitSnapshot Hit; }
    private readonly List<Slow> _slows = new List<Slow>(8);
    private readonly List<Burn> _burns = new List<Burn>(6);
    private EnemyBase _enemy;
    private float _nextBurn;
    private int _lastInkTick = int.MinValue;
    public float SpeedFactor { get; private set; } = 1f;

    /// <summary>缓存敌人身份，避免每帧查找。</summary>
    private void Awake() { _enemy = GetComponent<EnemyBase>(); }
    /// <summary>敌人复用或销毁时丢弃所有来源。</summary>
    private void OnDisable() { Clear(); }
    /// <summary>无攻击状态的敌人仅维护中性速度，不额外创建集合。</summary>
    private void Update()
    {
        if (!RoundController.AllowsCombat) { if (RoundController.Enabled && RoundController.Instance.Phase != RoundPhase.Combat) Clear(); return; }
        float strongest = 0;
        for (int i = _slows.Count - 1; i >= 0; i--)
        {
            Slow s = _slows[i];
            if (!Alive(s.Source) || Time.time > s.Until) _slows.RemoveAt(i);
            else strongest = Mathf.Max(strongest, s.Strength);
        }
        SpeedFactor = 1f - strongest;
        for (int i = _burns.Count - 1; i >= 0; i--)
            if (!Alive(_burns[i].Source)) _burns.RemoveAt(i);
        // 先按计划时刻结算自然到期前的伤害，再移除到期状态；卡顿不能吃掉第三跳。
        while (_burns.Count > 0 && Time.time + .001f >= _nextBurn)
        {
            Burn best = default;
            for (int i = 0; i < _burns.Count; i++)
                if (_burns[i].Until + .001f >= _nextBurn && _burns[i].Damage > best.Damage) best = _burns[i];
            _nextBurn += .5f;
            if (best.Damage > 0 && _enemy != null && _enemy.CurrentHealth > 0)
                best.Hit.ApplyDamageOverTime(_enemy, best.Damage, best.Weapon);
        }
        for (int i = _burns.Count - 1; i >= 0; i--)
            if (Time.time > _burns[i].Until) _burns.RemoveAt(i);
    }
    /// <summary>每个来源独立到期，弱减速不能续强减速；首领只承受一半减速。</summary>
    public void ApplySlow(Transform source, float amount, float duration)
    {
        if (!Alive(source)) return;
        if (GetComponent<BossEnemyController>() != null) amount *= .5f;
        Slow s = new Slow { Source = source, Strength = Mathf.Clamp01(amount), Until = Time.time + duration };
        for (int i = 0; i < _slows.Count; i++) if (_slows[i].Source == source) { _slows[i] = s; return; }
        _slows.Add(s);
    }
    /// <summary>同源灼烧保留更强伤害并续期；多把长鞭共用半秒节拍，只结算最强来源。</summary>
    public void ApplyBurn(Transform source, float damage, WeaponDataSO weapon)
    { ApplyWeaponBurn(source, damage, weapon, default); }

    /// <summary>保存施加灼烧的攻击归属；后续三跳计入原武器实例，并在回池时一并清空。</summary>
    public void ApplyWeaponBurn(Transform source, float damage, WeaponDataSO weapon, WeaponHitSnapshot hit)
    {
        if (!Alive(source)) return;
        if (_burns.Count == 0) _nextBurn = Time.time + .5f;
        Burn b = new Burn { Source = source, Damage = damage, Until = Time.time + 1.5f, Weapon = weapon, Hit = hit };
        for (int i = 0; i < _burns.Count; i++) if (_burns[i].Source == source)
        { b.Damage = Mathf.Max(b.Damage, _burns[i].Damage); _burns[i] = b; return; }
        _burns.Add(b);
    }
    /// <summary>重叠墨带在共同半秒窗口只能结算最强的一条。</summary>
    public bool ConsumeInkTick(float scheduledTime)
    { int tick = Mathf.FloorToInt((scheduledTime + .001f) * 2); if (tick <= _lastInkTick) return false; _lastInkTick = tick; return true; }
    /// <summary>释放全部运行状态；保持列表容量供下次池复用。</summary>
    private void Clear() { _slows.Clear(); _burns.Clear(); SpeedFactor = 1f; _nextBurn = 0; _lastInkTick = int.MinValue; }
    /// <summary>仅持有的活动来源能维持状态。</summary>
    private static bool Alive(Transform source) { return source != null && source.gameObject.activeInHierarchy; }
}
