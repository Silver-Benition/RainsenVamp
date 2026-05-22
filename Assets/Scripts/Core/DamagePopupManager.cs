using UnityEngine;

/// <summary>
/// 伤害飘字管理器（全局单例）。
/// 职责：
/// - 持有飘字 Prefab 引用
/// - 提供 Show() 接口供外部调用（EnemyBase.TakeDamage 等）
/// - 通过 PoolManager 生成/回收飘字实例
///
/// 用法：在场景中创建空 GameObject 挂此脚本，拖入飘字 Prefab 即可。
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    public static DamagePopupManager Instance { get; private set; }

    [Header("飘字 Prefab")]
    [Tooltip("World Space TextMeshPro 飘字预制体（需挂载 DamagePopup + IPoolable）。")]
    public GameObject popupPrefab;

    [Header("颜色配置")]
    [Tooltip("普通伤害颜色")]
    public Color normalColor = Color.white;
    [Tooltip("暴击伤害颜色（Vampire Survivors 风格建议橙红）")]
    public Color criticalColor = new Color(1f, 0.45f, 0.1f, 1f); // 橙红色

    [Header("生成偏移")]
    [Tooltip("飘字生成时在目标位置上方的 Y 偏移（避免和怪物贴图重叠）。")]
    public float spawnOffsetY = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 在指定世界坐标弹出一个伤害飘字。
    /// </summary>
    /// <param name="damage">伤害数值</param>
    /// <param name="worldPosition">弹出位置（通常为怪物 transform.position）</param>
    /// <param name="isCritical">是否暴击</param>
    public void Show(float damage, Vector3 worldPosition, bool isCritical = false)
    {
        if (popupPrefab == null)
        {
            Debug.LogWarning("[DamagePopupManager] popupPrefab 未赋值，无法生成飘字。");
            return;
        }

        // 在目标头顶稍上方生成
        Vector3 spawnPos = worldPosition + Vector3.up * spawnOffsetY;

        GameObject obj = PoolManager.Instance.Spawn(popupPrefab, spawnPos, Quaternion.identity);
        if (obj == null) return;

        if (obj.TryGetComponent<DamagePopup>(out var popup))
        {
            popup.Initialize(damage, isCritical, normalColor, criticalColor);
        }
    }
}
