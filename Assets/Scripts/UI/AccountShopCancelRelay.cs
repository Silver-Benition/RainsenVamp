using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>把当前选中按钮收到的 Cancel 事件交给商店状态机，保持键盘与手柄一致。</summary>
public sealed class AccountShopCancelRelay : MonoBehaviour, ICancelHandler
{
    [SerializeField] private AccountShopUI owner;

    /// <summary>配置所属页面；条目从模板创建时由页面绑定，固定控件由编辑器序列化。</summary>
    public void Bind(AccountShopUI value) { owner = value; }

    /// <summary>仅转发一次取消，页面按帧去重以兼容 Escape 后备检测。</summary>
    public void OnCancel(BaseEventData eventData)
    {
        if (owner != null) owner.CancelOperation();
        eventData?.Use();
    }
}
