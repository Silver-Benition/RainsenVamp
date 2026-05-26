# Session 4 代码改动详情

> 标注说明：
> - `[新增文件]` = 本次从零创建的文件，展示完整代码
> - `[新增代码块]` = 在已有文件中插入的全新代码段
> - `[修改代码块]` = 对已有代码的改写，附 Before/After 对比

---

## 新增文件

---

### 1. `Assets/Shaders/Sprites-FlashWhite.shader` [新增文件]

**用途**：在 Unity 默认 Sprite 渲染基础上增加 `_FlashAmount` 闪白通道。

<details>
<summary>点击展开完整代码</summary>

```hlsl
Shader "Custom/Sprites-FlashWhite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment FlashFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _FlashColor;
            fixed _FlashAmount;
            fixed4 _RendererColor;

            v2f SpriteVert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color * _RendererColor;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 FlashFrag(v2f IN) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;
                col.rgb = lerp(col.rgb, _FlashColor.rgb * col.a, _FlashAmount);
                col.rgb *= col.a;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
```

</details>

---

### 2. `Assets/Scripts/VFX/HitFlash.cs` [新增文件]

**用途**：受击闪白组件，MaterialPropertyBlock 驱动零分配闪白。

<details>
<summary>点击展开完整代码</summary>

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// 受击闪白组件。
/// 挂在需要受击反馈的实体（怪物/Boss）根节点或含 SpriteRenderer 的子节点上。
/// 通过 MaterialPropertyBlock 驱动 Shader 的 _FlashAmount 属性，
/// 零材质实例分配、零 GC，适合海量同屏实体。
///
/// 使用前提：
/// - SpriteRenderer 的 Material 必须使用 "Custom/Sprites-FlashWhite" Shader
/// - 或任何包含 _FlashAmount 属性的兼容 Shader
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Header("闪白配置")]
    [Tooltip("闪白持续时间（秒）。像素游戏建议极短，1~3 帧的视觉冲击最佳。")]
    [SerializeField] private float flashDuration = 0.1f;

    [Tooltip("闪白颜色。默认纯白，可改为其他色调（如红色表示高伤）。")]
    [SerializeField] private Color flashColor = Color.white;

    // 组件缓存
    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock mpb;

    // Shader 属性 ID 缓存（避免每帧字符串查找）
    private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

    // 协程句柄：防止多次闪白叠加导致提前恢复
    private Coroutine flashCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        mpb = new MaterialPropertyBlock();
    }

    /// <summary>
    /// 触发一次闪白。可在 TakeDamage() 中调用。
    /// 若前一次闪白尚未结束，会打断并重新开始（保证每次受击都有反馈）。
    /// </summary>
    public void TriggerFlash()
    {
        if (spriteRenderer == null) return;

        // 打断上一次未完成的闪白协程
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    /// <summary>
    /// 允许外部动态修改闪白颜色（如不同伤害类型用不同色调）。
    /// </summary>
    public void TriggerFlash(Color overrideColor)
    {
        flashColor = overrideColor;
        TriggerFlash();
    }

    private IEnumerator FlashRoutine()
    {
        // 设置闪白参数
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(FlashColorID, flashColor);
        mpb.SetFloat(FlashAmountID, 1f);
        spriteRenderer.SetPropertyBlock(mpb);

        // 等待闪白持续时间（使用 unscaledDeltaTime 确保暂停时不受影响）
        // 但受击闪白发生在游戏进行中，用 WaitForSeconds 即可（受 timeScale 影响是正确的）
        yield return new WaitForSeconds(flashDuration);

        // 恢复正常
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(FlashAmountID, 0f);
        spriteRenderer.SetPropertyBlock(mpb);

        flashCoroutine = null;
    }

    /// <summary>
    /// 对象被回收（OnDisable）时立即重置闪白状态，
    /// 防止下次从对象池取出时还残留白色。
    /// </summary>
    private void OnDisable()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        // 重置 MaterialPropertyBlock
        if (spriteRenderer != null && mpb != null)
        {
            spriteRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(FlashAmountID, 0f);
            spriteRenderer.SetPropertyBlock(mpb);
        }
    }
}
```

</details>

---

### 3. `Assets/Scripts/VFX/DamagePopup.cs` [新增文件]

**用途**：伤害飘字单体，对象池友好，纯代码驱动动画。

<details>
<summary>点击展开完整代码</summary>

```csharp
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
```

</details>

---

### 4. `Assets/Scripts/Core/DamagePopupManager.cs` [新增文件]

**用途**：飘字管理器全局单例。

<details>
<summary>点击展开完整代码</summary>

```csharp
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
```

</details>

---

### 5. `Assets/Scripts/Player/AimController.cs` [新增文件]

**用途**：瞄准方向控制器，为武器提供发射方向。

<details>
<summary>点击展开完整代码</summary>

```csharp
using UnityEngine;

