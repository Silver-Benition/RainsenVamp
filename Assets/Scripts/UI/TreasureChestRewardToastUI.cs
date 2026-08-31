using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 在主 HUD 顶部依次展示宝箱武器奖励。
/// 本组件只订阅结算结果并负责 UI 动画，不参与掉落、抽取或武器授予。
/// </summary>
[DisallowMultipleComponent]
public sealed class TreasureChestRewardToastUI : MonoBehaviour
{
    private enum ToastPhase
    {
        Hidden,
        FadeIn,
        Hold,
        FadeOut
    }

    [Header("本地化预留")]
    [SerializeField] private string rewardTitleKey = "ui.treasure.reward";
    [SerializeField] private string rewardTitleFallback = "宝箱奖励";

    [Header("布局与动画")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, -24f);
    [SerializeField] private Vector2 panelSize = new Vector2(520f, 76f);
    [SerializeField, Min(0.05f)] private float fadeInDuration = 0.2f;
    [SerializeField, Min(0.1f)] private float holdDuration = 1.8f;
    [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.35f;
    [SerializeField, Range(1, 16)] private int maxQueuedRewards = 8;

    [Header("颜色与字体")]
    [SerializeField] private Color borderColor = new Color(1f, 0.72f, 0.16f, 0.98f);
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.07f, 0.05f, 0.94f);
    [SerializeField] private Color textColor = new Color(1f, 0.93f, 0.68f, 1f);
    [SerializeField] private TMP_FontAsset font;

    private readonly Queue<ChestRewardResult> _pendingRewards =
        new Queue<ChestRewardResult>(8);

    private RectTransform _panelRoot;
    private CanvasGroup _canvasGroup;
    private Image _rewardIcon;
    private TextMeshProUGUI _rewardText;
    private LevelUpManager _levelUpManager;
    private ToastPhase _phase;
    private Vector2 _basePosition;
    private float _phaseElapsed;
    private string _displayedRewardName = string.Empty;
    private int _displayedRewardLevel;

    /// <summary>当前是否正在展示一项宝箱奖励。</summary>
    public bool IsShowingReward => _phase != ToastPhase.Hidden;

    /// <summary>当前横幅显示的权威武器名称。</summary>
    public string DisplayedRewardName => _displayedRewardName;

    /// <summary>当前横幅显示的奖励结算后等级。</summary>
    public int DisplayedRewardLevel => _displayedRewardLevel;

    /// <summary>等待展示的奖励数量。</summary>
    public int QueuedRewardCount => _pendingRewards.Count;

    /// <summary>缓存字体并一次性创建横幅层级，后续奖励只更新已有控件。</summary>
    private void Awake()
    {
        ResolveFont();
        BuildViewIfNeeded();
        HideImmediately();
    }

    /// <summary>启用时解析当前场景管理器并订阅宝箱奖励事件。</summary>
    private void OnEnable()
    {
        ResolveManagerAndSubscribe();
    }

    /// <summary>所有场景对象完成 Awake 后再次解析，规避脚本执行顺序差异。</summary>
    private void Start()
    {
        ResolveManagerAndSubscribe();
    }

    /// <summary>使用不受暂停影响的时间推进横幅淡入、停留和淡出状态机。</summary>
    private void Update()
    {
        if (_levelUpManager == null)
        {
            ResolveManagerAndSubscribe();
        }

        if (_phase == ToastPhase.Hidden)
        {
            return;
        }

        _phaseElapsed += Time.unscaledDeltaTime;
        switch (_phase)
        {
            case ToastPhase.FadeIn:
                UpdateFadeIn();
                break;
            case ToastPhase.Hold:
                if (_phaseElapsed >= holdDuration)
                {
                    BeginPhase(ToastPhase.FadeOut);
                }
                break;
            case ToastPhase.FadeOut:
                UpdateFadeOut();
                break;
        }
    }

    /// <summary>停用时解除事件并清空运行时队列，避免场景重载后重复回调。</summary>
    private void OnDisable()
    {
        UnsubscribeManager();
        _pendingRewards.Clear();
        if (_panelRoot != null)
        {
            HideImmediately();
        }
    }

