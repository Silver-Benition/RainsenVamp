using UnityEngine;

/// <summary>
/// 玩家正式受击框的显式标记。
/// 该组件必须与实际接收敌方伤害的 Collider2D 挂在同一个 GameObject 上；
/// 辅助范围、拾取或探测用 Trigger 不应挂载此组件。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class PlayerHurtbox : MonoBehaviour
{
}
