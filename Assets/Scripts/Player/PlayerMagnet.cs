using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class PlayerMagnet : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 如果进入磁铁范围的是经验球，命令它飞向玩家本体（父物体）
        if (collision.TryGetComponent<ExpGem>(out var gem))
        {
            gem.StartFlyingTowards(transform.parent);
        }
    }
}