    /// <summary>钳制 Inspector 中可能破坏布局或状态机的参数。</summary>
    private void OnValidate()
    {
        panelSize.x = Mathf.Max(1f, panelSize.x);
        panelSize.y = Mathf.Max(1f, panelSize.y);
        fadeInDuration = Mathf.Max(0.05f, fadeInDuration);
        holdDuration = Mathf.Max(0.1f, holdDuration);
        fadeOutDuration = Mathf.Max(0.05f, fadeOutDuration);
        maxQueuedRewards = Mathf.Clamp(maxQueuedRewards, 1, 16);
    }

    /// <summary>从同一 Canvas 中复用已配置字体；缺失时退回 TMP 默认字体。</summary>
    private void ResolveFont()
    {
        if (font != null)
        {
            return;
        }

        TMP_Text[] existingTexts = GetComponentsInChildren<TMP_Text>(true);
        for (int index = 0; index < existingTexts.Length; index++)
        {
            if (existingTexts[index] != null && existingTexts[index].font != null)
            {
                font = existingTexts[index].font;
                return;
            }
        }

        font = TMP_Settings.defaultFontAsset;
    }

    /// <summary>创建金色边框、深色背景、奖励图标和文字控件。</summary>
    private void BuildViewIfNeeded()
    {
        if (_panelRoot != null)
        {
            return;
        }

        GameObject panelObject = new GameObject(
            "TreasureChestRewardToast",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup));
        panelObject.layer = gameObject.layer;
        _panelRoot = panelObject.GetComponent<RectTransform>();
        _panelRoot.SetParent(transform, false);
        _panelRoot.anchorMin = new Vector2(0.5f, 1f);
        _panelRoot.anchorMax = new Vector2(0.5f, 1f);
        _panelRoot.pivot = new Vector2(0.5f, 1f);
        _panelRoot.anchoredPosition = anchoredPosition;
        _panelRoot.sizeDelta = panelSize;
        _panelRoot.SetAsLastSibling();

        Image border = panelObject.GetComponent<Image>();
        border.color = borderColor;
        border.raycastTarget = false;
        _canvasGroup = panelObject.GetComponent<CanvasGroup>();
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        Image background = CreateImage("Background", _panelRoot, backgroundColor);
        Stretch(background.rectTransform, 2f);

