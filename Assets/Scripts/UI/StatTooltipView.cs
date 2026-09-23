using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>两处属性面板共用的只读说明窗，单个面板只建一份并按悬停行复用。</summary>
public sealed class StatTooltipView
{
    private readonly RectTransform _parent, _root;
    private readonly TMP_Text _text;
    private RectTransform _owner;
    /// <summary>说明窗根节点，供视图生命周期与布局检查使用。</summary>
    public RectTransform Root => _root;
    /// <summary>在覆盖该页面的父区域中预建说明，不抢占鼠标射线。</summary>
    public StatTooltipView(RectTransform parent, TMP_FontAsset font)
    {
        _parent = parent;
        _root = new GameObject("StatTooltip", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _root.SetParent(parent, false); _root.GetComponent<Image>().color = new Color32(20, 24, 21, 255);
        _root.GetComponent<Image>().raycastTarget = false;
        _text = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        _text.transform.SetParent(_root, false); _text.rectTransform.anchorMin = Vector2.zero; _text.rectTransform.anchorMax = Vector2.one;
        _text.rectTransform.offsetMin = new Vector2(16, 12); _text.rectTransform.offsetMax = new Vector2(-16, -12);
        _text.font = font; _text.fontSize = 23; _text.color = Color.white; _text.raycastTarget = false;
        _text.enableAutoSizing = true; _text.fontSizeMin = 16; _text.fontSizeMax = 23;
        _text.alignment = TextAlignmentOptions.TopLeft; Hide();
    }
    /// <summary>显示属性说明并放在属性栏左侧；高度随文本变化，四边限制在页面内。</summary>
    public void Show(RectTransform owner, PlayerStatType stat)
    {
        if (owner == null || !owner.gameObject.activeInHierarchy) return;
        Canvas.ForceUpdateCanvases(); _owner = owner;
        _text.text = PlayerStatPresentation.GetDisplayName(stat) + "\n" + PlayerStatDescriptions.Get(stat);
        float width = Mathf.Min(470, _parent.rect.width * .36f);
        float height = Mathf.Min(_parent.rect.height * .8f, _text.GetPreferredValues(_text.text, width - 32, float.PositiveInfinity).y + 28);
        var corners = new Vector3[4]; owner.GetWorldCorners(corners);
        Vector3 top = _parent.InverseTransformPoint(corners[1]);
        _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.zero;
        _root.sizeDelta = new Vector2(width, height);
        _root.anchoredPosition = new Vector2(Mathf.Clamp(top.x - _parent.rect.xMin - width - 12, 8, _parent.rect.width - width - 8),
            Mathf.Clamp(top.y - _parent.rect.yMin - height, 8, _parent.rect.height - height - 8));
        _root.gameObject.SetActive(true); _root.SetAsLastSibling();
    }
    /// <summary>离开旧属性时不关闭后来已经打开的另一条说明。</summary>
    public void HideFrom(RectTransform owner) { if (_owner == owner) Hide(); }
    /// <summary>切页和关闭界面时立即清理悬停状态。</summary>
    public void Hide() { _owner = null; if (_root != null) _root.gameObject.SetActive(false); }
    /// <summary>组件单独移除时释放同级说明窗。</summary>
    public void Dispose() { if (_root != null) Object.Destroy(_root.gameObject); }
}
