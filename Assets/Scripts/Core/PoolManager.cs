using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;


public class PoolManager : MonoBehaviour
{
    public static PoolManager Instance { get; private set; }

    // 核心字典：Key为预制体引用，Value为对应的对象池
    private Dictionary<GameObject, ObjectPool<GameObject>> poolDictionary = new Dictionary<GameObject, ObjectPool<GameObject>>();

    private void Awake()
    {
        // 经典的单例模式，确保全局唯一
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 从对象池中获取一个实例
    /// </summary>
    /// <param name="prefab">需要实例化的预制体</param>
    /// <param name="position">生成位置</param>
    /// <param name="rotation">生成旋转</param>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("PoolManager.Spawn 收到 null prefab，已忽略。");
            return null;
        }

#if UNITY_EDITOR
        // 常见踩坑：把“场景里的对象”当 prefab 传入，会导致池 key 不稳定/回收异常。
        if (prefab.scene.IsValid() && prefab.scene.isLoaded)
        {
            Debug.LogWarning($"PoolManager.Spawn 收到的对象不是 Prefab 资产（而是场景实例）：{prefab.name}。请确认 WeaponDataSO/EnemyDataSO 引用的是 Project 面板里的 Prefab。");
        }
#endif

        if (!poolDictionary.ContainsKey(prefab))
        {
            // 如果字典里没有这个预制体的池，则初始化一个新池
            InitializePool(prefab);
        }

        // 从池中取出对象
        GameObject obj = poolDictionary[prefab].Get();
        obj.transform.position = position;
        obj.transform.rotation = rotation;
        return obj;
    }

    /// <summary>
    /// 将对象归还给对象池
    /// </summary>
    /// <param name="prefab">该对象对应的原始预制体（作为Key）</param>
    /// <param name="instance">需要回收的实例</param>
    public void Release(GameObject prefab, GameObject instance)
    {
        if (poolDictionary.ContainsKey(prefab))
        {
            poolDictionary[prefab].Release(instance);
        }
        else
        {
            // 容错处理：如果池不存在，直接销毁
            Destroy(instance);
        }
    }

    // 初始化特定预制体的对象池
    private void InitializePool(GameObject prefab)
    {
        // 使用 Unity 2021+ 内置的 ObjectPool
        ObjectPool<GameObject> newPool = new ObjectPool<GameObject>(
            createFunc: () => 
            {
                // 1. 创建逻辑
                GameObject obj = Instantiate(prefab);
                // 去掉层级里常见的 "(Clone)" 误导，便于调试（不影响对象池行为）
                obj.name = prefab.name;
                // 初始化时找 IPoolable 接口（意味着需要对象池），只要是Prefeb类型的内容，就会实现该方式
                if (obj.TryGetComponent<IPoolable>(out var poolableEntity))
                {
                    poolableEntity.SetPrefabReference(prefab);
                }
#if UNITY_EDITOR
                else
                {
                    Debug.LogWarning($"对象池创建实例时未在根节点找到 IPoolable：{prefab.name}。如果该对象需要正确回收，请确保 IPoolable 挂在 Prefab 根节点上。");
                }
#endif
                return obj;
            },
            actionOnGet: (obj) => obj.SetActive(true),       // 2. 取出时激活
            actionOnRelease: (obj) => obj.SetActive(false),  // 3. 回收时隐藏
            actionOnDestroy: (obj) => Destroy(obj),          // 4. 销毁逻辑（池满时）
            collectionCheck: false,                          // 关闭重复回收检查（提升性能，但需保证逻辑严密）
            defaultCapacity: 50,                             // 默认容量
            maxSize: 500                                     // 最大容量（防内存泄漏）
        );

        poolDictionary.Add(prefab, newPool);
    }
}