/// <summary>
/// 瞄准方向控制器。挂在 Player 上，为所有武器提供统一的"发射方向"。
///
/// 当前模式：
/// - FollowMovement：跟随玩家移动方向，停止移动时保持最后朝向
///
/// 未来扩展：
/// - Manual：由鼠标/右摇杆控制瞄准方向（移动方向 ≠ 攻击方向）
/// - NearestEnemy：自动锁定最近敌人方向
/// </summary>
public class AimController : MonoBehaviour
{
    public enum AimMode
    {
        FollowMovement = 0,  // 跟随移动方向（Vampire Survivors 经典模式）
        // Manual = 1,       // 手动瞄准（预留：鼠标/右摇杆）
        // NearestEnemy = 2, // 自动锁定最近敌人（预留）
    }

    [Header("瞄准模式")]
    public AimMode aimMode = AimMode.FollowMovement;

    [Header("默认朝向")]
    [Tooltip("游戏开始时 / 未产生任何输入前的默认发射方向")]
    [SerializeField] private Vector2 defaultDirection = Vector2.right;

    /// <summary>
    /// 当前瞄准方向（归一化）。武器系统读取此属性决定发射朝向。
    /// </summary>
    public Vector2 AimDirection { get; private set; }

    private void Awake()
    {
        AimDirection = defaultDirection.normalized;
    }

    private void Update()
    {
        switch (aimMode)
        {
            case AimMode.FollowMovement:
                UpdateFollowMovement();
                break;

            // 未来模式在这里扩展
            // case AimMode.Manual:
            //     UpdateManualAim();
            //     break;
        }
    }

    /// <summary>
    /// 跟随移动方向模式：读取输入轴，有输入时更新方向，无输入时保持最后朝向。
    /// </summary>
    private void UpdateFollowMovement()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        Vector2 input = new Vector2(moveX, moveY);

