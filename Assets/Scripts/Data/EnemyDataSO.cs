using UnityEngine;

/// <summary>
/// 敌人静态配置资产，保存生命、移动、接触伤害和掉落物等策划数据。
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "GameData/Enemy Data")]
public class EnemyDataSO : ScriptableObject
{
    [Header("基础属性")]
    [Tooltip("敌人名称的本地化键；运行时逻辑不得依赖显示文本。")]
    public string enemyNameKey;
    [Min(0.01f), Tooltip("敌人满生命值。")]
    public float maxHealth = 20f;
    [Min(0f), Tooltip("敌人追踪玩家时的基础移动速度。")]
    public float moveSpeed = 2f;
    [Min(0f), Tooltip("敌人与玩家持续接触时，每次有效受击造成的伤害。")]
    public float collisionDamage = 10f;

    [Tooltip("关闭后该敌人不会被 Defang，供未来首领或特殊机关使用。")]
    public bool canBeDefanged = true;
    [Header("逐波成长：第 1 波使用基础值")]
    [Min(0f), Tooltip("每经过一波增加的生命；生成时截断为整数，再应用关卡倍率。")]
    public float healthPerWave;
    [Min(0f)] public float contactDamagePerWave;
    [Min(0f), Tooltip("远程攻击基础伤害每波增加量，发射时写入弹体。")]
    public float projectileDamagePerWave;
    [Min(0f)] public float speedPerWave;

    [Header("掉落物")]
    [Tooltip("敌人死亡时从对象池生成的经验球 Prefab。")]
    public GameObject dropExpPrefab;

    [Tooltip("金币与宝箱的可选掉落配置；为空时只生成经验球。")]
    public EnemyDropTableSO dropTable;
}
