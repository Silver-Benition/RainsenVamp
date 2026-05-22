using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyData", menuName = "GameData/Enemy Data")]
public class EnemyDataSO : ScriptableObject
{
    [Header("基础属性")]
    public string enemyNameKey;
    public float maxHealth = 20f;
    public float moveSpeed = 2f;
    public float collisionDamage = 10f; // 碰到玩家时造成的伤害（预留）
    [Header("掉落物")]
    public GameObject dropExpPrefab; // 怪物死亡掉落的经验球预制体
}
