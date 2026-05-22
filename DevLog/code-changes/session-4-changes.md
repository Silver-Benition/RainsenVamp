# Session 4 代码改动详情

## 新增文件

---

### 1. `Assets/Shaders/Sprites-FlashWhite.shader`
**用途**：在 Unity 默认 Sprite 渲染基础上增加 `_FlashAmount` 闪白通道。

**核心逻辑**：
```hlsl
// Fragment Shader 核心行：将 RGB 向 FlashColor 做线性插值
col.rgb = lerp(col.rgb, _FlashColor.rgb * col.a, _FlashAmount);
```

**说明**：
- `_FlashAmount = 0` → 正常渲染
- `_FlashAmount = 1` → 整体变为 `_FlashColor`（默认白色）
- 配合 `MaterialPropertyBlock` 使用，不创建材质实例，适合海量实体
- 兼容 Built-in Render Pipeline；若使用 URP 需改为 URP Sprite 变体

---

### 2. `Assets/Scripts/VFX/HitFlash.cs`
**用途**：受击闪白组件，挂在怪物身上。

**核心逻辑**：
```csharp
// 触发闪白
public void TriggerFlash()
{
    if (flashCoroutine != null) StopCoroutine(flashCoroutine);
    flashCoroutine = StartCoroutine(FlashRoutine());
}

private IEnumerator FlashRoutine()
{
    // 设置 _FlashAmount = 1（全白）
    spriteRenderer.GetPropertyBlock(mpb);
    mpb.SetFloat(FlashAmountID, 1f);
    spriteRenderer.SetPropertyBlock(mpb);

    yield return new WaitForSeconds(flashDuration); // 默认 0.1s

    // 恢复 _FlashAmount = 0
    mpb.SetFloat(FlashAmountID, 0f);
    spriteRenderer.SetPropertyBlock(mpb);
}
```

**设计要点**：
- 使用 `MaterialPropertyBlock` 而非材质实例 → 零 GC
- 多次连续受击时打断旧协程重新开始 → 每次受击都有反馈
- `OnDisable` 时重置状态 → 对象池复用不残留白色

---

### 3. `Assets/Scripts/VFX/DamagePopup.cs`
**用途**：伤害飘字单体，纯代码驱动动画。

**核心逻辑**：
```csharp
public void Initialize(float damage, bool isCritical, Color normalColor, Color critColor)
{
    textMesh.text = isCritical ? $"{displayValue}!" : displayValue.ToString();
    baseColor = isCritical ? critColor : normalColor;
    transform.localScale = baseScale * (isCritical ? popScale * critScaleMultiplier : popScale);
    timer = 0f;
    isActive = true;
}

private void Update()
{
    if (!isActive) return;
    timer += Time.deltaTime;
    transform.position += Vector3.up * floatSpeed * Time.deltaTime; // 漂浮
    // ... 缩放回弹 + Alpha 淡出 ...
    if (timer >= lifetime) ReturnToPool();
}
```

**设计要点**：
- `isActive` 标志位防止 OnEnable → Update 之间的空帧执行
- `OnEnable` 重置 timer，确保对象池复用时状态干净
- 支持普通（白色）/ 暴击（橙红 + 放大 + 感叹号）两种表现

---

### 4. `Assets/Scripts/Core/DamagePopupManager.cs`
**用途**：飘字管理器单例，统一调度生成。

**核心接口**：
```csharp
public void Show(float damage, Vector3 worldPosition, bool isCritical = false)
{
    Vector3 spawnPos = worldPosition + Vector3.up * spawnOffsetY;
    GameObject obj = PoolManager.Instance.Spawn(popupPrefab, spawnPos, Quaternion.identity);
    obj.GetComponent<DamagePopup>().Initialize(damage, isCritical, normalColor, criticalColor);
}
```

---

### 5. `Assets/Scripts/Player/AimController.cs`
**用途**：瞄准方向控制器，解耦武器发射方向与玩家移动。

**核心逻辑**：
```csharp
public Vector2 AimDirection { get; private set; }

private void UpdateFollowMovement()
{
    Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    // 只在有输入时更新，停下保持最后方向
    if (input.sqrMagnitude > 0.01f)
        AimDirection = input.normalized;
}
```