        // 只在有实际输入时更新方向（停下时保持最后方向，和 Vampire Survivors 一致）
        if (input.sqrMagnitude > 0.01f)
        {
            AimDirection = input.normalized;
        }
    }

    // =======================================================================
    // 预留：手动瞄准模式（后续实现时取消注释并扩展）
    // =======================================================================
    // private void UpdateManualAim()
    // {
    //     // 鼠标方案：
    //     // Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    //     // AimDirection = ((Vector2)(mouseWorld - transform.position)).normalized;
    //
    //     // 右摇杆方案：
    //     // float aimX = Input.GetAxisRaw("RightStickHorizontal");
    //     // float aimY = Input.GetAxisRaw("RightStickVertical");
    //     // Vector2 aimInput = new Vector2(aimX, aimY);
    //     // if (aimInput.sqrMagnitude > 0.1f)
    //     //     AimDirection = aimInput.normalized;
    // }
}
```

</details>

---

### 6. `Assets/Scripts/UI/ExpBarUI.cs` [新增文件]

**用途**：经验等级条 UI 控制器。

<details>
<summary>点击展开完整代码</summary>

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 经验等级条 UI 控制器。
/// 职责：每帧从 PlayerStats 读取经验/等级数据，驱动填充条与文本显示。
///
/// 【Prefab 搭建指南】
/// 在已有的 Screen Space - Overlay Canvas 下创建如下层级：
///
/// ExpBarContainer (空物体，锚定屏幕顶部，拉满宽度)
///   ├── BgBar        (Image, 深色半透明底条，Anchor 拉满父级)
///   ├── FillBar      (Image, 亮色填充条，Image Type = Filled, Fill Method = Horizontal)
///   ├── LevelText    (TextMeshProUGUI, 锚定左侧，显示 "Lv.X")
///   └── ExpText      (TextMeshProUGUI, 锚定右侧，显示 "72 / 120")
///
/// 把此脚本挂在 ExpBarContainer 上，拖入对应引用即可。
/// </summary>
public class ExpBarUI : MonoBehaviour
{
    [Header("UI 引用")]
    [Tooltip("填充条 Image（Image Type 设为 Filled, Fill Method = Horizontal）")]
    public Image fillBar;

    [Tooltip("等级文本（显示 Lv.X）")]
    public TextMeshProUGUI levelText;

    [Tooltip("经验数值文本（显示 当前/需要），可选，不拖则不显示数值")]
    public TextMeshProUGUI expText;

    [Header("表现配置")]
    [Tooltip("填充条平滑过渡速度。越大越快跟上实际值，0 = 无平滑直接跳变。")]
    [SerializeField] private float fillSmoothSpeed = 8f;

    [Tooltip("填充条颜色（可在 Inspector 中调整风格）")]
    [SerializeField] private Color fillColor = new Color(0.2f, 0.85f, 1f, 1f); // 亮青色

    // 运行时引用
    private PlayerStats playerStats;
    private float displayFillAmount; // 用于平滑过渡的当前显示值

    private void Start()
    {
        // 获取 PlayerStats 引用
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerStats = player.GetComponent<PlayerStats>();
        }

        if (playerStats == null)
        {
            Debug.LogWarning("[ExpBarUI] 未找到 PlayerStats，经验条无法工作。");
        }

        // 初始化填充条颜色
        if (fillBar != null)
        {
            fillBar.color = fillColor;
        }

        // 立即刷新一次，避免第一帧显示空白
        UpdateUI();
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (playerStats == null) return;

        // --- 计算填充比例 ---
        float targetFill = 0f;
        if (playerStats.expToNextLevel > 0f)
        {
            targetFill = Mathf.Clamp01(playerStats.currentExp / playerStats.expToNextLevel);
        }

        // --- 平滑过渡 ---
        if (fillSmoothSpeed > 0f)
        {
            // 使用 unscaledDeltaTime 确保暂停时（升级面板）经验条仍能完成动画
            displayFillAmount = Mathf.Lerp(displayFillAmount, targetFill, fillSmoothSpeed * Time.unscaledDeltaTime);

            // 接近目标值时直接吸附，避免无限逼近
            if (Mathf.Abs(displayFillAmount - targetFill) < 0.005f)
            {
                displayFillAmount = targetFill;
            }
        }
        else
        {
            displayFillAmount = targetFill;
        }

        // --- 应用到 UI ---
        if (fillBar != null)
        {
            fillBar.fillAmount = displayFillAmount;
        }

        if (levelText != null)
        {
            levelText.text = $"Lv.{playerStats.currentLevel}";
        }

        if (expText != null)
        {
            int currentExpDisplay = Mathf.FloorToInt(playerStats.currentExp);
            int needExpDisplay = Mathf.CeilToInt(playerStats.expToNextLevel);
            expText.text = $"{currentExpDisplay} / {needExpDisplay}";
        }
    }

    /// <summary>
    /// 升级瞬间调用：立即将填充条归零（跳过平滑），提供清晰的"升级重置"反馈。
    /// 可由 PlayerStats 在升级时通过事件或直接调用触发。
    /// </summary>
    public void OnLevelUp()
    {
        displayFillAmount = 0f;
        if (fillBar != null)
        {
            fillBar.fillAmount = 0f;
        }
    }
}
```

</details>

---

## 修改文件

---

### 7. `Assets/Scripts/IDamageable.cs`

#### [修改代码块] 接口定义扩展

**Before：**
```csharp
public interface IDamageable
{
    void TakeDamage(float damage);
}
```

