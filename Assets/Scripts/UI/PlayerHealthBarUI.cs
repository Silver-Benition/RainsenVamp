using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 跟随玩家的世界空间生命条表现。
/// 通过 PlayerHealth 事件更新无交互 Slider，并将显示值映射到整数像素档位。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHealthBarUI : MonoBehaviour
{
    [Header("数据来源")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private SpriteRenderer playerSpriteRenderer;
    [SerializeField] private Transform healthBarAnchor;

    [Header("UI 引用")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Image borderImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;

    [Header("表现配置")]
    [SerializeField, Min(0f)] private float fillSmoothSpeed = 10f;
    [SerializeField] private Color borderColor = new Color(0.04f, 0.04f, 0.04f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.65f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color fillColor = new Color(0.15f, 0.85f, 0.2f, 1f);

    [Header("角色锚点")]
    [SerializeField, Tooltip("角色翻转时，将锚点相对玩家根节点的水平位置一并镜像。")]
    private bool mirrorAnchorWithSprite = true;

    private float _targetFillAmount = 1f;
    private float _displayFillAmount = 1f;
    private RectTransform _rectTransform;

    /// <summary>解析场景引用并初始化 Slider 与三层颜色。</summary>
    private void Awake()
    {
        ResolvePlayerHealth();
        ResolveVisualReferences();
        if (healthSlider != null)
        {
            healthSlider.interactable = false;
        }

        ApplyColors();
    }

    /// <summary>订阅生命变化事件并立即同步一次显示。</summary>
    private void OnEnable()
    {
        ResolvePlayerHealth();
        ResolveVisualReferences();
        if (playerHealth != null)
        {
            playerHealth.HealthChanged += HandleHealthChanged;
            RefreshImmediately();
        }
    }

    /// <summary>在所有场景对象完成 Awake 后再次同步初始生命，消除跨对象初始化顺序影响。</summary>
    private void Start()
    {
        RefreshImmediately();
    }

    /// <summary>取消事件订阅，避免场景卸载或组件开关后重复监听。</summary>
    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= HandleHealthChanged;
        }
    }

    /// <summary>用非缩放时间平滑追赶目标比例，暂停界面下也能完成过渡。</summary>
    private void Update()
    {
        if (healthSlider == null)
        {
            return;
        }

        _displayFillAmount = fillSmoothSpeed <= 0f
            ? _targetFillAmount
            : Mathf.MoveTowards(
                _displayFillAmount,
                _targetFillAmount,
                fillSmoothSpeed * Time.unscaledDeltaTime);
        SetSliderFill(_displayFillAmount);
    }

    /// <summary>
    /// 在角色朝向更新后，将血条对齐到角色专属锚点。
    /// 锚点 X 可随 SpriteRenderer.flipX 镜像，位置按当前 Sprite PPU 吸附到整数像素。
    /// </summary>
    private void LateUpdate()
    {
        if (_rectTransform == null || healthBarAnchor == null || _rectTransform.parent == null)
        {
            return;
        }

        Vector3 anchorWorldPosition = healthBarAnchor.position;
        Transform playerTransform = playerHealth != null
            ? playerHealth.transform
            : healthBarAnchor.parent;

        if (mirrorAnchorWithSprite
            && playerSpriteRenderer != null
            && playerSpriteRenderer.flipX
            && playerTransform != null)
        {
            Vector3 anchorPlayerLocalPosition = playerTransform.InverseTransformPoint(anchorWorldPosition);
            anchorPlayerLocalPosition.x = -anchorPlayerLocalPosition.x;
            anchorWorldPosition = playerTransform.TransformPoint(anchorPlayerLocalPosition);
        }

        Vector3 anchorLocalPosition = _rectTransform.parent.InverseTransformPoint(anchorWorldPosition);
        Sprite currentSprite = playerSpriteRenderer != null
            ? playerSpriteRenderer.sprite
            : null;
        if (currentSprite != null && currentSprite.pixelsPerUnit > 0f)
        {
            anchorLocalPosition.x = Mathf.Round(anchorLocalPosition.x * currentSprite.pixelsPerUnit)
                / currentSprite.pixelsPerUnit;
            anchorLocalPosition.y = Mathf.Round(anchorLocalPosition.y * currentSprite.pixelsPerUnit)
                / currentSprite.pixelsPerUnit;
        }

        Vector3 localPosition = _rectTransform.localPosition;
        if (Mathf.Approximately(localPosition.x, anchorLocalPosition.x)
            && Mathf.Approximately(localPosition.y, anchorLocalPosition.y))
        {
            return;
        }

        localPosition.x = anchorLocalPosition.x;
        localPosition.y = anchorLocalPosition.y;
        _rectTransform.localPosition = localPosition;
    }

    /// <summary>未显式绑定时通过 Player Tag 查找一次生命组件。</summary>
    private void ResolvePlayerHealth()
    {
        if (playerHealth != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();
        }
    }

    /// <summary>缓存显示引用，并按约定名称查找未显式绑定的角色血条锚点。</summary>
    private void ResolveVisualReferences()
    {
        if (_rectTransform == null)
        {
            _rectTransform = transform as RectTransform;
        }

        if (playerHealth == null)
        {
            return;
        }

        if (playerSpriteRenderer == null)
        {
            playerSpriteRenderer = playerHealth.GetComponentInChildren<SpriteRenderer>();
        }

        if (healthBarAnchor == null)
        {
            healthBarAnchor = playerHealth.transform.Find("HealthBarAnchor");
        }
    }

    /// <summary>读取当前生命并同步显示，避免 UI 启用首帧显示旧值。</summary>
    private void RefreshImmediately()
    {
        if (playerHealth == null)
        {
            return;
        }

        _targetFillAmount = playerHealth.NormalizedHealth;
        _displayFillAmount = _targetFillAmount;
        SetSliderFill(_displayFillAmount);
    }

    /// <summary>接收生命变化事件并更新目标比例。</summary>
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        _targetFillAmount = maxHealth > 0f
            ? Mathf.Clamp01(currentHealth / maxHealth)
            : 0f;
    }

    /// <summary>应用黑色边框、红色底条与绿色当前血量颜色。</summary>
    private void ApplyColors()
    {
        if (borderImage != null)
        {
            borderImage.color = borderColor;
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = backgroundColor;
        }

        if (fillImage != null)
        {
            fillImage.color = fillColor;
        }
    }

    /// <summary>将标准化生命值映射到 Slider 范围，由整数档位保证填充边缘落在像素列上。</summary>
    private void SetSliderFill(float normalizedHealth)
    {
        if (healthSlider == null)
        {
            return;
        }

        float sliderValue = Mathf.Lerp(
            healthSlider.minValue,
            healthSlider.maxValue,
            Mathf.Clamp01(normalizedHealth));
        healthSlider.SetValueWithoutNotify(sliderValue);
    }
}
