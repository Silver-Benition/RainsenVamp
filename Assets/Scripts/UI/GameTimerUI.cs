using TMPro;
using UnityEngine;

/// <summary>
/// 显示 RunDirector 权威维护的本局游戏时间。
/// UI 不再自行累计时间，暂停、升级、结果冻结和 Boss 触发共享同一时间源。
/// </summary>
public sealed class GameTimerUI : MonoBehaviour
{
    [Header("UI 引用")]
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("显示样式")]
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color outlineColor = new Color(0.02f, 0.04f, 0.06f, 1f);

    [Range(0f, 1f)]
    [SerializeField] private float outlineWidth = 0.24f;

    private RunDirector _runDirector;

    /// <summary>当前已累计的游戏时间，供 HUD 测试和其他只读表现层读取。</summary>
    public float CurrentTimeSeconds => _runDirector != null ? _runDirector.ElapsedSeconds : 0f;

    /// <summary>初始化计时器和文字样式。</summary>
    private void Awake()
    {
        _runDirector = FindObjectOfType<RunDirector>();
        ApplyTextStyle();
        RefreshText();
    }

    /// <summary>读取权威计时器并刷新文字；本组件不拥有任何可推进的时间状态。</summary>
    private void Update()
    {
        if (_runDirector == null)
        {
            _runDirector = FindObjectOfType<RunDirector>();
        }

        RefreshText();
    }

    /// <summary>将累计秒数格式化为玩家易读的分钟:秒数。</summary>
    private void RefreshText()
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.FloorToInt(CurrentTimeSeconds);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    /// <summary>为计时文字应用粗体、禁用换行和深色描边。</summary>
    private void ApplyTextStyle()
    {
        if (timerText == null)
        {
            return;
        }

        timerText.color = textColor;
        timerText.fontStyle = FontStyles.Bold;
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.enableWordWrapping = false;

        Material textMaterial = timerText.fontMaterial;
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
