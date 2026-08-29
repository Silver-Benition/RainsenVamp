using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 在主菜单画布上构建并管理角色选择页：固定槽位、悬停预览、角色信息和确认流程。
/// </summary>
public sealed class CharacterSelectionUI : MonoBehaviour
{
    private static readonly Color Orange = new Color32(255, 157, 47, 255);
    private static readonly Color Cyan = new Color32(62, 205, 238, 255);
    private static readonly Color PanelColor = new Color32(12, 32, 57, 245);
    private static readonly Color SectionColor = new Color32(18, 45, 75, 245);

    [Header("Characters")]
    [SerializeField] private List<CharacterDataSO> availableCharacters = new List<CharacterDataSO>();
    [SerializeField, Min(1)] private int slotCapacity = 12;
    [SerializeField, Min(1)] private int columns = 4;

    [Header("Presentation")]
    [SerializeField, Min(0.01f)] private float portraitSlideDuration = 0.4f;
    [SerializeField, Min(0.05f)] private float previewFrameInterval = 0.2f;

    private readonly List<CharacterSelectionSlotUI> _slots = new List<CharacterSelectionSlotUI>();
    private RectTransform _panelRoot;
    private RectTransform _leftPortraitRect;
    private RectTransform _rightPortraitRect;
    private Image _leftPortrait;
    private Image _rightPortrait;
    private Image _previewImage;
    private TMP_Text _characterNameText;
    private TMP_Text _statsText;
    private TMP_Text _starterWeaponNameText;
    private TMP_Text _starterWeaponDescriptionText;
    private TMP_Text _passiveNameText;
    private TMP_Text _passiveDescriptionText;
    private GameObject _unlockOverlay;
    private TMP_Text _unlockConditionText;
    private Button _unlockPurchaseButton;
    private TMP_Text _unlockPurchaseButtonLabel;
    private Button _confirmButton;
    private Button _backButton;
    private TMP_FontAsset _font;
    private CharacterSelectionSlotUI _selectedSlot;
    private CharacterDataSO _selectedCharacter;
    private float _portraitElapsed;
    private float _previewElapsed;
    private int _previewFrameIndex;
    private bool _interactionLocked;
    private AccountProgressService _accountProgress;

    private const float LeftPortraitTargetX = -700f;
    private const float RightPortraitTargetX = 700f;
    private const float PortraitStartOffset = 420f;

    /// <summary>用户确认了一个角色。</summary>
    public event Action<CharacterDataSO> CharacterConfirmed;

    /// <summary>用户取消并返回主菜单。</summary>
    public event Action Closed;

    /// <summary>选择页当前是否显示并拦截主菜单输入。</summary>
    public bool IsVisible => _panelRoot != null && _panelRoot.gameObject.activeSelf;

    /// <summary>当前页面实际创建的固定槽位数量。</summary>
    public int SlotCount => _slots.Count;

