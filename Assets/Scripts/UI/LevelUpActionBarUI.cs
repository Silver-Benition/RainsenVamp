using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 升级面板底部的局内操作栏表现。
/// 运行时构建是为了兼容现有场景而不复制候选卡 Prefab；逻辑与次数仍由 LevelUpManager/RunState 决定。
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelUpActionBarUI : MonoBehaviour
{
    private Button _rerollButton;
    private Button _skipButton;
    private Button _banishButton;
    private TextMeshProUGUI _rerollText;
    private TextMeshProUGUI _skipText;
    private TextMeshProUGUI _banishText;
    private Action _rerollAction;
    private Action _skipAction;
    private Action _banishAction;

    /// <summary>在升级面板底部建立操作栏并返回其控制组件。</summary>
    public static LevelUpActionBarUI Create(Transform panelTransform)
    {
        if (panelTransform == null)
        {
            return null;
        }

        var barObject = new GameObject(
            "LevelUpActionBar",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(HorizontalLayoutGroup),
            typeof(LevelUpActionBarUI));
        barObject.layer = panelTransform.gameObject.layer;
        RectTransform barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(panelTransform, false);
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, 28f);
        barRect.sizeDelta = new Vector2(900f, 76f);

        Image background = barObject.GetComponent<Image>();
        background.color = new Color(0.02f, 0.04f, 0.08f, 0.92f);
        background.raycastTarget = true;

        HorizontalLayoutGroup layout = barObject.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        LevelUpActionBarUI actionBar = barObject.GetComponent<LevelUpActionBarUI>();
        actionBar._rerollButton = actionBar.CreateActionButton("RerollButton", out actionBar._rerollText);
        actionBar._skipButton = actionBar.CreateActionButton("SkipButton", out actionBar._skipText);
        actionBar._banishButton = actionBar.CreateActionButton("BanishButton", out actionBar._banishText);
        return actionBar;
    }

    /// <summary>绑定三个按钮的逻辑行为；重复绑定会先移除旧监听。</summary>
    public void Bind(Action rerollAction, Action skipAction, Action banishAction)
    {
        Unbind();
        _rerollAction = rerollAction;
        _skipAction = skipAction;
        _banishAction = banishAction;

        if (_rerollButton != null) _rerollButton.onClick.AddListener(InvokeReroll);
        if (_skipButton != null) _skipButton.onClick.AddListener(InvokeSkip);
        if (_banishButton != null) _banishButton.onClick.AddListener(InvokeBanish);
    }

    /// <summary>刷新次数文本、按钮可交互状态和放逐模式提示。</summary>
    public void Refresh(int rerolls, int skips, int banishes, bool banishMode)
    {
        int safeRerolls = Mathf.Max(0, rerolls);
        int safeSkips = Mathf.Max(0, skips);
        int safeBanishes = Mathf.Max(0, banishes);

        if (_rerollText != null) _rerollText.text = $"重掷  ×{safeRerolls}";
        if (_skipText != null) _skipText.text = $"跳过  ×{safeSkips}";
        if (_banishText != null)
        {
            _banishText.text = banishMode ? "选择要放逐的项目" : $"放逐  ×{safeBanishes}";
            _banishText.color = banishMode ? new Color(1f, 0.82f, 0.25f) : Color.white;
        }

        if (_rerollButton != null) _rerollButton.interactable = safeRerolls > 0;
        if (_skipButton != null) _skipButton.interactable = safeSkips > 0;
        if (_banishButton != null) _banishButton.interactable = safeBanishes > 0;
    }

    /// <summary>销毁时移除本组件建立的按钮监听。</summary>
    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>创建一个带 TMP 文本的等宽操作按钮。</summary>
    private Button CreateActionButton(string objectName, out TextMeshProUGUI label)
    {
        var buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        buttonObject.layer = gameObject.layer;
        buttonObject.transform.SetParent(transform, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.28f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.2f, 0.32f, 0.48f, 1f);
        colors.pressedColor = new Color(0.08f, 0.12f, 0.2f, 1f);
        colors.disabledColor = new Color(0.08f, 0.09f, 0.11f, 0.75f);
        button.colors = colors;

        var textObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(buttonObject.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        label = textObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return button;
    }

    /// <summary>移除三个按钮的运行时监听并清空委托。</summary>
    private void Unbind()
    {
        if (_rerollButton != null) _rerollButton.onClick.RemoveListener(InvokeReroll);
        if (_skipButton != null) _skipButton.onClick.RemoveListener(InvokeSkip);
        if (_banishButton != null) _banishButton.onClick.RemoveListener(InvokeBanish);
        _rerollAction = null;
        _skipAction = null;
        _banishAction = null;
    }

    /// <summary>转发重掷按钮点击。</summary>
    private void InvokeReroll()
    {
        _rerollAction?.Invoke();
    }

    /// <summary>转发跳过按钮点击。</summary>
    private void InvokeSkip()
    {
        _skipAction?.Invoke();
    }

    /// <summary>转发放逐按钮点击。</summary>
    private void InvokeBanish()
    {
        _banishAction?.Invoke();
    }
}
