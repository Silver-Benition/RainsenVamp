using UnityEngine;
using TMPro;

/// <summary>
/// 伤害飘字单体（池化友好）。
/// 每个实例是一个 World Space 的 TextMeshPro 对象（注意：不是 UGUI 版本！）。
/// 动画逻辑全部用代码驱动（不依赖 Animator），包含：
///   - 向上漂浮（带随机 X 偏移，避免多数字重叠）
///   - 缩放弹跳（Pop-in 效果）
///   - Alpha 淡出
///
/// 生命周期由自身 timer 管理，到点自动归还对象池。
///
/// 【重要 Prefab 配置】
/// 1. 创建空 GameObject，直接 Add Component → TextMeshPro（3D 版本，不是 UI 版本）
/// 2. 不要放在任何 Canvas 下！直接作为根物体或挂在非 Canvas 节点下
/// 3. RectTransform 设置：Width=4, Height=2（控制文本边界框在世界空间的大小）
/// 4. TextMeshPro 设置：Font Size=8, Alignment=Center/Middle, Enable Auto Sizing=关闭
/// 5. Add Component → DamagePopup
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class DamagePopup : MonoBehaviour, IPoolable
{
    // =====================================================================
    // 配置（可在 Prefab 上调整默认值，也可由 Manager 运行时覆盖）
    // =====================================================================
    [Header("运动参数")]
    [Tooltip("飘字总持续时间（秒）")]
    [SerializeField] private float lifetime = 0.8f;
    [Tooltip("向上漂浮速度（单位/秒）")]
    [SerializeField] private float floatSpeed = 2.0f;
    [Tooltip("水平随机偏移范围（避免多数字堆叠）")]
    [SerializeField] private float randomOffsetX = 0.3f;

    [Header("缩放动画")]
    [Tooltip("初始弹出缩放倍率")]
    [SerializeField] private float popScale = 1.5f;
    [Tooltip("缩放回弹时间（秒）。在这段时间内从 popScale 缩回 1.0")]
    [SerializeField] private float popDuration = 0.15f;

    [Header("暴击加成")]
    [Tooltip("暴击时额外放大倍率")]
    [SerializeField] private float critScaleMultiplier = 1.4f;

    // =====================================================================
    // 内部状态
    // =====================================================================
    private TextMeshPro textMesh;
    private GameObject prefabReference;

    private float timer;
    private float fadeStartTime;
    private Vector3 baseScale;
    private Color baseColor;
    private bool isActive; // 显式标记：是否处于活跃动画状态

    private void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        baseScale = Vector3.one;
    }

    /// <summary>
    /// 对象池取出时触发：立即将自身标记为"等待初始化"，防止残留状态干扰。
    /// </summary>
    private void OnEnable()
    {
        // 重置关键状态，防止对象池复用时残留上一次的数据
        timer = 0f;
        isActive = false;
    }

    // =====================================================================
    // IPoolable 接口
    // =====================================================================
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    // =====================================================================
    // 初始化（由 DamagePopupManager 调用）
    // =====================================================================
    /// <summary>
    /// 初始化飘字内容与表现。
    /// </summary>
    /// <param name="damage">伤害数值</param>
    /// <param name="isCritical">是否暴击（影响颜色与大小）</param>
    /// <param name="normalColor">普通伤害颜色</param>
    /// <param name="critColor">暴击伤害颜色</param>
    public void Initialize(float damage, bool isCritical, Color normalColor, Color critColor)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();

        // --- 文本内容 ---
        int displayValue = Mathf.RoundToInt(damage);
        if (textMesh != null)
        {
            textMesh.text = isCritical ? $"{displayValue}!" : displayValue.ToString();
        }

        // --- 颜色 ---
        baseColor = isCritical ? critColor : normalColor;
        if (textMesh != null)
        {
            textMesh.color = baseColor;
        }

        // --- 缩放 ---
        float scale = isCritical ? popScale * critScaleMultiplier : popScale;
        transform.localScale = baseScale * scale;

        // --- 位置随机偏移（仅 X 轴，Y 由漂浮动画处理） ---
        Vector3 pos = transform.position;
        pos.x += Random.Range(-randomOffsetX, randomOffsetX);
        transform.position = pos;

        // --- 计时器重置 ---
        timer = 0f;
        fadeStartTime = lifetime * 0.5f;
        isActive = true; // 初始化完成，开始动画
    }

    // =====================================================================
    // 每帧更新动画
    // =====================================================================
    private void Update()
    {
        // 未初始化时不执行任何逻辑（防止 OnEnable → Update 之间的空帧）
        if (!isActive) return;

        timer += Time.deltaTime;

        // 1. 向上漂浮
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;

        // 2. 缩放回弹（Pop-in → 正常大小）
        if (timer < popDuration)
        {
            float t = timer / popDuration;
            float currentScaleValue = Mathf.Lerp(popScale, 1f, t);
            transform.localScale = baseScale * currentScaleValue;
        }
        else if (timer < popDuration + 0.05f)
        {
            // 确保回弹结束后稳定在 baseScale（只执行一帧赋值）
            transform.localScale = baseScale;
        }

        // 3. Alpha 淡出（后半段生命周期）
        if (timer >= fadeStartTime && textMesh != null)
        {
            float fadeProgress = (timer - fadeStartTime) / (lifetime - fadeStartTime);
            fadeProgress = Mathf.Clamp01(fadeProgress);
            float alpha = Mathf.Lerp(1f, 0f, fadeProgress);
            Color c = baseColor;
            c.a = alpha;
            textMesh.color = c;
        }

        // 4. 生命周期结束 → 归还对象池
        if (timer >= lifetime)
        {
            ReturnToPool();
        }
    }

    // =====================================================================
    // 回收
    // =====================================================================
    private void ReturnToPool()
    {
        isActive = false;

        if (prefabReference != null && PoolManager.Instance != null)
        {
            PoolManager.Instance.Release(prefabReference, gameObject);
        }
        else
        {
            // 兜底：没有池引用时直接隐藏
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 安全保底：如果因为某种原因 ReturnToPool 未执行（极端边界情况），
    /// 在 OnDisable 时确保状态干净。
    /// </summary>
    private void OnDisable()
    {
        isActive = false;
        timer = 0f;
    }
}
