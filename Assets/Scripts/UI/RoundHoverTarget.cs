using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>把鼠标悬停和导航焦点映射到同一详情入口，不负责道具或交易状态。</summary>
public sealed class RoundHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private Action _enter, _exit;
    /// <summary>图标复用时更新当前实例的回调。</summary>
    public void Bind(Action enter, Action exit) { _enter = enter; _exit = exit; }
    /// <summary>鼠标进入图标或详情窗时显示信息。</summary>
    public void OnPointerEnter(PointerEventData eventData) { _enter?.Invoke(); }
    /// <summary>鼠标离开后交给视图延时关闭，允许进入详情操作区。</summary>
    public void OnPointerExit(PointerEventData eventData) { _exit?.Invoke(); }
    /// <summary>键盘或手柄聚焦提供与悬停相同的信息。</summary>
    public void OnSelect(BaseEventData eventData) { _enter?.Invoke(); }
    /// <summary>导航离开后释放对应的详情。</summary>
    public void OnDeselect(BaseEventData eventData) { _exit?.Invoke(); }
}
