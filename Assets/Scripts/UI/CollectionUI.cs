using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>主菜单收藏页面，展示角色、武器、升级发现状态并管理账号级 Seal。</summary>
public sealed class CollectionUI : MonoBehaviour
{
    private enum CollectionCategory
    {
        Characters = 0,
        Weapons = 1,
        Upgrades = 2
    }

    [Header("Content")]
    [SerializeField] private GameContentCatalogSO contentCatalog;

    [Header("Scene UI")]
    [SerializeField] private RectTransform _panelRoot;
    [SerializeField] private RectTransform _contentRoot;
    [SerializeField] private TMP_Text _goldText;
    [SerializeField] private TMP_Text _sealText;
    [SerializeField] private Button _characterTabButton;
    [SerializeField] private Button _weaponTabButton;
    [SerializeField] private Button _upgradeTabButton;
    [SerializeField] private Button _backButton;

    private TMP_FontAsset _font;
    private AccountProgressService _accountProgress;
    private CollectionCategory _category;

    /// <summary>用户关闭收藏页并返回主菜单。</summary>
    public event Action Closed;

    /// <summary>收藏页当前是否显示。</summary>
    public bool IsVisible => _panelRoot != null && _panelRoot.gameObject.activeSelf;

    /// <summary>当前分类生成的收藏条目数量。</summary>
    public int VisibleEntryCount => _contentRoot != null ? _contentRoot.childCount : 0;

    /// <summary>场景内收藏面板根节点，供结构测试和编辑器诊断读取。</summary>
    public RectTransform PanelRoot => _panelRoot;

    /// <summary>固定收藏页骨架是否已经完整序列化进场景。</summary>
    public bool HasSceneReferences => _panelRoot != null && _contentRoot != null &&
        _goldText != null && _sealText != null && _characterTabButton != null &&
        _weaponTabButton != null && _upgradeTabButton != null && _backButton != null;

    /// <summary>缓存字体与账号服务，并校验收藏页固定骨架已由场景持有。</summary>
    private void Awake()
    {
        _font = GetComponentInChildren<TMP_Text>(true)?.font;
        _accountProgress = AccountProgressService.Current;
        if (!HasSceneReferences)
        {
            Debug.LogError("CollectionUI 缺少场景内固定 UI 引用，收藏页已停用。", this);
            enabled = false;
            return;
        }

        Hide();
    }

    /// <summary>启用时订阅账号变化，让金币和 Seal 状态实时同步。</summary>
    private void OnEnable()
    {
        if (!HasSceneReferences)
        {
            return;
        }

        if (_accountProgress == null)
        {
            _accountProgress = AccountProgressService.Current;
        }

        _accountProgress.Changed -= Refresh;
        _accountProgress.Changed += Refresh;
        _characterTabButton.onClick.RemoveListener(ShowCharacters);
        _characterTabButton.onClick.AddListener(ShowCharacters);
        _weaponTabButton.onClick.RemoveListener(ShowWeapons);
        _weaponTabButton.onClick.AddListener(ShowWeapons);
        _upgradeTabButton.onClick.RemoveListener(ShowUpgrades);
        _upgradeTabButton.onClick.AddListener(ShowUpgrades);
        _backButton.onClick.RemoveListener(Close);
        _backButton.onClick.AddListener(Close);
    }

    /// <summary>停用时解除账号事件订阅。</summary>
    private void OnDisable()
    {
        if (_accountProgress != null)
        {
            _accountProgress.Changed -= Refresh;
        }

        if (_characterTabButton != null)
        {
            _characterTabButton.onClick.RemoveListener(ShowCharacters);
        }
        if (_weaponTabButton != null)
        {
            _weaponTabButton.onClick.RemoveListener(ShowWeapons);
        }
        if (_upgradeTabButton != null)
        {
            _upgradeTabButton.onClick.RemoveListener(ShowUpgrades);
        }
        if (_backButton != null)
        {
            _backButton.onClick.RemoveListener(Close);
        }
    }

