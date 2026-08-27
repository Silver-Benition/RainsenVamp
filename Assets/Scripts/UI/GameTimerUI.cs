using TMPro;
using UnityEngine;

/// <summary>
/// 显示本局已经运行的游戏时间。
/// 计时使用 Time.deltaTime，因此暂停或升级选择时会随游戏一起停止。
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

    private float _elapsedSeconds;

    /// <summary>当前已累计的游戏时间，供 HUD 测试和其他只读表现层读取。</summary>
    public float CurrentTimeSeconds => _elapsedSeconds;

    /// <summary>初始化计时器和文字样式。</summary>
    private void Awake()
    {
        _elapsedSeconds = 0f;
        ApplyTextStyle();
        RefreshText();
    }

    /// <summary>按游戏时间推进计时；Time.timeScale 为 0 时自然停止。</summary>
    private void Update()
    {
        _elapsedSeconds += Time.deltaTime;
        RefreshText();
    }

    /// <summary>将累计秒数格式化为玩家易读的分钟:秒数。</summary>
    private void RefreshText()
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.FloorToInt(_elapsedSeconds);
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
