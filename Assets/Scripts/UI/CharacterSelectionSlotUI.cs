using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 角色选择页中的单个槽位。槽位只转发指针与导航事件，角色展示状态由页面统一维护。
/// </summary>
public sealed class CharacterSelectionSlotUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    private static readonly Color NormalFrameColor = new Color32(82, 126, 166, 255);
    private static readonly Color SelectedFrameColor = new Color32(255, 157, 47, 255);
    private static readonly Color EmptyFrameColor = new Color32(56, 76, 102, 220);

    private CharacterSelectionUI _owner;
    private CharacterDataSO _character;
    private Image _background;
    private Image _portrait;
    private TMP_Text _label;
    private Button _button;
    private Outline _outline;

    /// <summary>槽位在固定 4×3 网格中的从零开始索引。</summary>
    public int SlotIndex { get; private set; }

    /// <summary>槽位是否持有可以被确认的角色。</summary>
    public bool IsAvailable => _character != null;

    /// <summary>槽位当前绑定的角色；空位返回 null。</summary>
    public CharacterDataSO Character => _character;

    /// <summary>由角色选择页创建槽位后一次性注入其 UI 引用和角色数据。</summary>
    public void Bind(
        CharacterSelectionUI owner,
        int slotIndex,
        CharacterDataSO character,
        Image background,
        Image portrait,
        TMP_Text label,
        Button button,
        Outline outline)
    {
        _owner = owner;
        SlotIndex = slotIndex;
        _character = character;
        _background = background;
        _portrait = portrait;
        _label = label;
        _button = button;
        _outline = outline;

        bool isAvailable = character != null;
        _button.interactable = isAvailable;
        _button.onClick.RemoveAllListeners();
        if (isAvailable)
        {
            _button.onClick.AddListener(Submit);
        }

        _portrait.sprite = isAvailable ? character.GetSelectionIcon() : null;
        _portrait.enabled = _portrait.sprite != null;
        _label.text = isAvailable ? character.GetDisplayName() : "空位";
        _background.color = isAvailable
            ? new Color32(18, 43, 72, 245)
            : new Color32(10, 25, 45, 210);
        _label.color = isAvailable
            ? Color.white
            : new Color32(102, 125, 150, 255);
        SetSelected(false);
    }

    /// <summary>切换橙色选中描边；空槽位始终保持弱化边框。</summary>
    public void SetSelected(bool selected)
    {
        if (_outline == null)
        {
            return;
        }

        _outline.effectColor = !IsAvailable
            ? EmptyFrameColor
            : selected ? SelectedFrameColor : NormalFrameColor;
        _outline.effectDistance = selected
            ? new Vector2(4f, -4f)
            : new Vector2(2f, -2f);
    }

    /// <summary>鼠标进入有效槽位时立即切换角色展示。</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsAvailable)
        {
            _owner.HandleSlotHover(this);
        }
    }

    /// <summary>键盘或手柄导航到槽位时同步更新角色展示。</summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (IsAvailable)
        {
            _owner.HandleSlotHover(this);
        }
    }

    private void Submit()
    {
        if (IsAvailable)
        {
            _owner.HandleSlotClick(this);
        }
    }
}
