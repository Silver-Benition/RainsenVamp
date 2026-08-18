using TMPro;
using UnityEngine;

/// <summary>
/// 双世界波次调试信息面板。
/// 只读取协调器和两个世界波次管理器的运行时状态，不参与游戏逻辑。
/// </summary>
public class WorldWaveDebugUI : MonoBehaviour
{
    [SerializeField] private WorldLineCoordinator coordinator;
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private bool visible = true;

    /// <summary>自动寻找协调器并初始化调试面板。</summary>
    private void Awake()
    {
        if (coordinator == null) coordinator = FindObjectOfType<WorldLineCoordinator>();
        if (debugText == null) debugText = GetComponentInChildren<TextMeshProUGUI>();
        gameObject.SetActive(visible);
    }

    /// <summary>创建运行时调试文本，避免场景手写 TMP 材质引用。</summary>
    private TextMeshProUGUI CreateRuntimeLabel()
    {
        GameObject labelObject = new GameObject("WorldWaveDebugText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(transform, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -55f);
        rect.sizeDelta = new Vector2(520f, 90f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = 16f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        return label;
    }
    /// <summary>按未缩放时间刷新文本，让升级暂停期间也能显示稳定状态。</summary>
    private void Update()
    {
        if (coordinator == null || debugText == null) return;

        WorldWaveManager main = coordinator.MainWorldWaveManager;
        WorldWaveManager sub = coordinator.SubWorldWaveManager;
        debugText.text = string.Format(
            "WORLD: {0}\nMAIN   ENEMIES {1}   TIME {2}\nSUB    ENEMIES {3}   TIME {4}",
            coordinator.MainWorldIsActive ? "MAIN" : "SUB",
            main != null ? main.ActiveEnemyCount.ToString() : "-",
            FormatTime(main != null ? main.Elapsed : 0f),
            sub != null ? sub.ActiveEnemyCount.ToString() : "-",
            FormatTime(sub != null ? sub.Elapsed : 0f));
    }

    /// <summary>将波次秒数格式化为分钟和两位秒数。</summary>
    private string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
    }
}