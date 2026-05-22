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

public class HitFlash : MonoBehaviour
{
    [Header("闪白配置")]
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private Color flashColor = Color.white;

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock mpb;

    private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

    private Coroutine flashCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    public void TriggerFlash()
    {
        if (spriteRenderer == null) return;
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    public void TriggerFlash(Color overrideColor)
    {
        flashColor = overrideColor;
        TriggerFlash();
    }

    private IEnumerator FlashRoutine()
    {
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(FlashColorID, flashColor);
        mpb.SetFloat(FlashAmountID, 1f);
        spriteRenderer.SetPropertyBlock(mpb);

        yield return new WaitForSeconds(flashDuration);

        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(FlashAmountID, 0f);
        spriteRenderer.SetPropertyBlock(mpb);
        flashCoroutine = null;
    }

    private void OnDisable()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
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

[RequireComponent(typeof(TextMeshPro))]
public class DamagePopup : MonoBehaviour, IPoolable
{
    [Header("运动参数")]
    [SerializeField] private float lifetime = 0.8f;
    [SerializeField] private float floatSpeed = 2.0f;
    [SerializeField] private float randomOffsetX = 0.3f;

    [Header("缩放动画")]
    [SerializeField] private float popScale = 1.5f;
    [SerializeField] private float popDuration = 0.15f;

    [Header("暴击加成")]
    [SerializeField] private float critScaleMultiplier = 1.4f;

    private TextMeshPro textMesh;
    private GameObject prefabReference;
    private float timer;
    private float fadeStartTime;
    private Vector3 baseScale;
    private Color baseColor;
    private bool isActive;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        baseScale = Vector3.one;
    }

    private void OnEnable()
    {
        timer = 0f;
        isActive = false;
    }

    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    public void Initialize(float damage, bool isCritical, Color normalColor, Color critColor)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();

        int displayValue = Mathf.RoundToInt(damage);
        if (textMesh != null)
            textMesh.text = isCritical ? $"{displayValue}!" : displayValue.ToString();

        baseColor = isCritical ? critColor : normalColor;
        if (textMesh != null)
            textMesh.color = baseColor;

        float scale = isCritical ? popScale * critScaleMultiplier : popScale;
        transform.localScale = baseScale * scale;

        Vector3 pos = transform.position;
        pos.x += Random.Range(-randomOffsetX, randomOffsetX);
        transform.position = pos;

        timer = 0f;
        fadeStartTime = lifetime * 0.5f;
        isActive = true;
    }

    private void Update()
    {
        if (!isActive) return;
        timer += Time.deltaTime;

        transform.position += Vector3.up * floatSpeed * Time.deltaTime;

        if (timer < popDuration)
        {
            float t = timer / popDuration;
            float currentScaleValue = Mathf.Lerp(popScale, 1f, t);
            transform.localScale = baseScale * currentScaleValue;
        }
        else if (timer < popDuration + 0.05f)
        {
            transform.localScale = baseScale;
        }

        if (timer >= fadeStartTime && textMesh != null)
        {
            float fadeProgress = (timer - fadeStartTime) / (lifetime - fadeStartTime);
            fadeProgress = Mathf.Clamp01(fadeProgress);
            float alpha = Mathf.Lerp(1f, 0f, fadeProgress);
            Color c = baseColor;
            c.a = alpha;
            textMesh.color = c;
        }

        if (timer >= lifetime)
            ReturnToPool();
    }

    private void ReturnToPool()
    {
        isActive = false;
        if (prefabReference != null && PoolManager.Instance != null)
            PoolManager.Instance.Release(prefabReference, gameObject);
        else
            gameObject.SetActive(false);
    }

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

public class DamagePopupManager : MonoBehaviour
{
    public static DamagePopupManager Instance { get; private set; }

    [Header("飘字 Prefab")]
    public GameObject popupPrefab;

    [Header("颜色配置")]
    public Color normalColor = Color.white;
    public Color criticalColor = new Color(1f, 0.45f, 0.1f, 1f);

    [Header("生成偏移")]
    public float spawnOffsetY = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Show(float damage, Vector3 worldPosition, bool isCritical = false)
    {
        if (popupPrefab == null) return;
        Vector3 spawnPos = worldPosition + Vector3.up * spawnOffsetY;
        GameObject obj = PoolManager.Instance.Spawn(popupPrefab, spawnPos, Quaternion.identity);
        if (obj == null) return;
        if (obj.TryGetComponent<DamagePopup>(out var popup))
            popup.Initialize(damage, isCritical, normalColor, criticalColor);
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

public class AimController : MonoBehaviour
{
    public enum AimMode
    {
        FollowMovement = 0,
        // Manual = 1,       // 预留：鼠标/右摇杆
        // NearestEnemy = 2, // 预留：自动锁定
    }

    [Header("瞄准模式")]
    public AimMode aimMode = AimMode.FollowMovement;

    [Header("默认朝向")]
    [SerializeField] private Vector2 defaultDirection = Vector2.right;

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
        }
    }

