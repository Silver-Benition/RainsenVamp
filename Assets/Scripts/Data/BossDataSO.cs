using UnityEngine;

/// <summary>首领一轮径向弹幕的静态配置。</summary>
[System.Serializable]
public sealed class BossBarragePhaseData
{
    [Min(1), Tooltip("一次弹幕发射的射线数量。")]
    public int projectileCount = 8;

    [Min(0.1f), Tooltip("同阶段弹幕间隔，单位为秒。")]
    public float interval = 3f;

    [Min(0f), Tooltip("每枚首领弹体的基础伤害。")]
    public float projectileDamage = 10f;

    [Min(0f), Tooltip("每枚首领弹体的世界速度。")]
    public float projectileSpeed = 4.5f;

    [Min(0.1f), Tooltip("首领弹体超过该时间后回收。")]
    public float projectileLifetime = 6f;

    /// <summary>返回满足对象池和弹幕逻辑安全边界的射线数量。</summary>
    public int GetSafeProjectileCount()
    {
        return Mathf.Clamp(projectileCount, 1, 64);
    }

    /// <summary>返回不低于 0.1 秒的安全发射间隔。</summary>
    public float GetSafeInterval()
    {
        return Mathf.Max(0.1f, interval);
    }
}

/// <summary>
/// 武装巨像等首领的局内静态配置。
/// 首领不复用普通 EnemyDataSO 掉落规则，防止死亡出口意外生成经验、金币或宝箱。
/// </summary>
[CreateAssetMenu(fileName = "NewBossData", menuName = "GameData/Boss Data")]
public sealed class BossDataSO : ScriptableObject
{
    [Header("稳定身份与显示")]
    public string bossID = "boss_armed_colossus";
    public string nameKey = "boss.armed_colossus.name";
    public string displayName = "武装巨像";
    public Sprite icon;

    [Header("首领基础属性")]
    [Min(0.01f)] public float maxHealth = 800f;
    [Min(0f)] public float moveSpeed = 0.9f;
    [Min(0f)] public float contactDamage = 18f;
    [Tooltip("首领明确不参与 Defang；该字段仅用于数据审计。")]
    public bool canBeDefanged = false;

    [Header("阶段阈值")]
    [Range(0.01f, 0.99f), Tooltip("生命值小于等于该比例后进入第二阶段。")]
    public float phaseTwoHealthRatio = 0.5f;

    [Header("阶段一：8 向弹幕")]
    public BossBarragePhaseData phaseOne = new BossBarragePhaseData
    {
        projectileCount = 8,
        interval = 3f,
        projectileDamage = 10f,
        projectileSpeed = 4.5f,
        projectileLifetime = 6f
    };

    [Header("阶段二：12 向弹幕")]
    public BossBarragePhaseData phaseTwo = new BossBarragePhaseData
    {
        projectileCount = 12,
        interval = 2f,
        projectileDamage = 12f,
        projectileSpeed = 5.5f,
        projectileLifetime = 6f
    };

    [Header("对象池表现")]
    [Tooltip("首领使用的共享 EnemyProjectile 池化 Prefab。")]
    public GameObject projectilePrefab;
    [Tooltip("每次径向弹幕发射时播放的世界归属预警 VFX 池化 Prefab。")]
    public GameObject warningVfxPrefab;

    /// <summary>读取稳定首领 ID；缺失时以资产名安全回退。</summary>
    public string GetStableId()
    {
        return !string.IsNullOrWhiteSpace(bossID) ? bossID : name;
    }

    /// <summary>返回当前可直接显示的首领名称。</summary>
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(displayName) ? displayName : name;
    }

    /// <summary>建立首领专用敌人快照；不读取玩家 Curse/Defang，确保数值固定且不掉落。</summary>
    public EnemySpawnSnapshot CreateSpawnSnapshot()
    {
        return new EnemySpawnSnapshot(
            Mathf.Max(0.01f, maxHealth),
            Mathf.Max(0f, moveSpeed),
            Mathf.Max(0f, contactDamage),
            1f,
            false);
    }

    /// <summary>返回当前生命阶段需要使用的弹幕配置。</summary>
    public BossBarragePhaseData GetPhase(bool phaseTwoActive)
    {
        BossBarragePhaseData phase = phaseTwoActive ? phaseTwo : phaseOne;
        return phase ?? new BossBarragePhaseData();
    }
}