        Image iconFrame = CreateImage(
            "IconFrame",
            _panelRoot,
            new Color(0.22f, 0.16f, 0.06f, 0.98f));
        RectTransform frameRect = iconFrame.rectTransform;
        frameRect.anchorMin = new Vector2(0f, 0.5f);
        frameRect.anchorMax = new Vector2(0f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = new Vector2(42f, 0f);
        frameRect.sizeDelta = new Vector2(58f, 58f);

        _rewardIcon = CreateImage("RewardIcon", frameRect, Color.white);
        Stretch(_rewardIcon.rectTransform, 5f);
        _rewardIcon.preserveAspect = true;

        GameObject textObject = new GameObject(
            "RewardText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(_panelRoot, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(84f, 8f);
        textRect.offsetMax = new Vector2(-18f, -8f);
        _rewardText = textObject.GetComponent<TextMeshProUGUI>();
        _rewardText.font = font;
        _rewardText.fontSize = 30f;
        _rewardText.fontStyle = FontStyles.Bold;
        _rewardText.color = textColor;
        _rewardText.alignment = TextAlignmentOptions.MidlineLeft;
        _rewardText.enableWordWrapping = false;
        _rewardText.raycastTarget = false;

        _basePosition = anchoredPosition;
    }

    /// <summary>创建一个不接收射线的 UI Image 并挂到指定父节点。</summary>
    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.layer = parent.gameObject.layer;
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>把子节点拉伸到父节点，并保留统一像素内边距。</summary>
    private static void Stretch(RectTransform rectTransform, float inset)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(inset, inset);
        rectTransform.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>解析场景单例并保证同一管理器只订阅一次。</summary>
    private void ResolveManagerAndSubscribe()
    {
        LevelUpManager resolvedManager = LevelUpManager.Instance;
        if (_levelUpManager == resolvedManager)
        {
            return;
        }

        UnsubscribeManager();
        _levelUpManager = resolvedManager;
        if (_levelUpManager != null)
        {
            _levelUpManager.ChestRewardGranted += HandleChestRewardGranted;
        }
    }

    /// <summary>解除当前奖励事件订阅并清空缓存引用。</summary>
    private void UnsubscribeManager()
    {
        if (_levelUpManager != null)
        {
            _levelUpManager.ChestRewardGranted -= HandleChestRewardGranted;
        }

        _levelUpManager = null;
    }

    /// <summary>把新奖励加入有界队列；队列满时淘汰最旧的等待项，优先显示最新反馈。</summary>
    private void HandleChestRewardGranted(ChestRewardResult result)
    {
        while (_pendingRewards.Count >= maxQueuedRewards)
        {
            _pendingRewards.Dequeue();
        }

        _pendingRewards.Enqueue(result);
        if (_phase == ToastPhase.Hidden)
        {
            ShowNextReward();
        }
    }

    /// <summary>从队列取出下一项并绑定图标、名称和最终等级。</summary>
    private void ShowNextReward()
    {
        if (_pendingRewards.Count == 0)
        {
            HideImmediately();
            return;
        }

        ChestRewardResult result = _pendingRewards.Dequeue();
        WeaponDataSO weaponData = result.WeaponData;
        _displayedRewardName = weaponData != null
            ? weaponData.GetDisplayName()
            : result.UpgradeData != null
                ? result.UpgradeData.GetDisplayName()
                : string.Empty;
        _displayedRewardLevel = Mathf.Max(1, result.CurrentLevel);

        _rewardIcon.sprite = weaponData != null && weaponData.icon != null
            ? weaponData.icon
            : result.UpgradeData != null
                ? result.UpgradeData.icon
                : null;
        _rewardIcon.enabled = _rewardIcon.sprite != null;
        string rewardTitle = ResolveLocalizedText(rewardTitleKey, rewardTitleFallback);
        _rewardText.text = $"{rewardTitle}：{_displayedRewardName}  Lv.{_displayedRewardLevel}";
        _panelRoot.gameObject.SetActive(true);
        BeginPhase(ToastPhase.FadeIn);
        ApplyFadeIn(0f);
    }

    /// <summary>切换动画阶段并清零阶段计时。</summary>
    private void BeginPhase(ToastPhase nextPhase)
    {
        _phase = nextPhase;
        _phaseElapsed = 0f;
    }

    /// <summary>推进淡入阶段并在完成后进入停留阶段。</summary>
    private void UpdateFadeIn()
    {
        float progress = Mathf.Clamp01(_phaseElapsed / fadeInDuration);
        ApplyFadeIn(progress);
        if (progress >= 1f)
        {
            BeginPhase(ToastPhase.Hold);
        }
    }

    /// <summary>应用从顶部轻微滑入并提高透明度的视觉状态。</summary>
    private void ApplyFadeIn(float progress)
    {
        float eased = 1f - (1f - progress) * (1f - progress);
        _canvasGroup.alpha = eased;
        _panelRoot.anchoredPosition = Vector2.Lerp(
            _basePosition + Vector2.up * 20f,
            _basePosition,
            eased);
    }

    /// <summary>推进淡出；结束后立即展示队列下一项或完全隐藏。</summary>
    private void UpdateFadeOut()
    {
        float progress = Mathf.Clamp01(_phaseElapsed / fadeOutDuration);
        _canvasGroup.alpha = 1f - progress;
        _panelRoot.anchoredPosition = Vector2.Lerp(
            _basePosition,
            _basePosition + Vector2.up * 12f,
            progress);
        if (progress < 1f)
        {
            return;
        }

        if (_pendingRewards.Count > 0)
        {
            ShowNextReward();
        }
        else
        {
            HideImmediately();
        }
    }

    /// <summary>不播放动画地恢复隐藏状态，供初始化与停用清理。</summary>
    private void HideImmediately()
    {
        _phase = ToastPhase.Hidden;
        _phaseElapsed = 0f;
        _canvasGroup.alpha = 0f;
        _panelRoot.anchoredPosition = _basePosition;
        _panelRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// 当前项目尚未接入运行时本地化服务，因此使用键与回退文案的双字段契约。
    /// 后续接入服务时只需替换此入口，无需改动奖励事件或 UI 状态机。
    /// </summary>
    private static string ResolveLocalizedText(string localizationKey, string fallback)
    {
        return !string.IsNullOrWhiteSpace(fallback) ? fallback : localizationKey;
    }
}