    private void UpdateFollowMovement()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        Vector2 input = new Vector2(moveX, moveY);
        if (input.sqrMagnitude > 0.01f)
            AimDirection = input.normalized;
    }
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

public class ExpBarUI : MonoBehaviour
{
    [Header("UI 引用")]
    public Image fillBar;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI expText;

    [Header("表现配置")]
    [SerializeField] private float fillSmoothSpeed = 8f;
    [SerializeField] private Color fillColor = new Color(0.2f, 0.85f, 1f, 1f);

    private PlayerStats playerStats;
    private float displayFillAmount;

    private void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) playerStats = player.GetComponent<PlayerStats>();
        if (fillBar != null) fillBar.color = fillColor;
        UpdateUI();
    }

    private void Update() { UpdateUI(); }

    private void UpdateUI()
    {
        if (playerStats == null) return;

        float targetFill = 0f;
        if (playerStats.expToNextLevel > 0f)
            targetFill = Mathf.Clamp01(playerStats.currentExp / playerStats.expToNextLevel);

        if (fillSmoothSpeed > 0f)
        {
            displayFillAmount = Mathf.Lerp(displayFillAmount, targetFill, fillSmoothSpeed * Time.unscaledDeltaTime);
            if (Mathf.Abs(displayFillAmount - targetFill) < 0.005f)
                displayFillAmount = targetFill;
        }
        else
            displayFillAmount = targetFill;

        if (fillBar != null) fillBar.fillAmount = displayFillAmount;
        if (levelText != null) levelText.text = $"Lv.{playerStats.currentLevel}";
        if (expText != null)
        {
            int cur = Mathf.FloorToInt(playerStats.currentExp);
            int need = Mathf.CeilToInt(playerStats.expToNextLevel);
            expText.text = $"{cur} / {need}";
        }
    }

    public void OnLevelUp()
    {
        displayFillAmount = 0f;
        if (fillBar != null) fillBar.fillAmount = 0f;
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
public interface IDamageable
{
    void TakeDamage(float damage);

    // C# 8 默认接口实现：未覆写的实现者自动委托给简易版
    void TakeDamage(float damage, bool isCritical)
    {
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
private HitFlash hitFlash;
```

**Awake 中新增：**
```csharp
hitFlash = GetComponent<HitFlash>();
if (hitFlash == null)
    hitFlash = GetComponentInChildren<HitFlash>();
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
public void TakeDamage(float damage)
{
    TakeDamage(damage, false);
}

public void TakeDamage(float damage, bool isCritical)
{
    currentHealth -= damage;

    if (hitFlash != null)
        hitFlash.TriggerFlash();

    if (DamagePopupManager.Instance != null)
        DamagePopupManager.Instance.Show(damage, transform.position, isCritical);

    if (currentHealth <= 0)
        Die();
}
```

**改动说明**：原单一方法拆为两层。简易版委托给完整版（isCritical=false）。完整版依次触发闪白、飘字、死亡判定。

---

### 9. `Assets/Scripts/Weapon/AuraDamageZone.cs`

#### [新增代码块] OnEnable 生命周期

**新增：**
```csharp
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
public void Initialize(WeaponDataSO data, Transform target, float overrideTickInterval, float damage, float lifeTimeValue, float radius)
{
    followTarget = target;
    tickInterval = Mathf.Max(0.01f, overrideTickInterval);
    if (circleCollider != null)
        circleCollider.radius = Mathf.Max(0.05f, radius);

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
public void ShowLevelUpUI()
{
    List<UpgradeDataSO> pool = BuildSelectableUpgradePool();

    // 保底处理：如果没有可选升级项，直接跳过面板，恢复游戏
    if (pool.Count == 0)
    {
        Debug.Log("[LevelUpManager] 候选池为空（所有武器已满级），跳过升级面板。");
        playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
        return;
    }

    Time.timeScale = 0f;
    levelUpPanel.SetActive(true);

    foreach (var btn in activeButtons)
    {
        if (btn != null) Destroy(btn.gameObject);
    }
    activeButtons.Clear();

    int count = Mathf.Min(upgradeChoiceCount, pool.Count);
    // ... 生成按钮逻辑 ...
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

// ...
private Rigidbody2D rb;
private PlayerStats playerStats;
private Vector2 movementInput;
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
    rb = GetComponent<Rigidbody2D>();
    playerStats = GetComponent<PlayerStats>();
    // ...
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
    float speed = playerStats != null ? playerStats.FinalMoveSpeed : fallbackMoveSpeed;
    rb.velocity = movementInput * speed;
}
```

**改动说明**：移速不再使用本地固定值，改为每帧从 PlayerStats.FinalMoveSpeed 读取（含升级加成）。默认从 5.0 降至 3.0 改善手感。
