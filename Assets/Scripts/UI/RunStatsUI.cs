using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 显示并维护本局击杀数与金币数。
/// 击杀由 EnemyBase 在确认死亡时登记；金币暂时不由敌人掉落，但保留 AddGold 接口供拾取系统接入。
/// </summary>
public sealed class RunStatsUI : MonoBehaviour
{
    /// <summary>当前场景中的本局统计显示实例。</summary>
    public static RunStatsUI Instance { get; private set; }

    [Header("UI 引用")]
    [SerializeField] private Image skullIcon;
    [SerializeField] private TextMeshProUGUI killCountText;
    [SerializeField] private TextMeshProUGUI goldCountText;
    [SerializeField] private Image coinIcon;

    [Header("文字样式")]
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color outlineColor = new Color(0.02f, 0.04f, 0.06f, 1f);

    [Range(0f, 1f)]
    [SerializeField] private float outlineWidth = 0.24f;

    private int killCount;
    private int goldCount;

    /// <summary>本局已击杀单位数量。</summary>
    public int KillCount => killCount;

    /// <summary>本局持有金币数量。</summary>
    public int GoldCount => goldCount;

    /// <summary>注册本局统计实例，并从零开始显示当前场景的数据。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        killCount = 0;
        goldCount = 0;
        ApplyTextStyle(killCountText);
        ApplyTextStyle(goldCountText);
        RefreshText();
    }

    /// <summary>场景卸载时清理静态实例，避免下一局引用已销毁对象。</summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>登记一个已确认死亡的敌人，并立即刷新击杀数字。</summary>
    public void RegisterKill()
    {
        if (killCount < int.MaxValue)
        {
            killCount++;
        }

        RefreshText();
    }

    /// <summary>增加金币；当前版本尚未有掉落和拾取逻辑，因此暂不主动调用。</summary>
    /// <param name="amount">要增加的正整数金币数量。</param>
    public void AddGold(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        goldCount = Mathf.Clamp(goldCount + amount, 0, int.MaxValue);
        RefreshText();
    }

    /// <summary>把本局统计重置为零，供重新开始或测试流程调用。</summary>
    public void ResetRunStats()
    {
        killCount = 0;
        goldCount = 0;
        RefreshText();
    }

    /// <summary>同步两个计数文本，骷髅和金币图标保持为静态 Sprite。</summary>
    private void RefreshText()
    {
        if (killCountText != null)
        {
            killCountText.text = killCount.ToString();
        }

        if (goldCountText != null)
        {
            goldCountText.text = goldCount.ToString();
        }
    }

    /// <summary>为计数文本应用粗体、禁用换行和深色描边。</summary>
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