    /// <summary>收藏页显示期间允许 Escape 返回主菜单。</summary>
    private void Update()
    {
        if (IsVisible && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    /// <summary>打开收藏页并刷新当前分类、账号金币和 Seal 容量。</summary>
    public void Show()
    {
        if (!HasSceneReferences)
        {
            Debug.LogError("收藏页场景引用不完整，无法打开。", this);
            return;
        }

        _panelRoot.gameObject.SetActive(true);
        _panelRoot.SetAsLastSibling();
        Refresh();
    }

    /// <summary>隐藏收藏页但保留固定 UI 骨架以便下次复用。</summary>
    public void Hide()
    {
        if (_panelRoot != null)
        {
            _panelRoot.gameObject.SetActive(false);
        }
    }

#if UNITY_EDITOR
    /// <summary>仅供编辑器场景维护工具一次性生成可序列化的收藏页固定骨架。</summary>
    public void AuthorSceneInterface(GameContentCatalogSO catalog)
    {
        contentCatalog = catalog;
        _font = GetComponentInChildren<TMP_Text>(true)?.font;
        BuildEditorInterface();
        Hide();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>在编辑器中创建页面背景、分类按钮、状态文本、内容网格和返回按钮。</summary>
    private void BuildEditorInterface()
    {
        if (_panelRoot != null)
        {
            return;
        }

        _panelRoot = CreateRect("CollectionPanel", transform);
        Stretch(_panelRoot);
        Image backdrop = _panelRoot.gameObject.AddComponent<Image>();
        backdrop.color = new Color32(5, 16, 32, 252);

        TMP_Text title = CreateText(
            "CollectionTitle",
            _panelRoot,
            "收藏",
            50f,
            TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = new Vector2(0.35f, 0.91f);
        title.rectTransform.anchorMax = new Vector2(0.65f, 0.99f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;
        title.fontStyle = FontStyles.Bold;

        _goldText = CreateText(
            "AccountGoldText",
            _panelRoot,
            string.Empty,
            24f,
            TextAlignmentOptions.MidlineRight);
        _goldText.rectTransform.anchorMin = new Vector2(0.68f, 0.91f);
        _goldText.rectTransform.anchorMax = new Vector2(0.96f, 0.98f);
        _goldText.rectTransform.offsetMin = Vector2.zero;
        _goldText.rectTransform.offsetMax = Vector2.zero;

        _sealText = CreateText(
            "AccountSealText",
            _panelRoot,
            string.Empty,
            22f,
            TextAlignmentOptions.MidlineRight);
        _sealText.rectTransform.anchorMin = new Vector2(0.68f, 0.86f);
        _sealText.rectTransform.anchorMax = new Vector2(0.96f, 0.92f);
        _sealText.rectTransform.offsetMin = Vector2.zero;
        _sealText.rectTransform.offsetMax = Vector2.zero;

        _characterTabButton = CreateCategoryButton("CharacterTab", "角色", 0.31f);
        _weaponTabButton = CreateCategoryButton("WeaponTab", "武器", 0.45f);
        _upgradeTabButton = CreateCategoryButton("UpgradeTab", "升级项目", 0.59f);

        _contentRoot = CreateRect("CollectionContent", _panelRoot);
        _contentRoot.anchorMin = new Vector2(0.08f, 0.13f);
        _contentRoot.anchorMax = new Vector2(0.92f, 0.78f);
        _contentRoot.offsetMin = Vector2.zero;
        _contentRoot.offsetMax = Vector2.zero;
        GridLayoutGroup grid = _contentRoot.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(720f, 165f);
        grid.spacing = new Vector2(24f, 18f);
        grid.childAlignment = TextAnchor.UpperCenter;

        _backButton = CreateButton(
            "CollectionBackButton",
            _panelRoot,
            "返回",
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 24f),
            new Vector2(220f, 58f));
        _backButton.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
    }

    /// <summary>在编辑器固定骨架中创建一个分类按钮。</summary>
    private Button CreateCategoryButton(
        string objectName,
        string label,
        float anchorX)
    {
        Button button = CreateButton(
            objectName,
            _panelRoot,
            label,
            new Vector2(anchorX, 0.82f),
            new Vector2(anchorX, 0.82f),
            Vector2.zero,
            new Vector2(210f, 58f));
        return button;
    }
#endif

    /// <summary>切换到角色收藏分类。</summary>
    private void ShowCharacters()
    {
        SelectCategory(CollectionCategory.Characters);
    }

    /// <summary>切换到武器收藏分类。</summary>
    private void ShowWeapons()
    {
        SelectCategory(CollectionCategory.Weapons);
    }

    /// <summary>切换到升级项目收藏分类。</summary>
    private void ShowUpgrades()
    {
        SelectCategory(CollectionCategory.Upgrades);
    }

    /// <summary>切换收藏分类并重建少量静态条目。</summary>
    private void SelectCategory(CollectionCategory category)
    {
        _category = category;
        Refresh();
    }

    /// <summary>按当前账号和分类重建收藏条目；当前内容规模最多五项，不进入每帧路径。</summary>
    private void Refresh()
    {
        if (_panelRoot == null || !IsVisible || _contentRoot == null)
        {
            return;
        }

        _goldText.text = $"账号金币  {_accountProgress.Gold}";
        _sealText.text = $"Seal  {_accountProgress.ActiveSealCount} / {_accountProgress.SealCapacity}";
        ClearEntries();
        if (contentCatalog == null)
        {
            CreateMessageEntry("内容目录未配置");
            return;
        }

        switch (_category)
        {
            case CollectionCategory.Characters:
                BuildCharacterEntries();
                break;
            case CollectionCategory.Weapons:
                BuildWeaponEntries();
                break;
            case CollectionCategory.Upgrades:
                BuildUpgradeEntries();
                break;
        }
    }

    /// <summary>生成角色收藏卡；锁定角色使用黑影且不显示具体角色资料。</summary>
    private void BuildCharacterEntries()
    {
        for (int index = 0; index < contentCatalog.Characters.Count; index++)
        {
            CharacterDataSO character = contentCatalog.Characters[index];
            if (character == null)
            {
                continue;
            }

            bool unlocked = _accountProgress.IsCharacterUnlocked(character.characterID);
            string name = unlocked ? character.GetDisplayName() : "未解锁角色";
            string description = unlocked
                ? $"起始武器：{GetStartingWeaponName(character)}\n固有被动：{GetPassiveName(character)}"
                : character.unlock != null ? character.unlock.GetDisplayDescription() : "解锁条件未配置";
            CreateEntry(character.GetSelectionIcon(), unlocked, name, description, null, false);
        }
    }

    /// <summary>生成武器收藏卡；未发现内容只显示问号。</summary>
    private void BuildWeaponEntries()
    {
        for (int index = 0; index < contentCatalog.Weapons.Count; index++)
        {
            WeaponDataSO weapon = contentCatalog.Weapons[index];
            if (weapon == null)
            {
                continue;
            }

            bool discovered = _accountProgress.IsWeaponDiscovered(weapon.weaponID);
            CreateEntry(
                weapon.icon,
                discovered,
                discovered ? weapon.GetDisplayName() : "？？？",
                discovered ? weapon.GetDisplayDescription() : "在一局游戏中实际获得后发现",
                null,
                false);
        }
    }

    /// <summary>生成升级项目收藏卡，并为已发现项目提供 Seal 或解除 Seal 操作。</summary>
    private void BuildUpgradeEntries()
    {
        for (int index = 0; index < contentCatalog.Upgrades.Count; index++)
        {
            UpgradeDataSO upgrade = contentCatalog.Upgrades[index];
            if (upgrade == null)
            {
                continue;
            }

            string upgradeId = upgrade.GetStableId();
            bool discovered = _accountProgress.IsUpgradeDiscovered(upgradeId);
            WeaponDataSO grantedWeapon = upgrade.weaponToGrant;
            CreateEntry(
                grantedWeapon != null ? grantedWeapon.icon : upgrade.icon,
                discovered,
                discovered ? GetUpgradeCollectionName(upgrade) : "？？？",
                discovered ? GetUpgradeCollectionDescription(upgrade) : "在升级候选中出现后发现",
                upgradeId,
                discovered);
        }
    }

    /// <summary>创建一张收藏卡，并按发现状态决定图标黑影和 Seal 控件。</summary>
    private void CreateEntry(
        Sprite icon,
        bool revealed,
        string title,
        string description,
        string upgradeId,
        bool canSeal)
    {
        RectTransform card = CreateRect("CollectionEntry", _contentRoot);
        Image background = card.gameObject.AddComponent<Image>();
        background.color = new Color32(16, 42, 70, 245);
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(62, 205, 238, 255);
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform iconRect = CreateRect("Icon", card);
        iconRect.anchorMin = new Vector2(0.03f, 0.16f);
        iconRect.anchorMax = new Vector2(0.20f, 0.84f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        Image iconImage = iconRect.gameObject.AddComponent<Image>();
        iconImage.sprite = icon;
        iconImage.preserveAspect = true;
        iconImage.color = revealed ? Color.white : Color.black;
        iconImage.enabled = icon != null;

        TMP_Text titleText = CreateText("Title", card, title, 26f, TextAlignmentOptions.MidlineLeft);
        titleText.rectTransform.anchorMin = new Vector2(0.23f, 0.62f);
        titleText.rectTransform.anchorMax = new Vector2(0.96f, 0.94f);
        titleText.rectTransform.offsetMin = Vector2.zero;
        titleText.rectTransform.offsetMax = Vector2.zero;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = new Color32(255, 157, 47, 255);

        TMP_Text descriptionText = CreateText(
            "Description",
            card,
            description,
            19f,
            TextAlignmentOptions.TopLeft);
        descriptionText.rectTransform.anchorMin = new Vector2(0.23f, 0.10f);
        descriptionText.rectTransform.anchorMax = new Vector2(canSeal ? 0.74f : 0.96f, 0.62f);
        descriptionText.rectTransform.offsetMin = Vector2.zero;
        descriptionText.rectTransform.offsetMax = Vector2.zero;
        descriptionText.enableWordWrapping = true;

        if (!canSeal || string.IsNullOrWhiteSpace(upgradeId))
        {
            return;
        }

        bool isSealed = _accountProgress.IsUpgradeSealed(upgradeId);
        Button sealButton = CreateButton(
            "SealButton",
            card,
            isSealed ? "解除 Seal" : "Seal",
            new Vector2(0.85f, 0.28f),
            new Vector2(0.85f, 0.28f),
            Vector2.zero,
            new Vector2(160f, 52f));
        sealButton.interactable = isSealed ||
            _accountProgress.ActiveSealCount < _accountProgress.SealCapacity;
        sealButton.onClick.AddListener(() => ToggleSeal(upgradeId));
    }

    /// <summary>切换指定升级项目的账号级 Seal，并由账号事件统一刷新页面。</summary>
    private void ToggleSeal(string upgradeId)
    {
        bool targetState = !_accountProgress.IsUpgradeSealed(upgradeId);
        _accountProgress.TrySetUpgradeSealed(upgradeId, targetState);
    }

    /// <summary>内容目录缺失时显示一张明确诊断卡。</summary>
    private void CreateMessageEntry(string message)
    {
        CreateEntry(null, true, "收藏不可用", message, null, false);
    }

    /// <summary>清除上一次分类创建的少量卡片。</summary>
    private void ClearEntries()
    {
        for (int index = _contentRoot.childCount - 1; index >= 0; index--)
        {
            GameObject entry = _contentRoot.GetChild(index).gameObject;
            entry.transform.SetParent(null, false);
            if (Application.isPlaying)
            {
                Destroy(entry);
            }
            else
            {
                DestroyImmediate(entry);
            }
        }
    }

    /// <summary>隐藏收藏页并通知主菜单恢复按钮。</summary>
    private void Close()
    {
        Hide();
        Closed?.Invoke();
    }

    /// <summary>安全读取角色起始武器显示名。</summary>
    private static string GetStartingWeaponName(CharacterDataSO character)
    {
        return character != null && character.startingWeapon != null
            ? character.startingWeapon.GetDisplayName()
            : "未配置";
    }

    /// <summary>安全读取角色固有被动显示名。</summary>
    private static string GetPassiveName(CharacterDataSO character)
    {
        return character != null && character.passive != null
            ? character.passive.GetDisplayName()
            : "无固有被动";
    }

    /// <summary>武器型升级在收藏页复用武器权威名称，非武器升级回退到自身词条。</summary>
    private static string GetUpgradeCollectionName(UpgradeDataSO upgrade)
    {
        return upgrade != null && upgrade.weaponToGrant != null
            ? upgrade.weaponToGrant.GetDisplayName()
            : upgrade != null ? upgrade.GetDisplayName() : string.Empty;
    }

    /// <summary>武器型升级在收藏页复用武器权威描述，避免两份基础文案发生标点漂移。</summary>
    private static string GetUpgradeCollectionDescription(UpgradeDataSO upgrade)
    {
        return upgrade != null && upgrade.weaponToGrant != null
            ? upgrade.weaponToGrant.GetDisplayDescription()
            : upgrade != null ? upgrade.GetDisplayDescription() : string.Empty;
    }

    /// <summary>创建统一风格的低频菜单按钮。</summary>
    private Button CreateButton(
        string objectName,
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(objectName, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(25, 71, 108, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(255, 157, 47, 255);
        outline.effectDistance = new Vector2(2f, -2f);
        TMP_Text text = CreateText("Label", rect, label, 22f, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        return button;
    }

    /// <summary>创建使用主菜单 TMP 字体的文本节点。</summary>
    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string content,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
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

    /// <summary>创建继承父级 UI Layer 的 RectTransform 节点。</summary>
    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    /// <summary>把 RectTransform 拉伸到父节点完整范围。</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
