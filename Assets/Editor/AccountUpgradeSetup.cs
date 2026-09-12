using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>定向重建 MainMenu 商店布局和模板；保留既有价格、等级、ID、存档及 MainLevel 接线。</summary>
public static class AccountUpgradeSetup
{
    public const string CatalogPath = "Assets/Data/AccountUpgrades/AccountUpgradeCatalog.asset";
    private static TMP_FontAsset _font;

    /// <summary>应用已批准的两项百分比转换与缺失图标，重建商店区域并保存明确引用。</summary>
    [MenuItem("RainsenVampSur/Account/Build Session 22 Shop")]
    public static void Build()
    {
        AccountUpgradeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("现有成长目录缺失，不自动重建价格和等级。");
        ApplyApprovedPercentUpgrade(catalog.Find("account_movespeed"));
        ApplyApprovedPercentUpgrade(catalog.Find("account_magnet"));
        AssignMissingIcons(catalog);
        if (!catalog.Validate(out string error)) throw new InvalidOperationException(error);
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/msyh SDF.asset");
        var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        MainMenuController controller = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
        AccountShopUI shop = UnityEngine.Object.FindObjectOfType<AccountShopUI>(true);
        if (shop == null) throw new InvalidOperationException("现有商店控制器缺失。");
        if (shop.Panel != null) UnityEngine.Object.DestroyImmediate(shop.Panel);
        Canvas canvas = shop.GetComponent<Canvas>();
        shop.Catalog = catalog;
        shop.ContentCatalog = AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>("Assets/Data/GameContentCatalog.asset");
        RectTransform panel = Box("AccountShopPanel", canvas.transform, 0, 0, 1, 1, new Color32(10, 23, 34, 255));
        shop.Panel = panel.gameObject;
        Box("ShopBackgroundRegion", panel, 0.02f, 0.08f, 0.345f, 0.88f, new Color32(21, 46, 57, 255));
        RectTransform title = Box("ShopTitleBar", panel, 0, 0.9f, 1, 1, new Color32(15, 34, 47, 255));
        Text("ShopTitle", title, "商店", 0.025f, 0.05f, 0.8f, 0.95f, 46);
        RectTransform tabs = Box("ShopTabs", panel, 0.37f, 0.43f, 0.485f, 0.80f, new Color32(17, 34, 45, 255));
        shop.BasicTab = Button("ShopBasicTab", tabs, "基础属性", 0.035f, 0.68f, 0.965f, 0.98f);
        shop.AdvancedTab = Button("ShopAdvancedTab", tabs, "进阶属性", 0.035f, 0.35f, 0.965f, 0.65f);
        shop.ExclusionTab = Button("ShopExclusionTab", tabs, "道具排除", 0.035f, 0.02f, 0.965f, 0.32f);
        foreach (Button tab in new[] { shop.BasicTab, shop.AdvancedTab, shop.ExclusionTab })
        {
            RectTransform marker = Box("CurrentMarker", tab.transform, 0, 0, 0.025f, 1, new Color32(255, 209, 64, 255));
            marker.GetComponent<Image>().raycastTarget = false;
        }
        RectTransform balance = Box("ShopBalanceFrame", panel, 0.78f, 0.82f, 0.975f, 0.885f, new Color32(27, 41, 55, 255));
        Border(balance, new Color32(176, 202, 210, 255));
        shop.GoldIcon = Picture("ShopCoin", balance, catalog.goldIcon, 0.025f, 0.12f, 0.22f, 0.88f);
        shop.GoldText = Text("ShopGold", balance, "0", 0.24f, 0.04f, 0.95f, 0.96f, 33);
        shop.GoldText.alignment = TextAlignmentOptions.MidlineRight;
        shop.CapacityText = Text("ShopCapacity", panel, "", 0.495f, 0.82f, 0.775f, 0.88f, 24);
        RectTransform scrollRoot = Box("ShopScroll", panel, 0.495f, 0.335f, 0.975f, 0.80f, new Color32(13, 28, 40, 255));
        shop.Scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = Rect("Viewport", scrollRoot, 0, 0, 0.972f, 1);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("Content", viewport, 0, 1, 1, 1);
        content.pivot = new Vector2(0.5f, 1);
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.cellSize = new Vector2(280, 104);
        grid.spacing = new Vector2(14, 14);
        grid.padding = new RectOffset(10, 10, 10, 10);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        shop.Scroll.content = content;
        shop.Scroll.viewport = viewport;
        shop.Scroll.horizontal = false;
        shop.Scroll.movementType = ScrollRect.MovementType.Clamped;
        shop.Scroll.scrollSensitivity = 40;
        RectTransform track = Box("ShopScrollbar", scrollRoot, 0.98f, 0, 1, 1, new Color32(40, 56, 69, 255));
        Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
        RectTransform handle = Box("Handle", track, 0, 0, 1, 1, new Color32(62, 209, 222, 255));
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        shop.Scroll.verticalScrollbar = scrollbar;
        shop.Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        Text("ShopScrollHint", panel, "滚动查看更多", 0.78f, 0.303f, 0.975f, 0.33f, 18).alignment = TextAlignmentOptions.MidlineRight;
        RectTransform details = Box("ShopDescription", panel, 0.37f, 0.075f, 0.975f, 0.29f, new Color32(20, 42, 56, 255));
        Border(details, new Color32(48, 198, 221, 255));
        shop.DetailName = Text("ShopDetailName", details, "选择项目", 0.02f, 0.78f, 0.95f, 0.99f, 28);
        RectTransform detailFrame = Box("ShopDetailIconFrame", details, 0.02f, 0.20f, 0.105f, 0.66f, new Color32(13, 26, 36, 255));
        Border(detailFrame, new Color32(180, 203, 210, 255));
        shop.DetailIcon = Picture("ShopDetailIcon", detailFrame, null, 0.08f, 0.08f, 0.92f, 0.92f);
        shop.DetailText = Text("ShopDetails", details, "", 0.13f, 0.34f, 0.97f, 0.76f, 22);
        shop.DetailText.alignment = TextAlignmentOptions.TopLeft;
        shop.StatusText = Text("ShopStatus", details, "", 0.13f, 0.04f, 0.64f, 0.30f, 19);
        shop.StatusText.alignment = TextAlignmentOptions.TopLeft;
        shop.BuyButton = Button("ShopBuy", details, "购买一级", 0.66f, 0.05f, 0.81f, 0.29f);
        shop.RefundButton = Button("ShopRefund", details, "退最高一级", 0.825f, 0.05f, 0.98f, 0.29f);
        shop.BuyLabel = shop.BuyButton.GetComponentInChildren<TMP_Text>();
        shop.RefundLabel = shop.RefundButton.GetComponentInChildren<TMP_Text>();
        shop.BuyLabel.fontSize = shop.RefundLabel.fontSize = 21;
        shop.InputHints = Text("ShopInputHints", panel, "确认：选择项目    取消：返回菜单", 0.53f, 0.02f, 0.975f, 0.06f, 21);
        shop.InputHints.alignment = TextAlignmentOptions.MidlineRight;
        shop.BackButton = Button("ShopBack", panel, "返回", 0.37f, 0.015f, 0.47f, 0.06f);
        foreach (Button control in new[] { shop.BasicTab, shop.AdvancedTab, shop.ExclusionTab, shop.BuyButton, shop.RefundButton, shop.BackButton })
            control.gameObject.AddComponent<AccountShopCancelRelay>().Bind(shop);
        shop.EntryTemplate = BuildEntryTemplate(panel);
        shop.BuyButton.gameObject.SetActive(false);
        shop.RefundButton.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        EditorUtility.SetDirty(shop);
        ConfigureMainMenuNavigation(controller);
        EditorSceneManager.SaveScene(menu);
        AssetDatabase.SaveAssets();
        Debug.Log("Session 22 卡片商店重建完成；价格、等级和 MainLevel 保持不变。");
    }

