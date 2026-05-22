using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 凡是需要被 PoolManager 管理的对象，都必须实现此接口
/// </summary>
public interface IPoolable
{
    // 接收并保存自己对应的预制体引用（作为回收时的 Key）
    void SetPrefabReference(GameObject prefab);
}
