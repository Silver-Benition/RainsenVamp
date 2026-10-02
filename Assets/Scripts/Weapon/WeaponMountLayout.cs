using System.Collections.Generic;
using UnityEngine;

/// <summary>装备变更时重新计算等角挂点；角色移动由父子 Transform 自动传递。</summary>
public sealed class WeaponMountLayout : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float radius = .72f;
    [SerializeField] private float firstAngle = 0f;

    /// <summary>按独立装备顺序均匀分布；仅数量改变时调用，重复武器仍各占一个位置。</summary>
    public void Refresh(IReadOnlyList<WeaponBase> weapons)
    {
        int count = 0;
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i] != null && weapons[i].UsesHeldMount) count++;
        int mountIndex = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponBase weapon = weapons[i];
            if (weapon == null) continue;
            if (!weapon.UsesHeldMount)
            {
                weapon.transform.localPosition = Vector3.zero;
                WeaponHeldView oldView = weapon.GetComponent<WeaponHeldView>();
                if (oldView != null) oldView.Configure(weapon);
                continue;
            }
            float angle = (firstAngle + 360f * mountIndex++ / count) * Mathf.Deg2Rad;
            weapon.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            WeaponHeldView view = weapon.GetComponent<WeaponHeldView>();
            if (view == null) view = weapon.gameObject.AddComponent<WeaponHeldView>();
            view.Configure(weapon);
        }
    }
}
