using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>横向商店卡片：图标、动态等级格和价格；选择只预览，Button 的确认事件才锁定。</summary>
public sealed class AccountShopEntryUI : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image icon;
    [SerializeField] private Outline highlight;
    [SerializeField] private RectTransform levelRoot;
    [SerializeField] private Image levelTemplate;
    [SerializeField] private Color purchasedColor = new Color32(36, 219, 242, 255);
    [SerializeField] private Color emptyColor = new Color32(89, 101, 113, 255);
    private readonly List<Image> _levels = new List<Image>();
    private Action<bool> _preview;
    private Action _confirm;
    private bool _hovered;
    private bool _focused;
    private bool _locked;
    public Button Control => button;
    public int VisibleLevelCount { get; private set; }
    public bool IsLocked => _locked;

#if UNITY_EDITOR
    /// <summary>仅供作者工具设置固定模板的全部引用。</summary>
    public void Author(Button control, TMP_Text price, Image picture, Outline border, RectTransform levels, Image pip)
    {
        button = control; label = price; icon = picture; highlight = border; levelRoot = levels; levelTemplate = pip;
    }
#endif
    /// <summary>绑定预览与确认两条独立路径；不会在导航 OnSelect 中调用确认。</summary>
    public void Bind(Action<bool> preview, Action confirm, AccountShopUI owner)
    {
        _preview = preview; _confirm = confirm;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Confirm);
        GetComponent<AccountShopCancelRelay>().Bind(owner);
        _hovered = _focused = _locked = false;
        UpdateHighlight();
    }

    /// <summary>按真实上限复用等级格；已购历史超过新上限时只亮现有格，退款仍由账号服务处理。</summary>
    public void Refresh(Sprite picture, int purchased, int max, string price, bool locked)
    {
        icon.sprite = picture;
        icon.enabled = picture != null;
        label.text = price;
        _locked = locked;
        VisibleLevelCount = Mathf.Max(0, max);
        while (_levels.Count < VisibleLevelCount)
        {
            Image pip = Instantiate(levelTemplate, levelRoot);
            pip.gameObject.SetActive(true);
            _levels.Add(pip);
        }
        for (int i = 0; i < _levels.Count; i++)
        {
            _levels[i].gameObject.SetActive(i < VisibleLevelCount);
            _levels[i].color = i < purchased ? purchasedColor : emptyColor;
        }
        // 五格一行由模板配置，行数随 maxLevel 增长；格高适应信息框而非写死等级数。
        GridLayoutGroup grid = levelRoot.GetComponent<GridLayoutGroup>();
        int columns = Mathf.Max(1, grid.constraintCount);
        int rows = Mathf.Max(1, Mathf.CeilToInt(VisibleLevelCount / (float)columns));
        grid.cellSize = new Vector2(Mathf.Max(4, (levelRoot.rect.width - (columns - 1) * grid.spacing.x) / columns),
            Mathf.Min(14, Mathf.Max(2, (levelRoot.rect.height - (rows - 1) * grid.spacing.y) / rows)));
        UpdateHighlight();
    }

    /// <summary>鼠标进入只请求详情预览。</summary>
    public void OnPointerEnter(PointerEventData eventData) { _hovered = true; UpdateHighlight(); _preview?.Invoke(false); }
    /// <summary>鼠标离开恢复普通或锁定边框。</summary>
    public void OnPointerExit(PointerEventData eventData) { _hovered = false; UpdateHighlight(); }
    /// <summary>方向导航及鼠标按下造成的选择都只预览，不提前锁定。</summary>
    public void OnSelect(BaseEventData eventData) { _focused = true; UpdateHighlight(); _preview?.Invoke(true); }
    /// <summary>导航离开后仍保留被锁定卡片的橙色边框。</summary>
    public void OnDeselect(BaseEventData eventData) { _focused = false; UpdateHighlight(); }
    /// <summary>点击或 Submit 仅确认卡片；购买另由详情按钮发起。</summary>
    private void Confirm() { _confirm?.Invoke(); }
    /// <summary>整张组合卡片的高亮统一包围图标与信息框，锁定优先于悬停。</summary>
    private void UpdateHighlight()
    {
        highlight.enabled = _locked || _hovered || _focused;
        highlight.effectColor = _locked ? new Color32(255, 191, 72, 255) : new Color32(38, 224, 246, 255);
    }
}
