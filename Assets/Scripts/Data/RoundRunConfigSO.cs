using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>回合额外目标；稳定键由击杀、首领或未来任务系统报告。</summary>
[Serializable]
public sealed class RoundObjectiveDefinition
{
    public string key = "kills";
    public string nameKey = "round.goal.kills";
    public string displayName = "击杀敌人";
    [Min(1)] public int required = 1;
}

/// <summary>一个回合的静态定义；生存门槛、硬截止与目标彼此独立。</summary>
[Serializable]
public sealed class RoundDefinition
{
    [Min(0.1f)] public float survivalSeconds = 60;
    [Tooltip("0 表示允许加时；大于零时作为未完成目标的最终截止。")]
    public float deadlineSeconds;
    public bool allowEarlyBossVictory;
    public bool spawnBoss;
    public bool requireAllObjectives = true;
    public WaveConfigSO spawnConfig;
    [Min(0.01f)] public float enemyHealthMultiplier = 1;
    [Min(0)] public float enemyDamageMultiplier = 1;
    public List<RoundObjectiveDefinition> objectives = new List<RoundObjectiveDefinition>();
}

/// <summary>二十回合模式的配置入口；运行进度不得写入共享资产。</summary>
[CreateAssetMenu(menuName = "GameData/Rounds/Run")]
public sealed class RoundRunConfigSO : ScriptableObject
{
    [Min(.45f)] public float settlementSeconds = 1.5f;
    public Sprite crateIcon;
    public Vector2 arenaSize = new Vector2(24, 16);
    public RunShopCatalogSO shopCatalog;
    public List<RoundDefinition> rounds = new List<RoundDefinition>();

    /// <summary>检查可达目标与时间边界，拒绝无法推进的正式回合配置。</summary>
    public bool Validate(out string error)
    {
        error = "";
        if (float.IsNaN(settlementSeconds) || float.IsInfinity(settlementSeconds) || settlementSeconds < .45f)
            { error = "结算过渡必须至少包含 0.45 秒的吸收时间。"; return false; }
        if (arenaSize.x < 8 || arenaSize.y < 8 || rounds == null || rounds.Count == 0 || shopCatalog == null)
            { error = "回合、商店或竞技场配置缺失。"; return false; }
        foreach (RoundDefinition round in rounds)
        {
            if (round == null || float.IsNaN(round.survivalSeconds) || float.IsInfinity(round.survivalSeconds)
                || round.survivalSeconds <= 0 || round.spawnConfig == null
                || float.IsNaN(round.deadlineSeconds) || float.IsInfinity(round.deadlineSeconds)
                || round.deadlineSeconds < 0 || (round.deadlineSeconds > 0 && round.deadlineSeconds < round.survivalSeconds))
                { error = "回合时长、截止或刷怪配置无效。"; return false; }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoundObjectiveDefinition objective in round.objectives)
                if (objective == null || string.IsNullOrWhiteSpace(objective.key) || objective.required < 1 || !keys.Add(objective.key))
                    { error = "目标键重复或目标值无效。"; return false; }
        }
        return shopCatalog.Validate(out error);
    }
}
