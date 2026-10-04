using System;
using UnityEditor;

/// <summary>作者工具按稳定 ID 引用既有标签资产，重新导入不会从模板继承错误分类。</summary>
public static class WeaponSetAuthoring
{
    /// <summary>缺少配置立即报告导入错误，避免悄悄丢失商店偏好与羁绊。</summary>
    public static WeaponSetSO[] Resolve(string[] ids)
    {
        if (ids == null || ids.Length == 0) throw new InvalidOperationException("武器必须显式配置标签。");
        var result = new WeaponSetSO[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            result[i] = AssetDatabase.LoadAssetAtPath<WeaponSetSO>("Assets/Data/WeaponSets/" + ids[i] + ".asset");
            if (result[i] == null || result[i].GetStableId() != ids[i])
                throw new InvalidOperationException("缺少武器标签：" + ids[i]);
        }
        return result;
    }
}
