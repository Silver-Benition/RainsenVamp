using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>长按确认按钮；真实时间计时，每次按下最多提交一次，普通点击和重复 Submit 不提交。</summary>
public sealed class HoldToConfirmButton : Button
{
    [SerializeField, Min(.1f)] private float holdSeconds = .8f;
    private RectTransform _fill;
    private bool _holding, _keyboard;
    private float _elapsed;
    public float Progress => Mathf.Clamp01(_elapsed / holdSeconds);

    /// <summary>预建不拦截射线的进度底色；按钮文字随后创建，始终位于填充层上方。</summary>
    protected override void Awake()
    {
        base.Awake();
        var go = new GameObject("HoldProgress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);
        _fill = (RectTransform)go.transform;
        _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0, 1);
        _fill.offsetMin = _fill.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.color = new Color(.65f, .86f, .4f, .38f); image.raycastTarget = false;
    }

    /// <summary>左键按下启动一次计时；右键和不可用状态不产生进度。</summary>
    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        if (eventData.button == PointerEventData.InputButton.Left) BeginHold(false);
    }

    /// <summary>松开立即清零，不执行 Button 默认点击提交。</summary>
    public override void OnPointerUp(PointerEventData eventData)
    { base.OnPointerUp(eventData); CancelHold(); }

    /// <summary>鼠标移出即取消；移回时必须重新按下。</summary>
    public override void OnPointerExit(PointerEventData eventData)
    { base.OnPointerExit(eventData); CancelHold(); }

    /// <summary>吞掉短按生成的点击；奖励只由完成长按的唯一入口提交。</summary>
    public override void OnPointerClick(PointerEventData eventData) { }

    /// <summary>键盘或手柄只接受 Submit 实际按下帧，避免输入模块的按住重复事件连续处理宝箱。</summary>
    public override void OnSubmit(BaseEventData eventData)
    { if (Input.GetButtonDown("Submit")) BeginHold(true); }

    /// <summary>焦点转到其他控件时取消计时。</summary>
    public override void OnDeselect(BaseEventData eventData)
    { base.OnDeselect(eventData); CancelHold(); }

    /// <summary>页面隐藏或组件停用时清除未完成的操作。</summary>
    protected override void OnDisable() { CancelHold(); base.OnDisable(); }

    /// <summary>窗口失焦时取消，防止返回游戏后完成先前的长按。</summary>
    private void OnApplicationFocus(bool focused) { if (!focused) CancelHold(); }

    /// <summary>开始一次独立操作；重复按下不能重置已经进行中的计时。</summary>
    private void BeginHold(bool keyboard)
    {
        if (_holding || !IsActive() || !IsInteractable()) return;
        _keyboard = keyboard; _holding = true; _elapsed = 0;
    }

    /// <summary>仅在长按期间推进；使用真实时间使暂停中的宝箱页面也可操作。</summary>
    private void Update()
    {
        if (!_holding) return;
        if (!IsActive() || !IsInteractable() || (_keyboard && !Input.GetButton("Submit")))
        { CancelHold(); return; }
        _elapsed += Time.unscaledDeltaTime;
        _fill.anchorMax = new Vector2(Progress, 1);
        if (Progress < 1) return;
        // 先关闭计时再通知业务；事件重入及下一箱复用同一按钮都不会自动续按。
        CancelHold();
        onClick.Invoke();
    }

    /// <summary>清除计时和填充，供换箱、页面切换和输入取消共用。</summary>
    public void CancelHold()
    {
        _holding = false; _elapsed = 0;
        if (_fill != null) _fill.anchorMax = new Vector2(0, 1);
    }
}
