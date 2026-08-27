using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 在手动暂停面板右侧创建角色属性看板，并展示 PlayerStats 的最终缓存值。
/// 组件应挂在 PausePanel 上，因此只会随手动暂停界面显示。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerStatBoardUI : MonoBehaviour
{
    [Header("布局")]
    [SerializeField] private Vector2 boardSize = new Vector2(410f, 720f);
    [SerializeField] private Vector2 anchoredOffset = new Vector2(-32f, 32f);
    [SerializeField, Min(12f)] private float titleFontSize = 28f;
    [SerializeField, Min(10f)] private float rowFontSize = 20f;
    [SerializeField, Min(0f)] private float rowSpacing = 2f;

    [Header("颜色")]
    [SerializeField] private Color borderColor = new Color(0.66f, 0.61f, 0.40f, 0.95f);
    [SerializeField] private Color backgroundColor = new Color(0.055f, 0.07f, 0.10f, 0.96f);
    [SerializeField] private Color titleColor = new Color(1f, 0.88f, 0.45f, 1f);
    [SerializeField] private Color labelColor = new Color(0.86f, 0.88f, 0.91f, 1f);
    [SerializeField] private Color valueColor = Color.white;

    private PlayerStats _playerStats;
    private RectTransform _boardRoot;
    private TextMeshProUGUI _labelsText;
    private TextMeshProUGUI _valuesText;
    private bool _statsSubscribed;

    /// <summary>运行时生成的右侧长方形看板根节点。</summary>
    public RectTransform BoardRoot => _boardRoot;

    /// <summary>当前看板显示的全部最终值文本，供可视化回归测试读取。</summary>
    public string CurrentValuesText => _valuesText != null ? _valuesText.text : string.Empty;

    /// <summary>当前建立的属性行数。</summary>
    public int DisplayedStatCount => PlayerStatPresentation.StatCount;

    /// <summary>预建看板并取得玩家属性；首次打开暂停菜单时不会产生逐帧创建。</summary>
    private void Awake()
    {
        BuildBoardIfNeeded();
        ResolvePlayerStats();
        RefreshValues();
    }

    /// <summary>面板显示时订阅低频属性变化，并刷新暂停前可能发生的所有修改。</summary>
    private void OnEnable()
    {
        ResolvePlayerStats();
        SubscribePlayerStats();
        RefreshValues();
    }

    /// <summary>暂停面板隐藏时解除监听；再次打开会重新同步最终快照。</summary>
    private void OnDisable()
    {
        UnsubscribePlayerStats();
    }

    /// <summary>编辑布局参数时阻止非法尺寸进入场景序列化。</summary>
    private void OnValidate()
    {
        boardSize.x = Mathf.Max(240f, boardSize.x);
        boardSize.y = Mathf.Max(560f, boardSize.y);
        titleFontSize = Mathf.Max(12f, titleFontSize);
        rowFontSize = Mathf.Max(10f, rowFontSize);
        rowSpacing = Mathf.Max(0f, rowSpacing);
    }

    /// <summary>只创建一次背景、标题以及左右两列文本。</summary>
    private void BuildBoardIfNeeded()
    {
        if (_boardRoot != null && _labelsText != null && _valuesText != null)
        {
            return;
        }

        Transform existingBoard = transform.Find("PlayerStatBoard");
        if (existingBoard != null)
        {
            _boardRoot = existingBoard as RectTransform;
            _labelsText = existingBoard.Find("Labels")?.GetComponent<TextMeshProUGUI>();
            _valuesText = existingBoard.Find("Values")?.GetComponent<TextMeshProUGUI>();
            if (_boardRoot != null && _labelsText != null && _valuesText != null)
            {
                ConfigureBoardLayout();
                return;
            }
        }

        TMP_Text existingText = GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset sharedFont = existingText != null ? existingText.font : null;

        GameObject boardObject = new GameObject(
            "PlayerStatBoard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline));
        boardObject.layer = gameObject.layer;
        boardObject.transform.SetParent(transform, false);
        _boardRoot = boardObject.GetComponent<RectTransform>();

        Image background = boardObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        Outline outline = boardObject.GetComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        TextMeshProUGUI title = CreateText(
            boardObject.transform,
            "Title",
            sharedFont,
            titleFontSize,
            titleColor,
            TextAlignmentOptions.Center);
        title.text = "角色属性";
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -14f);
        titleRect.sizeDelta = new Vector2(-32f, 48f);

        GameObject dividerObject = new GameObject(
            "Divider",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        dividerObject.layer = gameObject.layer;
        dividerObject.transform.SetParent(boardObject.transform, false);
        RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = Vector2.one;
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -66f);
        dividerRect.sizeDelta = new Vector2(-32f, 2f);
        Image divider = dividerObject.GetComponent<Image>();
        divider.color = borderColor;
        divider.raycastTarget = false;

        _labelsText = CreateText(
            boardObject.transform,
            "Labels",
            sharedFont,
            rowFontSize,
            labelColor,
            TextAlignmentOptions.TopLeft);
        ConfigureBodyColumn(_labelsText.rectTransform, 20f, -172f);

        _valuesText = CreateText(
            boardObject.transform,
            "Values",
            sharedFont,
            rowFontSize,
            valueColor,
            TextAlignmentOptions.TopRight);
        ConfigureBodyColumn(_valuesText.rectTransform, 224f, -20f);

        StringBuilder labelsBuilder = new StringBuilder(256);
        for (int index = 0; index < PlayerStatPresentation.StatCount; index++)
        {
            if (index > 0)
            {
                labelsBuilder.Append('\n');
            }

            labelsBuilder.Append(PlayerStatPresentation.GetDisplayName(
                PlayerStatPresentation.GetStatAt(index)));
        }

        _labelsText.text = labelsBuilder.ToString();
        ConfigureBoardLayout();
    }

    private void ConfigureBoardLayout()
    {
        _boardRoot.anchorMin = new Vector2(1f, 0f);
        _boardRoot.anchorMax = new Vector2(1f, 0f);
        _boardRoot.pivot = new Vector2(1f, 0f);
        _boardRoot.anchoredPosition = anchoredOffset;
        _boardRoot.sizeDelta = boardSize;
        _boardRoot.SetAsLastSibling();
    }

    private void ConfigureBodyColumn(RectTransform column, float leftInset, float rightInset)
    {
        column.anchorMin = Vector2.zero;
        column.anchorMax = Vector2.one;
        column.pivot = new Vector2(0.5f, 0.5f);
        column.offsetMin = new Vector2(leftInset, 22f);
        column.offsetMax = new Vector2(rightInset, -86f);
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string objectName,
        TMP_FontAsset font,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.lineSpacing = rowSpacing;
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }

    /// <summary>优先从 Player 标签取得正式玩家，并提供组件搜索后备。</summary>
    private void ResolvePlayerStats()
    {
        if (_playerStats != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerStats = player.GetComponent<PlayerStats>();
        }

        if (_playerStats == null)
        {
            _playerStats = FindObjectOfType<PlayerStats>();
        }
    }

    private void SubscribePlayerStats()
    {
        if (_statsSubscribed || _playerStats == null)
        {
            return;
        }

        _playerStats.StatsChanged += RefreshValues;
        _statsSubscribed = true;
    }

    private void UnsubscribePlayerStats()
    {
        if (!_statsSubscribed || _playerStats == null)
        {
            _statsSubscribed = false;
            return;
        }

        _playerStats.StatsChanged -= RefreshValues;
        _statsSubscribed = false;
    }

    /// <summary>从最终属性缓存重建右侧数值列；仅在显示或属性变化时调用。</summary>
    private void RefreshValues()
    {
        if (_valuesText == null)
        {
            return;
        }

        if (_playerStats == null)
        {
            _valuesText.text = "未找到玩家属性";
            return;
        }

        StringBuilder valuesBuilder = new StringBuilder(256);
        for (int index = 0; index < PlayerStatPresentation.StatCount; index++)
        {
            if (index > 0)
            {
                valuesBuilder.Append('\n');
            }

            PlayerStatType statType = PlayerStatPresentation.GetStatAt(index);
            valuesBuilder.Append(PlayerStatPresentation.FormatFinalValue(
                statType,
                _playerStats.GetFinalStat(statType)));
        }

        _valuesText.text = valuesBuilder.ToString();
    }
}