**After：**
```csharp
using UnityEngine;

/// <summary>
/// 可受击接口。
/// 所有能承受伤害的实体（怪物、Boss、可破坏物）必须实现此接口。
/// 配合 TryGetComponent 使用，彻底解耦攻击方与受击方。
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// 承受伤害（简易版，默认非暴击）。
    /// </summary>
    /// <param name="damage">伤害值</param>
    void TakeDamage(float damage);

    /// <summary>
    /// 承受伤害（完整版，支持暴击标记）。
    /// 默认实现委托给简易版，保持向后兼容。
    /// </summary>
    /// <param name="damage">伤害值</param>
    /// <param name="isCritical">是否暴击（影响飘字颜色与大小）</param>
    void TakeDamage(float damage, bool isCritical)
    {
        // C# 8.0 接口默认实现：未覆写的实现者自动走这里
        TakeDamage(damage);
    }
}
```

**改动说明**：新增带暴击标记的重载，利用 C# 8 默认实现保持向后兼容，已有的 IDamageable 实现者无需强制覆写新方法。

---

### 8. `Assets/Scripts/Enemy/EnemyBase.cs`

#### [新增代码块] 字段与 Awake 缓存

**新增字段：**
```csharp
// 受击反馈组件缓存（Awake 时自动获取，不强制依赖）
private HitFlash hitFlash;
```

**Awake 中新增：**
```csharp
// 尝试获取闪白组件（可能挂在自身或子物体上）
hitFlash = GetComponent<HitFlash>();
if (hitFlash == null)
{
    hitFlash = GetComponentInChildren<HitFlash>();
}
```

#### [修改代码块] TakeDamage 方法重写

**Before：**
```csharp
public void TakeDamage(float damage)
{
    currentHealth -= damage;
    // TODO: 这里可以播放受击闪白特效或飘字 UI
    if (currentHealth <= 0)
    {
        Die();
    }
}
```

**After：**
```csharp
// =====================================================================
// IDamageable 实现
// =====================================================================

/// <summary>
/// 简易版受击（向后兼容，默认非暴击）。
/// </summary>
public void TakeDamage(float damage)
{
    TakeDamage(damage, false);
}

/// <summary>
/// 完整版受击：扣血 + 触发受击闪白 + 弹出伤害飘字。
/// </summary>
/// <param name="damage">伤害值</param>
/// <param name="isCritical">是否暴击</param>
public void TakeDamage(float damage, bool isCritical)
{
    currentHealth -= damage;

    // --- 受击闪白 ---
    if (hitFlash != null)
    {
        hitFlash.TriggerFlash();
    }

    // --- 伤害飘字 ---
    if (DamagePopupManager.Instance != null)
    {
        DamagePopupManager.Instance.Show(damage, transform.position, isCritical);
    }

    // --- 死亡判定 ---
    if (currentHealth <= 0)
    {
        Die();
    }
}
```

**改动说明**：原单一方法拆为两层。简易版委托给完整版（isCritical=false）。完整版依次触发闪白、飘字、死亡判定。

---

### 9. `Assets/Scripts/Weapon/AuraDamageZone.cs`

#### [新增代码块] OnEnable 生命周期

**新增：**
```csharp
/// <summary>
/// 对象池取出时（首次激活）清空追踪列表，确保不残留上一次生命周期的数据。
/// </summary>
private void OnEnable()
{
    targets.Clear();
}
```

#### [修改代码块] Initialize 方法

**Before：**
```csharp
public void Initialize(WeaponDataSO data, Transform target, float overrideTickInterval, float damage, float lifeTimeValue, float radius)
{
    followTarget = target;
    tickInterval = Mathf.Max(0.01f, overrideTickInterval);
    if (circleCollider != null)
        circleCollider.radius = Mathf.Max(0.05f, radius);

    // 重置运行态
    currentDamage = damage;
    lifeTimer = lifeTimeValue;
    tickTimer = 0f;
    targets.Clear();       // ← 这里是问题根源
    RefreshRangeVisual();
}
```

