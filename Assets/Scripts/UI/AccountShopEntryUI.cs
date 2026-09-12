using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>商店复用条目模板，鼠标点击与导航选中均更新详情，并请求滚动到可见区域。</summary>
public sealed class AccountShopEntryUI : MonoBehaviour, ISelectHandler
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    private Action _selected;


#if UNITY_EDITOR
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button Button { get => button; set => button = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text Label { get => label; set => label = value; }
#endif
    /// <summary>绑定一次低频列表内容；模板上的固定 UI 由编辑器序列化。</summary>
    public void Bind(string text, Action selected)
    {
        label.text = text;
        _selected = selected;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Select);
    }

    /// <summary>导航进入条目时与点击使用同一详情路径。</summary>
    public void OnSelect(BaseEventData eventData)
    {
        Select();
    }

    /// <summary>向页面请求选中和滚动，不在条目中修改账号状态。</summary>
    private void Select()
    {
        _selected?.Invoke();
    }
}
