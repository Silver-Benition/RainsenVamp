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
    private RectTransform _modernRows;
    private readonly TMP_Text[] _names = new TMP_Text[15], _values = new TMP_Text[15];
    private readonly RectTransform[] _rows = new RectTransform[15];
    private Button _primaryTab, _secondaryTab;
    private StatTooltipView _statTooltip;
    private bool _secondary;
    private string _currentModernValues = "";
    private static readonly PlayerStatType[] OtherStats = { PlayerStatType.ExperienceGain, PlayerStatType.PickupRange,
        PlayerStatType.Revival, PlayerStatType.Reroll, PlayerStatType.Skip, PlayerStatType.Banish };

    /// <summary>运行时生成的右侧长方形看板根节点。</summary>
    public RectTransform BoardRoot => _boardRoot;

    /// <summary>当前看板显示的全部最终值文本，供可视化回归测试读取。</summary>
    public string CurrentValuesText => _modernRows != null ? _currentModernValues : _valuesText != null ? _valuesText.text : string.Empty;

    /// <summary>当前建立的属性行数。</summary>
    public int DisplayedStatCount => _modernRows != null ? (_secondary ? OtherStats.Length : 15) : PlayerStatPresentation.StatCount;

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
        _statTooltip?.Hide();
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

    /// <summary>暂停属性位于右侧上方，底部为武器预留独立区域；与武器栏使用同一列边界。</summary>
    private void ConfigureBoardLayout()
    {
        _boardRoot.anchorMin = new Vector2(.735f, .19f);
        _boardRoot.anchorMax = new Vector2(.965f, .865f);
        _boardRoot.pivot = new Vector2(1, 1);
        _boardRoot.offsetMin = _boardRoot.offsetMax = Vector2.zero;
        _boardRoot.SetAsLastSibling();
        RectTransform title = (RectTransform)_boardRoot.Find("Title");
        if (title != null)
        {
            title.anchorMin = new Vector2(.05f, .89f); title.anchorMax = new Vector2(.95f, .98f);
            title.offsetMin = title.offsetMax = Vector2.zero;
            TMP_Text text = title.GetComponent<TMP_Text>();
            text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = titleFontSize;
        }
        RectTransform divider = (RectTransform)_boardRoot.Find("Divider");
        if (divider != null)
        {
            divider.anchorMin = new Vector2(.05f, .875f); divider.anchorMax = new Vector2(.95f, .875f);
            divider.anchoredPosition = Vector2.zero; divider.sizeDelta = new Vector2(0, 2);
        }
        if (_labelsText != null && _valuesText != null)
        {
            // 两列采用同一字号，避免独立自动缩放后同一属性的名称和值错行。
            float height = Mathf.Max(1, _boardRoot.rect.height * .81f);
            float size = Mathf.Min(rowFontSize, Mathf.Max(8, (height / (_playerStats != null && _playerStats.UsesBrotatoStats ? 24 : PlayerStatPresentation.StatCount) - rowSpacing) / 1.35f));
            _labelsText.fontSize = _valuesText.fontSize = size;
        }
    }

    /// <summary>分辨率变化后按当前 Canvas 尺寸重新计算相同的属性列字号。</summary>
    private void OnRectTransformDimensionsChange()
    { if (_boardRoot != null) ConfigureBoardLayout(); }

    /// <summary>属性名称和值分别占据左右两列，使用比例边界避免固定像素挤压。</summary>
    private void ConfigureBodyColumn(RectTransform column, float leftInset, float rightInset)
    {
        bool values = column.name == "Values";
        column.anchorMin = new Vector2(values ? .70f : .06f, .035f);
        column.anchorMax = new Vector2(values ? .94f : .70f, .845f);
        column.offsetMin = column.offsetMax = Vector2.zero;
    }

    /// <summary>构建不拦截输入的属性文本，字体使用暂停菜单已有资源。</summary>
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

    /// <summary>订阅属性变化以即时刷新当前页面。</summary>
    private void SubscribePlayerStats()
    {
        if (_statsSubscribed || _playerStats == null)
        {
            return;
        }

        _playerStats.StatsChanged += RefreshValues;
        _statsSubscribed = true;
    }

    /// <summary>解除属性事件，避免场景重载残留订阅。</summary>
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
        if (_playerStats.UsesBrotatoStats) RefreshModern();
    }

    /// <summary>切换暂停属性分页；不改变角色属性和特殊资源。</summary>
    public void SelectPage(bool secondary) { _secondary = secondary; _statTooltip?.Hide(); RefreshValues(); }

    /// <summary>按当前页创建/刷新独立属性行，零和负值均保留，特殊资源显示剩余数量。</summary>
    private void RefreshModern()
    {
        if (_modernRows == null)
        {
            _labelsText.gameObject.SetActive(false); _valuesText.gameObject.SetActive(false);
            _modernRows = MakeRect("Rows", _boardRoot, .06f, .035f, .94f, .745f);
            _primaryTab = MakeTab("Primary", "主要", .06f, .48f, false);
            _secondaryTab = MakeTab("Secondary", "次要 / 特殊", .52f, .94f, true);
            _statTooltip = new StatTooltipView((RectTransform)transform, _labelsText.font);
            for (int i = 0; i < 15; i++)
            {
                RectTransform row = MakeRect("Stat" + i, _modernRows, 0, 0, 1, 1); _rows[i] = row;
                row.gameObject.AddComponent<Image>().color = Color.clear;
                _names[i] = CreateText(row, "Name", _labelsText.font, rowFontSize, labelColor, TextAlignmentOptions.MidlineLeft);
                _values[i] = CreateText(row, "Value", _labelsText.font, rowFontSize, valueColor, TextAlignmentOptions.MidlineRight);
                SetRect(_names[i].rectTransform, 0, 0, .76f, 1); SetRect(_values[i].rectTransform, .76f, 0, 1, 1);
                _names[i].enableAutoSizing = _values[i].enableAutoSizing = true;
                _names[i].fontSizeMin = _values[i].fontSizeMin = 12;
                _names[i].fontSizeMax = _values[i].fontSizeMax = rowFontSize;
                row.gameObject.AddComponent<RoundHoverTarget>();
            }
        }
        PlayerStatType[] stats = _secondary ? OtherStats : BrotatoStatRules.Primary;
        _primaryTab.image.color = _secondary ? backgroundColor : new Color32(64, 74, 51, 255);
        _secondaryTab.image.color = _secondary ? new Color32(64, 74, 51, 255) : backgroundColor;
        var values = new StringBuilder();
        for (int i = 0; i < 15; i++)
        {
            RectTransform row = _rows[i]; row.gameObject.SetActive(i < stats.Length);
            if (i >= stats.Length) continue;
            PlayerStatType stat = stats[i]; float top = 1 - i / 15f;
            // 次要属性与流程资源之间保留一行空隙，分类不挤占核心栏。
            if (_secondary && i >= 2) top -= 1f / 15;
            SetRect(row, 0, top - .06f, 1, top);
            _names[i].text = PlayerStatPresentation.GetDisplayName(stat);
            float value = _playerStats.GetFinalStat(stat); RunState state = RunState.Instance;
            if (state != null)
            {
                if (stat == PlayerStatType.Revival) value = state.RemainingRevivals;
                if (stat == PlayerStatType.Reroll) value = state.RemainingRerolls;
                if (stat == PlayerStatType.Skip) value = state.RemainingSkips;
                if (stat == PlayerStatType.Banish) value = state.RemainingBanishes;
            }
            _values[i].text = PlayerStatPresentation.FormatFinalValue(stat, value);
            if (i > 0) values.Append('\n'); values.Append(_values[i].text);
            row.GetComponent<RoundHoverTarget>().Bind(() => _statTooltip.Show(row, stat), () => _statTooltip.HideFrom(row));
        }
        _currentModernValues = values.ToString();
    }
    /// <summary>创建分页按钮并保留统一文本本地化入口。</summary>
    private Button MakeTab(string name, string text, float x0, float x1, bool secondary)
    {
        RectTransform rect = MakeRect(name, _boardRoot, x0, .78f, x1, .86f);
        rect.gameObject.AddComponent<Image>(); Button button = rect.gameObject.AddComponent<Button>();
        TMP_Text label = CreateText(rect, "Label", _labelsText.font, 22, Color.white, TextAlignmentOptions.Center);
        label.text = RoundShopPresentation.Text("round.ui." + (secondary ? "secondary" : "primary"), text);
        label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 22;
        SetRect(label.rectTransform, .02f, .03f, .98f, .97f); button.onClick.AddListener(() => SelectPage(secondary)); return button;
    }
    /// <summary>创建相对布局矩形。</summary>
    private static RectTransform MakeRect(string name, Transform parent, float x0, float y0, float x1, float y1)
    { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); SetRect(rect, x0, y0, x1, y1); return rect; }
    /// <summary>设置归一化区域并清除默认像素偏移。</summary>
    private static void SetRect(RectTransform rect, float x0, float y0, float x1, float y1)
    { rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.offsetMin = rect.offsetMax = Vector2.zero; }
    /// <summary>销毁本组件创建的独立说明窗。</summary>
    private void OnDestroy() { _statTooltip?.Dispose(); }

    /// <summary>按核心、次要、特殊资源分组，保留角色原始负值；次数显示剩余数量。</summary>
    private void AppendGroup(StringBuilder labels, StringBuilder values, string title, PlayerStatType[] stats)
    {
        labels.Append(title).Append('\n'); values.Append('\n');
        foreach (PlayerStatType stat in stats)
        {
            labels.Append(PlayerStatPresentation.GetDisplayName(stat)).Append('\n');
            float value = _playerStats.GetFinalStat(stat);
            RunState state = RunState.Instance;
            if (state != null)
            {
                if (stat == PlayerStatType.Revival) value = state.RemainingRevivals;
                if (stat == PlayerStatType.Reroll) value = state.RemainingRerolls;
                if (stat == PlayerStatType.Skip) value = state.RemainingSkips;
                if (stat == PlayerStatType.Banish) value = state.RemainingBanishes;
            }
            values.Append(PlayerStatPresentation.FormatFinalValue(stat, value)).Append('\n');
        }
    }
}
