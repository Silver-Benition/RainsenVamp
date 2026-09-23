using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>经验条下方的数字生命条；使用生命事件刷新，与经验栏共用显隐和布局。</summary>
public sealed class PlayerHealthHudUI : MonoBehaviour
{
    private PlayerHealth _health;
    private RectTransform _frame, _root, _fill, _counters;
    private TMP_Text _text;
    private Vector2 _originalCountersPosition;
    /// <summary>供布局验收读取生成的血条区域。</summary>
    public RectTransform BarRoot => _root;
    /// <summary>当前显示的精确生命文本。</summary>
    public string CurrentText => _text != null ? _text.text : "";

    /// <summary>只初始化一次；血条作为经验外框的子节点，宽度自然保持外框的六分之一。</summary>
    public void Initialize(RectTransform frame, PlayerHealth health, TMP_FontAsset font)
    {
        if (_root != null || frame == null || health == null) return;
        _frame = frame; _health = health;
        _root = new GameObject("PlayerHealthHUD", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _root.SetParent(frame, false); _root.anchorMin = Vector2.zero; _root.anchorMax = new Vector2(1f / 6, 0);
        _root.pivot = new Vector2(0, 1); _root.anchoredPosition = new Vector2(0, -8); _root.sizeDelta = new Vector2(0, 30);
        Image background = _root.GetComponent<Image>(); background.color = new Color32(55, 17, 20, 255); background.raycastTarget = false;
        // 内边距交给固定轨道；填充自身不再减去边距，极低生命比例也不会出现负宽度。
        RectTransform track = new GameObject("Track", typeof(RectTransform)).GetComponent<RectTransform>();
        track.SetParent(_root, false); track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(2, 2); track.offsetMax = new Vector2(-2, -2);
        _fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _fill.SetParent(track, false); _fill.anchorMin = Vector2.zero; _fill.anchorMax = Vector2.one;
        _fill.offsetMin = _fill.offsetMax = Vector2.zero;
        Image fill = _fill.GetComponent<Image>(); fill.color = new Color32(188, 47, 56, 255); fill.raycastTarget = false;
        _text = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        _text.transform.SetParent(_root, false); _text.rectTransform.anchorMin = Vector2.zero; _text.rectTransform.anchorMax = Vector2.one;
        _text.rectTransform.offsetMin = _text.rectTransform.offsetMax = Vector2.zero;
        _text.font = font; _text.fontSize = 21; _text.enableAutoSizing = true; _text.fontSizeMin = 12; _text.fontSizeMax = 21;
        _text.alignment = TextAlignmentOptions.Center; _text.color = Color.white; _text.raycastTarget = false;
        _counters = transform.parent != null ? transform.parent.Find("RunStatsDisplay") as RectTransform : null;
        if (_counters != null) _originalCountersPosition = _counters.anchoredPosition;
        _health.HealthChanged += Refresh; Refresh(_health.CurrentHealth, _health.MaxHealth); LayoutCounters();
    }
    /// <summary>重新启用时恢复订阅，避免禁用期间的生命变化造成旧显示。</summary>
    private void OnEnable()
    { if (_health != null) { _health.HealthChanged -= Refresh; _health.HealthChanged += Refresh; Refresh(_health.CurrentHealth, _health.MaxHealth); } }
    /// <summary>解除订阅，避免场景关闭后收到生命通知。</summary>
    private void OnDisable() { if (_health != null) _health.HealthChanged -= Refresh; }
    /// <summary>销毁动态血条并恢复被本组件调整的计数位置。</summary>
    private void OnDestroy()
    { if (_root != null) Destroy(_root.gameObject); if (_counters != null) _counters.anchoredPosition = _originalCountersPosition; }
    /// <summary>窗口宽度变化时重新排列旁侧击杀与金币计数。</summary>
    private void OnRectTransformDimensionsChange() { LayoutCounters(); }
    /// <summary>把原有统计栏放到血条右侧，维持其纵向位置，不挤占材料区域。</summary>
    private void LayoutCounters()
    { if (_frame != null && _counters != null) _counters.anchoredPosition = _originalCountersPosition + new Vector2(_frame.rect.width / 6 + 20, 0); }
    /// <summary>文本显示当前值而不是动画插值值，零血时关闭填充避免出现负宽度。</summary>
    private void Refresh(float current, float maximum)
    {
        if (_root == null) return;
        _fill.gameObject.SetActive(current > 0);
        _fill.anchorMax = new Vector2(maximum > 0 ? Mathf.Clamp01(current / maximum) : 0, 1);
        _text.text = current.ToString("0.##") + " / " + maximum.ToString("0.##");
    }
}
