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
