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
/// ExpBarContainer (RectTransform, 锚定屏幕顶部的透明定位容器)
///   ├── ExpBarFrame  (Image, 左右留白后的实心外框)
///   │   └── ExpBarTrack (Image, 深色内轨道)
///   │       ├── FillBar  (Image, 无 Sprite 的纯色矩形，通过 RectTransform 宽度显示进度)
///   │       ├── LevelText (TextMeshProUGUI, 锚定轨道左侧，显示 "Lv.X")
///   │       └── ExpText   (TextMeshProUGUI, 居中显示 "72 / 120")
///
/// 把此脚本挂在 ExpBarContainer 上，拖入对应引用即可。
/// </summary>
public class ExpBarUI : MonoBehaviour
{
    [Header("UI 引用")]
    [Tooltip("无 Sprite 的纯色填充 Image；脚本通过 RectTransform 宽度显示进度。")]
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

    [Header("文字样式")]
    [Tooltip("经验栏文字颜色。")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("经验栏文字描边颜色。")]
    [SerializeField] private Color outlineColor = new Color(0.02f, 0.04f, 0.06f, 1f);

    [Tooltip("经验栏文字描边宽度。")]
    [Range(0f, 1f)]
    [SerializeField] private float outlineWidth = 0.24f;

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

        // 使用无 Sprite 的纯色四边形，避免内置 UI Sprite 的透明边缘造成渐变。
        if (fillBar != null)
        {
            fillBar.sprite = null;
            fillBar.type = Image.Type.Simple;
            fillBar.color = fillColor;
        }

        ApplyTextStyle(levelText);
        ApplyTextStyle(expText);

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
        ApplyFillWidth(displayFillAmount);

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
        ApplyFillWidth(0f);
    }

    /// <summary>
    /// 把经验比例转换为左对齐纯色矩形的宽度。
    /// 不使用 Image.fillAmount，避免任何 Sprite 透明边或渐变参与进度显示。
    /// </summary>
    private void ApplyFillWidth(float fillAmount)
    {
        if (fillBar == null)
        {
            return;
        }

        RectTransform fillRect = fillBar.rectTransform;
        Vector2 anchorMin = fillRect.anchorMin;
        Vector2 anchorMax = fillRect.anchorMax;
        anchorMin.x = 0f;
        anchorMax.x = Mathf.Clamp01(fillAmount);
        fillRect.anchorMin = anchorMin;
        fillRect.anchorMax = anchorMax;
    }

    /// <summary>为经验栏文字应用统一的粗体与描边样式，只在初始化时修改材质实例。</summary>
    private void ApplyTextStyle(TMP_Text targetText)
    {
        if (targetText == null)
        {
            return;
        }

        targetText.color = textColor;
        targetText.fontStyle = FontStyles.Bold;
        targetText.enableWordWrapping = false;

        Material textMaterial = targetText.fontMaterial;
        if (textMaterial == null)
        {
            return;
        }

        if (textMaterial.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        }

        if (textMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
        {
            textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        }
    }
}