**After：**
```csharp
// 供 AuraWeapon 注入"当前等级快照"属性
public void Initialize(WeaponDataSO data, Transform target, float overrideTickInterval, float damage, float lifeTimeValue, float radius)
{
    followTarget = target;
    tickInterval = Mathf.Max(0.01f, overrideTickInterval);
    if (circleCollider != null)
    {
        circleCollider.radius = Mathf.Max(0.05f, radius);
    }

    // 重置运行态（注意：不再清空 targets！
    // targets 在 OnEnable 时已清空；运行中 Re-Initialize 属于"刷新参数"场景，
    // 不应清空已追踪的范围内敌人，否则已在内部的怪物无法重新触发 OnTriggerEnter2D）
    currentDamage = damage;
    lifeTimer = lifeTimeValue;
    tickTimer = 0f;
    RefreshRangeVisual();
}
```

**改动说明**：修复 Bug——`targets.Clear()` 移至 `OnEnable()`（仅对象池首次取出时清空）。运行中刷新参数时保留已在范围内的敌人追踪列表。

---

### 10. `Assets/Scripts/LevelUpManager.cs`

#### [修改代码块] ShowLevelUpUI 方法

**Before：**
```csharp
public void ShowLevelUpUI()
{
    Time.timeScale = 0f;
    levelUpPanel.SetActive(true);

    foreach (var btn in activeButtons)
    {
        if (btn != null) Destroy(btn.gameObject);
    }
    activeButtons.Clear();

    List<UpgradeDataSO> pool = BuildSelectableUpgradePool();

    int count = Mathf.Min(upgradeChoiceCount, pool.Count);
    // ... 生成按钮逻辑 ...
}
```

**After：**
```csharp
/// <summary>
/// 触发升级面板：清除旧按钮 → 抽候选 → 动态生成 Prefab
/// </summary>
public void ShowLevelUpUI()
{
    // 候选池：过滤掉已拥有且已满级的武器
    List<UpgradeDataSO> pool = BuildSelectableUpgradePool();

    // 保底处理：如果没有可选升级项，直接跳过面板，恢复游戏
    if (pool.Count == 0)
    {
        Debug.Log("[LevelUpManager] 候选池为空（所有武器已满级），跳过升级面板。");
        // 依然检查队列中是否有残余升级
        playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
        return;
    }

    Time.timeScale = 0f; // 暂停时间，怪物和子弹全部冻结
    levelUpPanel.SetActive(true);

    // 销毁上一次生成的按钮（避免残留旧候选）
    foreach (var btn in activeButtons)
    {
        if (btn != null) Destroy(btn.gameObject);
    }
    activeButtons.Clear();

    // 动态生成按钮 Prefab，数量取"期望数量"和"候选池数量"的较小值
    int count = Mathf.Min(upgradeChoiceCount, pool.Count);
    for (int i = 0; i < count; i++)
    {
        int randomIndex = Random.Range(0, pool.Count);
        UpgradeDataSO selectedUpgrade = pool[randomIndex];
        pool.RemoveAt(randomIndex); // 防止同一选项出现两次

        // 在 buttonContainer 下生成 Prefab
        GameObject btnObj = Instantiate(upgradeButtonPrefab, buttonContainer);
        if (btnObj.TryGetComponent<UpgradeUIItem>(out var uiItem))
        {
            uiItem.Setup(selectedUpgrade);
            activeButtons.Add(uiItem);
        }
    }
}
```

**改动说明**：修复 Bug——候选池构建移至方法开头，为空时直接 return（不暂停、不展示面板），防止全满级时游戏卡死。

---

### 11. `Assets/Scripts/Weapon/WeaponBase.cs`

#### [新增代码块] 字段与 Awake 方法

**新增：**
```csharp
// 瞄准控制器缓存（从 Player 父级获取）
protected AimController aimController;

protected virtual void Awake()
{
    // 武器挂在 Player 子物体上，向上查找 AimController
    aimController = GetComponentInParent<AimController>();
}
```

#### [修改代码块] Attack() 中的方向获取

**Before：**
```csharp
// TODO: 结合 PlayerController 获取最近敌人方向；目前以 Vector3.right 作为占位方向
Vector3 baseDirection = Vector3.right;
```