**扩展预留**：
- `AimMode` 枚举已包含 Manual / NearestEnemy 注释
- 手动瞄准模式代码已预写（注释状态），取消注释即可启用

---

### 6. `Assets/Scripts/UI/ExpBarUI.cs`
**用途**：经验等级条 UI 控制器。

**核心逻辑**：
```csharp
private void UpdateUI()
{
    float targetFill = Mathf.Clamp01(playerStats.currentExp / playerStats.expToNextLevel);
    // 平滑过渡（unscaledDeltaTime 确保暂停时也能完成动画）
    displayFillAmount = Mathf.Lerp(displayFillAmount, targetFill, fillSmoothSpeed * Time.unscaledDeltaTime);
    fillBar.fillAmount = displayFillAmount;
    levelText.text = $"Lv.{playerStats.currentLevel}";
}
```

---

## 修改文件

---

### 7. `Assets/Scripts/IDamageable.cs`（修改）
**改动**：新增带暴击标记的重载，使用 C# 8 默认接口实现保持向后兼容。

```csharp
public interface IDamageable
{
    void TakeDamage(float damage);
    // 默认实现：未覆写的实现者自动委托给简易版
    void TakeDamage(float damage, bool isCritical) { TakeDamage(damage); }
}
```

---

### 8. `Assets/Scripts/Enemy/EnemyBase.cs`（修改）
**改动**：TakeDamage 拆为两层，接入闪白 + 飘字。

```csharp
public void TakeDamage(float damage) => TakeDamage(damage, false);

public void TakeDamage(float damage, bool isCritical)
{
    currentHealth -= damage;
    if (hitFlash != null) hitFlash.TriggerFlash();
    if (DamagePopupManager.Instance != null)
        DamagePopupManager.Instance.Show(damage, transform.position, isCritical);
    if (currentHealth <= 0) Die();
}
```

---

### 9. `Assets/Scripts/Weapon/AuraDamageZone.cs`（修改）
**改动**：修复光环只在边缘造成伤害的 Bug。

```csharp
// 新增 OnEnable：对象池取出时清空列表
private void OnEnable() { targets.Clear(); }

// Initialize 中移除了 targets.Clear()
// 原因：运行中 Re-Initialize 属于"刷新参数"，不应清空已在范围内的敌人
```

---

### 10. `Assets/Scripts/LevelUpManager.cs`（修改）
**改动**：修复全满级卡死 Bug。

```csharp
public void ShowLevelUpUI()
{
    List<UpgradeDataSO> pool = BuildSelectableUpgradePool();
    // 保底：候选池为空直接跳过
    if (pool.Count == 0)
    {
        playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
        return;
    }
    Time.timeScale = 0f;
    // ... 正常流程 ...
}
```

---

### 11. `Assets/Scripts/Weapon/WeaponBase.cs`（修改）
**改动**：发射方向从 AimController 读取。

```csharp
protected AimController aimController;

protected virtual void Awake()
{
    aimController = GetComponentInParent<AimController>();
}

// Attack() 中：
Vector3 baseDirection = GetAimDirection(); // 替代原来的 Vector3.right

protected Vector3 GetAimDirection()
{
    if (aimController != null)
        return new Vector3(aimController.AimDirection.x, aimController.AimDirection.y, 0f).normalized;
    return Vector3.right;
}
```

---

### 12. `Assets/Scripts/Player/PlayerStats.cs`（修改）
**改动**：新增移动速度属性化管理。

```csharp
[Header("移动属性")]
public float baseMoveSpeed = 3.0f;
public float moveSpeedBonus = 0f;
public float FinalMoveSpeed => baseMoveSpeed * (1f + moveSpeedBonus);
```

---

### 13. `Assets/Scripts/Player/PlayerController.cs`（修改）
**改动**：移速改为从 PlayerStats 读取。

```csharp
private PlayerStats playerStats;

private void Awake()
{
    playerStats = GetComponent<PlayerStats>();
}

private void FixedUpdate()
{
    float speed = playerStats != null ? playerStats.FinalMoveSpeed : fallbackMoveSpeed;
    rb.velocity = movementInput * speed;
}
```