    /// <summary>当前拥有角色数据的可用槽位数量。</summary>
    public int AvailableSlotCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _slots.Count; index++)
            {
                if (_slots[index].IsAvailable)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>悬停或导航选中的角色。</summary>
    public CharacterDataSO SelectedCharacter => _selectedCharacter;

    /// <summary>当前属性文本，供自动化验证和无障碍读取。</summary>
    public string StatsText => _statsText != null ? _statsText.text : string.Empty;

    /// <summary>左右立绘渐入进度，范围为 0 到 1。</summary>
    public float PortraitAnimationProgress => Mathf.Clamp01(
        _portraitElapsed / Mathf.Max(0.01f, portraitSlideDuration));

    /// <summary>角色选择页根节点，供菜单流程与测试定位。</summary>
    public GameObject PanelRoot => _panelRoot != null ? _panelRoot.gameObject : null;

    /// <summary>确认按钮引用，供键盘焦点和自动化流程读取。</summary>
    public Button ConfirmButton => _confirmButton;

    /// <summary>锁定角色信息浮层当前是否显示。</summary>
    public bool IsUnlockOverlayVisible => _unlockOverlay != null && _unlockOverlay.activeSelf;

    /// <summary>当前锁定角色的解锁条件文本。</summary>
    public string UnlockConditionText => _unlockConditionText != null
        ? _unlockConditionText.text
        : string.Empty;

    /// <summary>金币购买按钮；非金币条件下保持隐藏。</summary>
    public Button UnlockPurchaseButton => _unlockPurchaseButton;

    private void Awake()
    {
        EnsureAccountProgress();
        _accountProgress.EvaluateAutomaticUnlocks(availableCharacters);
        _font = GetComponentInChildren<TMP_Text>(true)?.font;
        BuildInterface();
        Hide();
    }

    /// <summary>启用时订阅账号进度，保证金币、解锁和重置后页面立即刷新。</summary>
    private void OnEnable()
    {
        if (_accountProgress == null)
        {
            _accountProgress = AccountProgressService.Current;
        }

        _accountProgress.Changed -= HandleAccountProgressChanged;
        _accountProgress.Changed += HandleAccountProgressChanged;
    }

    /// <summary>停用时解除账号事件，防止场景重载后旧页面继续响应。</summary>
    private void OnDisable()
    {
        if (_accountProgress != null)
        {
            _accountProgress.Changed -= HandleAccountProgressChanged;
        }
    }

    private void Update()
    {
        if (!IsVisible)
        {
            return;
        }

        TickPortraitAnimation();
        TickPreviewAnimation();

        if (!_interactionLocked && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    /// <summary>打开页面并默认展示首个有效角色。</summary>
    public void Show()
    {
        EnsureAccountProgress();
        if (_panelRoot == null)
        {
            BuildInterface();
        }

        _interactionLocked = false;
        _accountProgress.EvaluateAutomaticUnlocks(availableCharacters);
        RefreshSlotUnlockStates();
        _panelRoot.gameObject.SetActive(true);
        _panelRoot.SetAsLastSibling();

        CharacterSelectionSlotUI firstAvailable = null;
        for (int index = 0; index < _slots.Count; index++)
        {
            CharacterSelectionSlotUI slot = _slots[index];
            if (slot.IsUnlocked && slot.Character != null &&
                string.Equals(
                    slot.Character.characterID,
                    _accountProgress.LastSelectedCharacterId,
                    StringComparison.Ordinal))
            {
                firstAvailable = slot;
                break;
            }
        }

        if (firstAvailable == null)
        {
            for (int index = 0; index < _slots.Count; index++)
            {
                if (_slots[index].IsUnlocked)
                {
                    firstAvailable = _slots[index];
                    break;
                }
            }
        }

        SelectSlot(firstAvailable, true);
        if (EventSystem.current != null && firstAvailable != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(firstAvailable.gameObject);
        }
    }

    /// <summary>隐藏页面但保留已构建对象，以便从返回按钮再次打开。</summary>
    public void Hide()
    {
        if (_panelRoot != null)
        {
            _panelRoot.gameObject.SetActive(false);
        }
    }

    /// <summary>在加载游戏期间锁定选择页所有可提交控件。</summary>
    public void SetInteractionEnabled(bool enabled)
    {
        _interactionLocked = !enabled;
        for (int index = 0; index < _slots.Count; index++)
        {
            CharacterSelectionSlotUI slot = _slots[index];
            Button button = slot.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = enabled && slot.IsAvailable;
            }
        }

        if (_confirmButton != null)
        {
            _confirmButton.interactable = enabled && _selectedSlot != null && _selectedSlot.IsUnlocked;
        }

        if (_backButton != null)
        {
            _backButton.interactable = enabled;
        }

        RefreshUnlockOverlay();
    }

    /// <summary>有效槽位被鼠标悬停或导航选中时刷新展示。</summary>
    public void HandleSlotHover(CharacterSelectionSlotUI slot)
    {
        if (!_interactionLocked && slot != null && slot.IsAvailable)
        {
            SelectSlot(slot, slot.Character != _selectedCharacter);
        }
    }

    /// <summary>鼠标点击角色槽位时选择该角色；再次点击已选角色可直接确认。</summary>
    public void HandleSlotClick(CharacterSelectionSlotUI slot)
    {
        if (_interactionLocked || slot == null || !slot.IsAvailable)
        {
            return;
        }

        if (_selectedSlot == slot && slot.IsUnlocked)
        {
            Confirm();
            return;
        }

        SelectSlot(slot, true);
    }

    private void BuildInterface()
    {
        if (_panelRoot != null)
        {
            return;
        }

        _panelRoot = CreateRect("CharacterSelectPanel", transform);
        Stretch(_panelRoot);
        Image backdrop = _panelRoot.gameObject.AddComponent<Image>();
        backdrop.color = new Color32(5, 16, 32, 252);

        CreatePortraits();
        CreateHeader();
        CreateSlots();
        CreateInfoPanel();
        CreateFooterButtons();
    }

    private void CreatePortraits()
    {
        _leftPortraitRect = CreateRect("LeftCharacterPortrait", _panelRoot);
        SetCenteredRect(_leftPortraitRect, new Vector2(430f, 630f), new Vector2(LeftPortraitTargetX, 10f));
        _leftPortrait = _leftPortraitRect.gameObject.AddComponent<Image>();
        _leftPortrait.preserveAspect = true;
        _leftPortrait.raycastTarget = false;

        _rightPortraitRect = CreateRect("RightCharacterPortrait", _panelRoot);
        SetCenteredRect(_rightPortraitRect, new Vector2(430f, 630f), new Vector2(RightPortraitTargetX, 10f));
        _rightPortraitRect.localScale = new Vector3(-1f, 1f, 1f);
        _rightPortrait = _rightPortraitRect.gameObject.AddComponent<Image>();
        _rightPortrait.preserveAspect = true;
        _rightPortrait.raycastTarget = false;
    }

    private void CreateHeader()
    {
        TMP_Text title = CreateText("CharacterSelectTitle", _panelRoot, "选择角色", 52f, TextAlignmentOptions.Center);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(760f, 72f);
        titleRect.anchoredPosition = new Vector2(0f, -28f);
        title.color = Color.white;
        title.fontStyle = FontStyles.Bold;

        TMP_Text hint = CreateText(
            "CharacterSelectHint",
            _panelRoot,
            "移动鼠标查看角色 · 点击已选角色或按确认进入游戏",
            22f,
            TextAlignmentOptions.Center);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0.5f, 1f);
        hintRect.anchorMax = new Vector2(0.5f, 1f);
        hintRect.pivot = new Vector2(0.5f, 1f);
        hintRect.sizeDelta = new Vector2(900f, 40f);
        hintRect.anchoredPosition = new Vector2(0f, -98f);
        hint.color = new Color32(158, 204, 228, 255);
    }

    private void CreateSlots()
    {
        RectTransform gridRect = CreateRect("CharacterSlotGrid", _panelRoot);
        gridRect.anchorMin = new Vector2(0.5f, 1f);
        gridRect.anchorMax = new Vector2(0.5f, 1f);
        gridRect.pivot = new Vector2(0.5f, 1f);
        gridRect.sizeDelta = new Vector2(720f, 300f);
        gridRect.anchoredPosition = new Vector2(0f, -148f);

        GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
        int safeColumns = Mathf.Max(1, columns);
        int safeCapacity = Mathf.Max(1, slotCapacity);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = safeColumns;
        grid.cellSize = new Vector2(164f, 84f);
        grid.spacing = new Vector2(16f, 16f);
        grid.childAlignment = TextAnchor.UpperCenter;

        for (int index = 0; index < safeCapacity; index++)
        {
            CharacterDataSO character = index < availableCharacters.Count
                ? availableCharacters[index]
                : null;
            CreateSlot(gridRect, index, character);
        }
    }

    private void CreateSlot(RectTransform parent, int index, CharacterDataSO character)
    {
        RectTransform slotRect = CreateRect($"CharacterSlot_{index + 1:00}", parent);
        Image background = slotRect.gameObject.AddComponent<Image>();
        Button button = slotRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        Outline outline = slotRect.gameObject.AddComponent<Outline>();

        RectTransform portraitRect = CreateRect("Portrait", slotRect);
        portraitRect.anchorMin = new Vector2(0f, 0.24f);
        portraitRect.anchorMax = new Vector2(1f, 1f);
        portraitRect.offsetMin = new Vector2(8f, 5f);
        portraitRect.offsetMax = new Vector2(-8f, -5f);
        Image portrait = portraitRect.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true;
        portrait.raycastTarget = false;

        TMP_Text label = CreateText("Name", slotRect, string.Empty, 18f, TextAlignmentOptions.Center);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(1f, 0.25f);
        labelRect.offsetMin = new Vector2(4f, 0f);
        labelRect.offsetMax = new Vector2(-4f, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 18f;
        label.raycastTarget = false;

        CharacterSelectionSlotUI slot = slotRect.gameObject.AddComponent<CharacterSelectionSlotUI>();
        bool isUnlocked = character != null &&
            _accountProgress.IsCharacterUnlocked(character.characterID);
        slot.Bind(this, index, character, background, portrait, label, button, outline, isUnlocked);
        _slots.Add(slot);
    }

    private void CreateInfoPanel()
    {
        RectTransform infoRect = CreateRect("CharacterInfoPanel", _panelRoot);
        infoRect.anchorMin = new Vector2(0.035f, 0.085f);
        infoRect.anchorMax = new Vector2(0.965f, 0.39f);
        infoRect.offsetMin = Vector2.zero;
        infoRect.offsetMax = Vector2.zero;
        Image infoBackground = infoRect.gameObject.AddComponent<Image>();
        infoBackground.color = PanelColor;
        Outline infoOutline = infoRect.gameObject.AddComponent<Outline>();
        infoOutline.effectColor = Cyan;
        infoOutline.effectDistance = new Vector2(3f, -3f);

        RectTransform left = CreateSection("CharacterSummary", infoRect, 0f, 0.34f);
        RectTransform middle = CreateSection("StarterWeapon", infoRect, 0.34f, 0.67f);
        RectTransform right = CreateSection("SignatureAbility", infoRect, 0.67f, 1f);

        BuildCharacterSummary(left);
        BuildDetailSection(
            middle,
            "初始武器",
            "待配置",
            "角色基础武器将在后续版本接入角色配置。",
            Orange,
            out _starterWeaponNameText,
            out _starterWeaponDescriptionText);
        BuildDetailSection(
            right,
            "角色能力",
            "待配置",
            "固有能力将在后续版本接入，且不占用局内能力／属性栏位。",
            Cyan,
            out _passiveNameText,
            out _passiveDescriptionText);
        CreateUnlockOverlay(infoRect);
    }

    private void BuildCharacterSummary(RectTransform section)
    {
        _characterNameText = CreateText(
            "CharacterName",
            section,
            string.Empty,
            30f,
            TextAlignmentOptions.Center);
        RectTransform nameRect = _characterNameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0.79f);
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = new Vector2(12f, 0f);
        nameRect.offsetMax = new Vector2(-12f, -4f);
        _characterNameText.fontStyle = FontStyles.Bold;
        _characterNameText.color = Orange;

        RectTransform previewRect = CreateRect("CharacterPreview", section);
        previewRect.anchorMin = new Vector2(0.04f, 0.08f);
        previewRect.anchorMax = new Vector2(0.45f, 0.76f);
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = Vector2.zero;
        _previewImage = previewRect.gameObject.AddComponent<Image>();
        _previewImage.preserveAspect = true;
        _previewImage.raycastTarget = false;

        _statsText = CreateText("CharacterStats", section, string.Empty, 24f, TextAlignmentOptions.MidlineLeft);
        RectTransform statsRect = _statsText.rectTransform;
        statsRect.anchorMin = new Vector2(0.48f, 0.08f);
        statsRect.anchorMax = new Vector2(0.98f, 0.76f);
        statsRect.offsetMin = Vector2.zero;
        statsRect.offsetMax = Vector2.zero;
        _statsText.lineSpacing = 18f;
    }

    private void BuildDetailSection(
        RectTransform section,
        string heading,
        string itemName,
        string description,
        Color accent,
        out TMP_Text itemText,
        out TMP_Text descriptionText)
    {
        TMP_Text headingText = CreateText("Heading", section, heading, 28f, TextAlignmentOptions.Center);
        RectTransform headingRect = headingText.rectTransform;
        headingRect.anchorMin = new Vector2(0.04f, 0.76f);
        headingRect.anchorMax = new Vector2(0.96f, 0.98f);
        headingRect.offsetMin = Vector2.zero;
        headingRect.offsetMax = Vector2.zero;
        headingText.color = accent;
        headingText.fontStyle = FontStyles.Bold;

        itemText = CreateText("ItemName", section, itemName, 24f, TextAlignmentOptions.Center);
        RectTransform itemRect = itemText.rectTransform;
        itemRect.anchorMin = new Vector2(0.08f, 0.57f);
        itemRect.anchorMax = new Vector2(0.92f, 0.76f);
        itemRect.offsetMin = Vector2.zero;
        itemRect.offsetMax = Vector2.zero;

        descriptionText = CreateText(
            "Description",
            section,
            description,
            20f,
            TextAlignmentOptions.TopLeft);
        RectTransform descriptionRect = descriptionText.rectTransform;
        descriptionRect.anchorMin = new Vector2(0.08f, 0.08f);
        descriptionRect.anchorMax = new Vector2(0.92f, 0.54f);
        descriptionRect.offsetMin = Vector2.zero;
        descriptionRect.offsetMax = Vector2.zero;
        descriptionText.color = new Color32(207, 221, 233, 255);
        descriptionText.enableWordWrapping = true;
    }

    /// <summary>在整个角色信息栏上方建立锁定浮层，避免泄露任何未解锁角色资料。</summary>
    private void CreateUnlockOverlay(RectTransform infoRect)
    {
        RectTransform overlayRect = CreateRect("CharacterUnlockOverlay", infoRect);
        Stretch(overlayRect);
        Image background = overlayRect.gameObject.AddComponent<Image>();
        background.color = new Color32(5, 15, 28, 252);

        TMP_Text title = CreateText(
            "UnlockTitle",
            overlayRect,
            "角色尚未解锁",
            34f,
            TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = new Vector2(0.12f, 0.68f);
        title.rectTransform.anchorMax = new Vector2(0.88f, 0.94f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;
        title.fontStyle = FontStyles.Bold;
        title.color = Orange;

        _unlockConditionText = CreateText(
            "UnlockCondition",
            overlayRect,
            string.Empty,
            25f,
            TextAlignmentOptions.Center);
        _unlockConditionText.rectTransform.anchorMin = new Vector2(0.12f, 0.30f);
        _unlockConditionText.rectTransform.anchorMax = new Vector2(0.88f, 0.70f);
        _unlockConditionText.rectTransform.offsetMin = Vector2.zero;
        _unlockConditionText.rectTransform.offsetMax = Vector2.zero;
        _unlockConditionText.enableWordWrapping = true;

        _unlockPurchaseButton = CreateButton(
            "CharacterUnlockPurchaseButton",
            overlayRect,
            "解锁角色",
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 22f),
            new Vector2(360f, 58f));
        _unlockPurchaseButton.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
        _unlockPurchaseButtonLabel = _unlockPurchaseButton.GetComponentInChildren<TMP_Text>(true);
        _unlockPurchaseButton.onClick.AddListener(PurchaseSelectedCharacter);
        _unlockOverlay = overlayRect.gameObject;
        _unlockOverlay.SetActive(false);
    }

    private void CreateFooterButtons()
    {
        _backButton = CreateButton(
            "CharacterBackButton",
            _panelRoot,
            "返回",
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(48f, 22f),
            new Vector2(180f, 52f));
        _backButton.GetComponent<RectTransform>().pivot = Vector2.zero;
        _backButton.onClick.AddListener(Close);

        _confirmButton = CreateButton(
            "CharacterConfirmButton",
            _panelRoot,
            "确认角色",
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-48f, 22f),
            new Vector2(220f, 52f));
        _confirmButton.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
        _confirmButton.onClick.AddListener(Confirm);
    }

    private void SelectSlot(CharacterSelectionSlotUI slot, bool restartPortraitAnimation)
    {
        _selectedSlot = slot;
        _selectedCharacter = slot != null ? slot.Character : null;

        for (int index = 0; index < _slots.Count; index++)
        {
            _slots[index].SetSelected(_slots[index] == slot);
        }

        if (_selectedCharacter == null)
        {
            _characterNameText.text = "暂无可选角色";
            _statsText.text = string.Empty;
            _starterWeaponNameText.text = string.Empty;
            _starterWeaponDescriptionText.text = string.Empty;
            _passiveNameText.text = string.Empty;
            _passiveDescriptionText.text = string.Empty;
            _previewImage.enabled = false;
            _leftPortrait.enabled = false;
            _rightPortrait.enabled = false;
            _confirmButton.interactable = false;
            RefreshUnlockOverlay();
            return;
        }

        if (_selectedSlot == null || !_selectedSlot.IsUnlocked)
        {
            // 锁定角色只允许查看解锁目标；姓名、属性、立绘、起始武器和被动全部隐藏。
            _characterNameText.text = string.Empty;
            _statsText.text = string.Empty;
            _starterWeaponNameText.text = string.Empty;
            _starterWeaponDescriptionText.text = string.Empty;
            _passiveNameText.text = string.Empty;
            _passiveDescriptionText.text = string.Empty;
            _previewImage.enabled = false;
            _leftPortrait.enabled = false;
            _rightPortrait.enabled = false;
            _confirmButton.interactable = false;
            RefreshUnlockOverlay();
            return;
        }

        RefreshUnlockOverlay();
        Sprite portrait = _selectedCharacter.GetPortraitSprite();
        _leftPortrait.sprite = portrait;
        _rightPortrait.sprite = portrait;
        _leftPortrait.enabled = portrait != null;
        _rightPortrait.enabled = portrait != null;
        _characterNameText.text = _selectedCharacter.GetDisplayName();
        _statsText.text = BuildStatsText(_selectedCharacter);
        WeaponDataSO startingWeapon = _selectedCharacter.startingWeapon;
        _starterWeaponNameText.text = startingWeapon != null
            ? startingWeapon.GetDisplayName()
            : "未配置";
        _starterWeaponDescriptionText.text = startingWeapon != null
            ? startingWeapon.GetDisplayDescription()
            : "该角色尚未配置起始武器。";
        CharacterPassiveDefinition passive = _selectedCharacter.passive;
        _passiveNameText.text = passive != null ? passive.GetDisplayName() : "无固有被动";
        _passiveDescriptionText.text = passive != null
            ? passive.GetDisplayDescription()
            : "该角色当前没有额外的固有属性效果。";
        _previewFrameIndex = 0;
        _previewElapsed = 0f;
        RefreshPreviewFrame();
        _confirmButton.interactable = !_interactionLocked && _selectedSlot.IsUnlocked;

        if (restartPortraitAnimation)
        {
            ResetPortraitAnimation();
        }
    }

    private static string BuildStatsText(CharacterDataSO character)
    {
        float maxHealth = character.GetBaseValue(PlayerStatType.MaxHealth);
        float might = character.GetBaseValue(PlayerStatType.Might) * 100f;
        float moveSpeed = character.GetBaseValue(PlayerStatType.MoveSpeed);
        return $"生命  {maxHealth:0.#}\n力量  {might:0.#}%\n移动速度  {moveSpeed:0.##}";
    }

    private void ResetPortraitAnimation()
    {
        _portraitElapsed = 0f;
        ApplyPortraitAnimation(0f);
    }

    private void TickPortraitAnimation()
    {
        if (PortraitAnimationProgress >= 1f)
        {
            return;
        }

        _portraitElapsed += Time.unscaledDeltaTime;
        ApplyPortraitAnimation(PortraitAnimationProgress);
    }

    private void ApplyPortraitAnimation(float progress)
    {
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        float leftX = Mathf.Lerp(LeftPortraitTargetX - PortraitStartOffset, LeftPortraitTargetX, eased);
        float rightX = Mathf.Lerp(RightPortraitTargetX + PortraitStartOffset, RightPortraitTargetX, eased);
        _leftPortraitRect.anchoredPosition = new Vector2(leftX, 10f);
        _rightPortraitRect.anchoredPosition = new Vector2(rightX, 10f);

        Color portraitColor = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 0.38f, eased));
        _leftPortrait.color = portraitColor;
        _rightPortrait.color = portraitColor;
    }

    private void TickPreviewAnimation()
    {
        if (_selectedCharacter == null || _selectedSlot == null || !_selectedSlot.IsUnlocked)
        {
            return;
        }

        _previewElapsed += Time.unscaledDeltaTime;
        if (_previewElapsed < previewFrameInterval)
        {
            return;
        }

        _previewElapsed %= Mathf.Max(0.05f, previewFrameInterval);
        _previewFrameIndex++;
        RefreshPreviewFrame();
    }

    private void RefreshPreviewFrame()
    {
        Sprite frame = _selectedCharacter != null && _selectedSlot != null && _selectedSlot.IsUnlocked
            ? _selectedCharacter.GetPreviewFrame(_previewFrameIndex)
            : null;
        _previewImage.sprite = frame;
        _previewImage.enabled = frame != null;
    }

    /// <summary>按账号权威状态刷新全部已占用角色槽位的锁定表现。</summary>
    private void RefreshSlotUnlockStates()
    {
        if (_accountProgress == null)
        {
            return;
        }

        for (int index = 0; index < _slots.Count; index++)
        {
            CharacterSelectionSlotUI slot = _slots[index];
            CharacterDataSO character = slot != null ? slot.Character : null;
            slot?.SetUnlocked(
                character != null && _accountProgress.IsCharacterUnlocked(character.characterID));
        }
    }

    /// <summary>惰性解析账号服务，兼容 EditMode 中不会自动执行 Awake 的轻量组件夹具。</summary>
    private void EnsureAccountProgress()
    {
        if (_accountProgress == null)
        {
            _accountProgress = AccountProgressService.Current;
        }
    }

    /// <summary>账号变化后重新评估自动条件，并刷新当前选择而不泄露锁定资料。</summary>
    private void HandleAccountProgressChanged()
    {
        _accountProgress.EvaluateAutomaticUnlocks(availableCharacters);
        RefreshSlotUnlockStates();
        if (_selectedSlot != null)
        {
            SelectSlot(_selectedSlot, _selectedSlot.IsUnlocked);
        }
    }

    /// <summary>根据当前锁定角色展示累计击杀进度或金币购买按钮。</summary>
    private void RefreshUnlockOverlay()
    {
        if (_unlockOverlay == null)
        {
            return;
        }

        bool lockedCharacterSelected = _selectedCharacter != null &&
            (_selectedSlot == null || !_selectedSlot.IsUnlocked);
        _unlockOverlay.SetActive(lockedCharacterSelected);
        if (!lockedCharacterSelected)
        {
            return;
        }

        CharacterUnlockDefinition unlock = _selectedCharacter.unlock;
        if (unlock == null)
        {
            _unlockConditionText.text = "解锁条件尚未配置";
            _unlockPurchaseButton.gameObject.SetActive(false);
            return;
        }

        int required = Mathf.Max(0, unlock.requiredAmount);
        switch (unlock.conditionType)
        {
            case CharacterUnlockConditionType.LifetimeKills:
                _unlockConditionText.text =
                    $"解锁条件\n账号累计击杀 {_accountProgress.LifetimeKills} / {required}";
                _unlockPurchaseButton.gameObject.SetActive(false);
                break;
            case CharacterUnlockConditionType.GoldPurchase:
                _unlockConditionText.text =
                    $"解锁条件\n{unlock.GetDisplayDescription()}\n当前账号金币：{_accountProgress.Gold}";
                _unlockPurchaseButton.gameObject.SetActive(true);
                _unlockPurchaseButton.interactable =
                    !_interactionLocked && _accountProgress.Gold >= required;
                if (_unlockPurchaseButtonLabel != null)
                {
                    _unlockPurchaseButtonLabel.text = _accountProgress.Gold >= required
                        ? $"花费 {required} 金币解锁"
                        : $"金币不足（需要 {required}）";
                }
                break;
            default:
                _unlockConditionText.text = $"解锁条件\n{unlock.GetDisplayDescription()}";
                _unlockPurchaseButton.gameObject.SetActive(false);
                break;
        }
    }

    /// <summary>点击浮层按钮时请求账号服务扣除金币并永久解锁当前角色。</summary>
    private void PurchaseSelectedCharacter()
    {
        if (_interactionLocked || _selectedCharacter == null || _selectedSlot == null ||
            _selectedSlot.IsUnlocked)
        {
            return;
        }

        if (_accountProgress.TryPurchaseCharacter(_selectedCharacter))
        {
            // 购买入口自身也刷新一次，避免 EditMode 夹具或临时禁用状态尚未订阅 Changed 时残留旧浮层。
            RefreshSlotUnlockStates();
            SelectSlot(_selectedSlot, true);
            return;
        }

        RefreshUnlockOverlay();
    }

    private void Confirm()
    {
        if (_interactionLocked || _selectedCharacter == null ||
            _selectedSlot == null || !_selectedSlot.IsUnlocked)
        {
            return;
        }

        SetInteractionEnabled(false);
        CharacterConfirmed?.Invoke(_selectedCharacter);
    }

    private void Close()
    {
        if (_interactionLocked)
        {
            return;
        }

        Hide();
        Closed?.Invoke();
    }

    private RectTransform CreateSection(string name, RectTransform parent, float minX, float maxX)
    {
        RectTransform section = CreateRect(name, parent);
        section.anchorMin = new Vector2(minX, 0f);
        section.anchorMax = new Vector2(maxX, 1f);
        section.offsetMin = new Vector2(5f, 5f);
        section.offsetMax = new Vector2(-5f, -5f);
        Image background = section.gameObject.AddComponent<Image>();
        background.color = SectionColor;
        background.raycastTarget = false;
        return section;
    }

    private Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(25, 71, 108, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(255, 190, 104, 255);
        colors.selectedColor = new Color32(255, 190, 104, 255);
        colors.pressedColor = Orange;
        button.colors = colors;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Orange;
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text text = CreateText("Label", rect, label, 24f, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return button;
    }

    private TMP_Text CreateText(
        string name,
        Transform parent,
        string content,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null)
        {
            text.font = _font;
        }

        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }
}