**After：**
```csharp
// TODO: 结合 PlayerController 获取最近敌人方向；目前以 Vector3.right 作为占位方向
Vector3 baseDirection = GetAimDirection();
```

#### [新增代码块] GetAimDirection 辅助方法

**新增（文件末尾）：**
```csharp
/// <summary>
/// 获取当前发射方向。优先从 AimController 读取；
/// 若未找到（如武器不在玩家身上），回退到默认右方向。
/// </summary>
protected Vector3 GetAimDirection()
{
    if (aimController != null)
    {
        Vector2 aim = aimController.AimDirection;
        return new Vector3(aim.x, aim.y, 0f).normalized;
    }
    return Vector3.right;
}
```

**改动说明**：弹道武器发射方向从 AimController 读取（跟随移动朝向），替代原来硬编码的 Vector3.right。预留手动瞄准扩展口。

---

### 12. `Assets/Scripts/Player/PlayerStats.cs`

#### [新增代码块] 移动速度属性

**新增（在 expToNextLevel 字段下方）：**
```csharp
[Header("移动属性")]
[Tooltip("基础移动速度")]
public float baseMoveSpeed = 3.0f;
[Tooltip("速度加成（来自升级/道具，叠加计算）。0.2 = 加速 20%")]
public float moveSpeedBonus = 0f;

/// <summary>
/// 最终移动速度 = 基础 × (1 + 加成百分比)。
/// PlayerController 每帧读取此值。
/// </summary>
public float FinalMoveSpeed => baseMoveSpeed * (1f + moveSpeedBonus);
```

**改动说明**：移动速度纳入 PlayerStats 统一管理，支持 base + bonus% 公式，升级系统可直接修改 moveSpeedBonus。

---

### 13. `Assets/Scripts/Player/PlayerController.cs`

#### [修改代码块] 字段声明

**Before：**
```csharp
[Header("移动参数")]
[Tooltip("玩家基础移动速度")]
[SerializeField] private float moveSpeed = 5.0f;

// ...
private Rigidbody2D rb;
private Vector2 movementInput;
```

**After：**
```csharp
[Header("移动参数")]
[Tooltip("后备移动速度（仅当未找到 PlayerStats 时使用）")]
[SerializeField] private float fallbackMoveSpeed = 3.0f;

[Header("组件引用")]
[Tooltip("角色动画控制器")]
[SerializeField] private Animator animator;
[Tooltip("角色精灵渲染器，用于翻转朝向")]
[SerializeField] private SpriteRenderer spriteRenderer;

// 内部缓存
private Rigidbody2D rb;
private PlayerStats playerStats;
private Vector2 movementInput;

// 动画参数哈希值缓存（性能优化：比直接传字符串快得多）
private readonly int isMovingHash = Animator.StringToHash("IsMoving");
```

#### [修改代码块] Awake 方法

**Before：**
```csharp
private void Awake()
{
    rb = GetComponent<Rigidbody2D>();
    // ...
}
```

**After：**
```csharp
private void Awake()
{
    // 缓存自身组件，避免运行时 GetComponent 产生开销
    rb = GetComponent<Rigidbody2D>();
    playerStats = GetComponent<PlayerStats>();

    // 安全校验：如果未在面板拖拽赋值，尝试自动获取子物体的表现组件
    if (animator == null) animator = GetComponentInChildren<Animator>();
    if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
}
```

#### [修改代码块] FixedUpdate 方法

**Before：**
```csharp
private void FixedUpdate()
{
    rb.velocity = movementInput * moveSpeed;
}
```

**After：**
```csharp
private void FixedUpdate()
{
    // 物理层：在 FixedUpdate 中处理刚体移动，确保帧率波动时移动平滑且碰撞稳定
    // 从 PlayerStats 读取最终速度（含升级加成），找不到则用后备值
    float speed = playerStats != null ? playerStats.FinalMoveSpeed : fallbackMoveSpeed;
    rb.velocity = movementInput * speed;
}
```

**改动说明**：移速不再使用本地固定值，改为每帧从 PlayerStats.FinalMoveSpeed 读取（含升级加成）。默认从 5.0 降至 3.0 改善手感。