    /// <summary>仅转换已确认的旧三级 Flat 移速/磁吸；已转换或自定义数值不被重跑覆盖，价格/maxLevel 从不改动。</summary>
    public static void ApplyApprovedPercentUpgrade(AccountUpgradeDataSO definition)
    {
        if (definition == null || (definition.statType != PlayerStatType.MoveSpeed && definition.statType != PlayerStatType.Magnet)) return;
        if (definition.levels.Count < 3) return;
        for (int i = 0; i < 3; i++)
        {
            var modifiers = definition.levels[i].modifiers;
            if (modifiers.Count != 1 || modifiers[0].Mode != PlayerStatModifierMode.Flat ||
                !Mathf.Approximately(modifiers[0].Value, 0.2f * (i + 1))) return;
        }
        for (int i = 0; i < 3; i++) definition.levels[i].modifiers[0] =
            new PlayerStatModifier(definition.statType, PlayerStatModifierMode.AdditivePercent, 0.05f * (i + 1));
        EditorUtility.SetDirty(definition);
    }

    /// <summary>只给缺失图标填充项目现有 Point Sprite，已替换的图标保留，不修改纹理导入器。</summary>
    private static void AssignMissingIcons(AccountUpgradeCatalogSO catalog)
    {
        string[] names = { "AdversityInstinct", "RetaliationPulse", "SprintTraining", "StrengthTraining", "MagneticCore", "CooldownOptimization" };
        var icons = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) icons[i] = LoadSprite("Assets/Art/Sprites/Ability/Icons/" + names[i] + ".png");
        foreach (AccountUpgradeDataSO definition in catalog.upgrades)
        {
            if (definition.icon != null) continue;
            int i = (int)definition.statType % icons.Length;
            if (definition.statType == PlayerStatType.MoveSpeed) i = 2;
            if (definition.statType == PlayerStatType.Magnet) i = 4;
            if (definition.statType == PlayerStatType.Cooldown) i = 5;
            definition.icon = icons[i];
            EditorUtility.SetDirty(definition);
        }
        if (catalog.goldIcon == null) catalog.goldIcon = LoadSprite("Assets/Art/Sprites/Other/Coin/Coin.png");
        if (catalog.sealSlotIcon == null) catalog.sealSlotIcon = icons[4];
        while (catalog.advancedIcons.Count < 4) catalog.advancedIcons.Add(null);
        for (int i = 0; i < 4; i++) if (catalog.advancedIcons[i] == null) catalog.advancedIcons[i] = icons[(i + 1) % icons.Length];
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>兼容单 Sprite 与已切片资源，读取现有第一张 Sprite，不修改美术资源。</summary>
    private static Sprite LoadSprite(string path)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path)) if (asset is Sprite sprite) return sprite;
        throw new InvalidOperationException("缺少可复用 Sprite：" + path);
    }

    /// <summary>建立图标与信息框的横向组合模板；等级格小模板也保存为可检查子节点。</summary>
    private static AccountShopEntryUI BuildEntryTemplate(Transform parent)
    {
        RectTransform root = Box("AccountShopEntry", parent, 0, 1, 0, 1, new Color32(14, 26, 37, 255));
        root.sizeDelta = new Vector2(280, 104);
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        button.transition = Selectable.Transition.None;
        Outline highlight = Border(root, new Color32(36, 219, 242, 255));
        highlight.effectDistance = new Vector2(4, -4);
        highlight.enabled = false;
        RectTransform iconSlot = Rect("IconSlot", root, 0.02f, 0.07f, 0.35f, 0.93f);
        RectTransform frame = Box("IconFrame", iconSlot, 0, 0, 1, 1, new Color32(12, 24, 32, 255));
        AspectRatioFitter aspect = frame.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1;
        Border(frame, new Color32(194, 209, 211, 255));
        Image icon = Picture("Icon", frame, null, 0.08f, 0.08f, 0.92f, 0.92f);
        RectTransform info = Box("InformationFrame", root, 0.38f, 0.07f, 0.98f, 0.93f, new Color32(27, 40, 53, 255));
        RectTransform levels = Rect("LevelCells", info, 0.08f, 0.47f, 0.92f, 0.91f);
        GridLayoutGroup grid = levels.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.cellSize = new Vector2(18, 14);
        grid.spacing = new Vector2(4, 4);
        Image pip = Box("LevelCellTemplate", levels, 0, 0, 0, 0, new Color32(89, 101, 113, 255)).GetComponent<Image>();
        pip.raycastTarget = false;
        pip.gameObject.SetActive(false);
        TMP_Text price = Text("Price", info, "100", 0.08f, 0.04f, 0.94f, 0.45f, 27);
        price.color = new Color32(255, 221, 60, 255);
        root.gameObject.AddComponent<AccountShopCancelRelay>();
        AccountShopEntryUI entry = root.gameObject.AddComponent<AccountShopEntryUI>();
        entry.Author(button, price, icon, highlight, levels, pip);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Prefab/UI/AccountShopEntry.prefab");
        UnityEngine.Object.DestroyImmediate(root.gameObject);
        return prefab.GetComponent<AccountShopEntryUI>();
    }

    /// <summary>创建有背景色的矩形区域。</summary>
    private static RectTransform Box(string name, Transform parent, float x0, float y0, float x1, float y1, Color color)
    {
        RectTransform rect = Rect(name, parent, x0, y0, x1, y1);
        rect.gameObject.AddComponent<Image>().color = color;
        return rect;
    }
    /// <summary>创建保持图像比例的占位图标。</summary>
    private static Image Picture(string name, Transform parent, Sprite sprite, float x0, float y0, float x1, float y1)
    {
        Image image = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }
    /// <summary>建立像素风矩形边框，不额外引入美术资源。</summary>
    private static Outline Border(RectTransform rect, Color color)
    {
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(2, -2);
        return outline;
    }

    /// <summary>按视觉顺序接好四个主菜单按钮的上下循环；重跑作者工具也会修复已有页面的旧导航。</summary>
    public static void ConfigureMainMenuNavigation(MainMenuController controller)
    {
        var serialized = new SerializedObject(controller);
        string[] fields = { "startButton", "collectionButton", "shopButton", "quitButton" };
        var buttons = new Button[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            buttons[i] = serialized.FindProperty(fields[i]).objectReferenceValue as Button;
            if (buttons[i] == null) throw new InvalidOperationException("主菜单导航缺少按钮：" + fields[i]);
        }
        // 不继承克隆按钮的显式引用：上下邻居必须对应最终四按钮顺序。
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = buttons[(i + buttons.Length - 1) % buttons.Length],
                selectOnDown = buttons[(i + 1) % buttons.Length]
            };
            EditorUtility.SetDirty(buttons[i]);
        }
    }

    /// <summary>建立归一化锚点布局，继承 UI Layer，保持多分辨率缩放。</summary>
    private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x0, y0);
        rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    /// <summary>建立中文 TMP 标签，不拦截鼠标射线。</summary>
    private static TMP_Text Text(string name, Transform parent, string text, float x0, float y0, float x1, float y1, float size)
    {
        TMP_Text label = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        return label;
    }

    /// <summary>建立沿用主菜单蓝底橙边风格的导航按钮。</summary>
    private static Button Button(string name, Transform parent, string title, float x0, float y0, float x1, float y1)
    {
        RectTransform rect = Rect(name, parent, x0, y0, x1, y1);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(25, 71, 108, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.selectedColor = new Color(1f, 0.75f, 0.35f);
        colors.highlightedColor = new Color(1f, 0.85f, 0.55f);
        button.colors = colors;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(255, 157, 47, 255);
        TMP_Text text = Text("Label", rect, title, 0.03f, 0.05f, 0.97f, 0.95f, 27);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
