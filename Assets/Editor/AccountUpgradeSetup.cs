using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>定向建立 Session 22 资产与序列化页面；仅编辑 MainMenu/MainLevel 的商店和成长引用。</summary>
public static class AccountUpgradeSetup
{
    public const string CatalogPath = "Assets/Data/AccountUpgrades/AccountUpgradeCatalog.asset";
    private static TMP_FontAsset _font;

    /// <summary>一次性生成缺失配置与页面，已有数值资产不覆盖，避免重跑丢失策划调整。</summary>
    [MenuItem("RainsenVampSur/Account/Build Session 22 Shop")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data/AccountUpgrades")) AssetDatabase.CreateFolder("Assets/Data", "AccountUpgrades");
        if (!AssetDatabase.IsValidFolder("Assets/Prefab/UI")) AssetDatabase.CreateFolder("Assets/Prefab", "UI");
        AccountUpgradeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AccountUpgradeCatalogSO>();
            foreach (PlayerStatType stat in Enum.GetValues(typeof(PlayerStatType)))
            {
                var definition = ScriptableObject.CreateInstance<AccountUpgradeDataSO>();
                definition.stableId = "account_" + stat.ToString().ToLowerInvariant();
                definition.statType = stat;
                definition.nameKey = "account.upgrade." + stat + ".name";
                definition.descriptionKey = "account.upgrade." + stat + ".description";
                definition.fallbackName = PlayerStatPresentation.GetDisplayName(stat);
                definition.fallbackDescription = "所有角色开局获得此项加成。可逐级购买或退款。";
                definition.maxLevel = 3;
                for (int level = 1; level <= 3; level++)
                {
                    PlayerStatModifierMode mode = PlayerStatModifierMode.Flat;
                    float value = level;
                    switch (stat)
                    {
                        case PlayerStatType.MaxHealth: value = level * 10; break;
                        case PlayerStatType.Recovery: value = level * 0.1f; break;
                        case PlayerStatType.MoveSpeed:
                        case PlayerStatType.Magnet: value = level * 0.2f; break;
                        case PlayerStatType.Defang: value = level * 0.01f; break;
                        case PlayerStatType.Cooldown: mode = PlayerStatModifierMode.Multiplicative; value = 1f - level * 0.05f; break;
                        case PlayerStatType.Might:
                        case PlayerStatType.Area:
                        case PlayerStatType.ProjectileSpeed:
                        case PlayerStatType.Duration:
                        case PlayerStatType.Luck:
                        case PlayerStatType.Growth:
                        case PlayerStatType.Greed:
                        case PlayerStatType.Curse: mode = PlayerStatModifierMode.AdditivePercent; value = level * 0.05f; break;
                    }
                    definition.levels.Add(new AccountUpgradeLevel { cost = level * 100,
                        modifiers = new List<PlayerStatModifier> { new PlayerStatModifier(stat, mode, value) } });
                }
                AssetDatabase.CreateAsset(definition, "Assets/Data/AccountUpgrades/" + stat + ".asset");
                catalog.upgrades.Add(definition);
            }
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        if (!catalog.Validate(out string error)) throw new InvalidOperationException(error);
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/msyh SDF.asset");
        if (_font == null) throw new InvalidOperationException("缺少中文字体");
        var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        MainMenuController controller = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
        AccountShopUI shop = UnityEngine.Object.FindObjectOfType<AccountShopUI>(true);
        if (shop == null)
        {
            Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
            shop = canvas.gameObject.AddComponent<AccountShopUI>();
            shop.Catalog = catalog;
            shop.ContentCatalog = AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>("Assets/Data/GameContentCatalog.asset");
            RectTransform panel = Rect("AccountShopPanel", canvas.transform, 0, 0, 1, 1);
            panel.gameObject.AddComponent<Image>().color = new Color32(5, 16, 32, 255);
            shop.Panel = panel.gameObject;
            Text("ShopTitle", panel, "商店", 0.06f, 0.9f, 0.5f, 0.98f, 46);
            shop.GoldText = Text("ShopGold", panel, "金币", 0.65f, 0.9f, 0.94f, 0.98f, 28);
            shop.BasicTab = Button("ShopBasicTab", panel, "基础属性", 0.06f, 0.8f, 0.32f, 0.88f);
            shop.AdvancedTab = Button("ShopAdvancedTab", panel, "进阶属性", 0.37f, 0.8f, 0.63f, 0.88f);
            shop.ExclusionTab = Button("ShopExclusionTab", panel, "道具排除", 0.68f, 0.8f, 0.94f, 0.88f);
            RectTransform scrollRoot = Rect("ShopScroll", panel, 0.06f, 0.17f, 0.43f, 0.76f);
            scrollRoot.gameObject.AddComponent<Image>().color = new Color32(11, 29, 49, 255);
            shop.Scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Rect("Viewport", scrollRoot, 0, 0, 1, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect("Content", viewport, 0, 1, 1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            shop.Scroll.viewport = viewport;
            shop.Scroll.content = content;
            shop.Scroll.horizontal = false;
            shop.Scroll.movementType = ScrollRect.MovementType.Clamped;
            shop.Scroll.scrollSensitivity = 35;
            shop.DetailText = Text("ShopDetails", panel, "选择项目", 0.49f, 0.31f, 0.94f, 0.76f, 28);
            shop.DetailText.alignment = TextAlignmentOptions.TopLeft;
            shop.BuyButton = Button("ShopBuy", panel, "购买一级", 0.49f, 0.21f, 0.7f, 0.29f);
            shop.RefundButton = Button("ShopRefund", panel, "退最高一级", 0.73f, 0.21f, 0.94f, 0.29f);
            shop.BuyLabel = shop.BuyButton.GetComponentInChildren<TMP_Text>();
            shop.RefundLabel = shop.RefundButton.GetComponentInChildren<TMP_Text>();
            shop.StatusText = Text("ShopStatus", panel, "", 0.49f, 0.1f, 0.94f, 0.19f, 24);
            shop.BackButton = Button("ShopBack", panel, "返回", 0.06f, 0.04f, 0.27f, 0.12f);
            Button templateButton = Button("AccountShopEntry", panel, "属性", 0, 0, 1, 1);
            LayoutElement element = templateButton.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 66;
            AccountShopEntryUI entry = templateButton.gameObject.AddComponent<AccountShopEntryUI>();
            entry.Button = templateButton;
            entry.Label = templateButton.GetComponentInChildren<TMP_Text>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(entry.gameObject, "Assets/Prefab/UI/AccountShopEntry.prefab");
            shop.EntryTemplate = prefab.GetComponent<AccountShopEntryUI>();
            UnityEngine.Object.DestroyImmediate(entry.gameObject);
            panel.gameObject.SetActive(false);
            SerializedObject serialized = new SerializedObject(controller);
            Button collection = (Button)serialized.FindProperty("collectionButton").objectReferenceValue;
            Button entryButton = UnityEngine.Object.Instantiate(collection, collection.transform.parent);
            entryButton.name = "ShopButton";
            entryButton.transform.SetSiblingIndex(collection.transform.GetSiblingIndex() + 1);
            entryButton.GetComponentInChildren<TMP_Text>().text = "商店";
            RectTransform group = (RectTransform)collection.transform.parent;
            group.sizeDelta = new Vector2(group.sizeDelta.x, 360);
            VerticalLayoutGroup menuLayout = group.GetComponent<VerticalLayoutGroup>();
            menuLayout.spacing = 12;
            foreach (Button button in group.GetComponentsInChildren<Button>())
            {
                LayoutElement le = button.GetComponent<LayoutElement>();
                if (le != null) { le.preferredHeight = 70; le.minHeight = 60; }
            }
            serialized.FindProperty("shopButton").objectReferenceValue = entryButton;
            serialized.FindProperty("shopUI").objectReferenceValue = shop;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        ConfigureMainMenuNavigation(controller);
        EditorSceneManager.SaveScene(menu);
        var main = EditorSceneManager.OpenScene("Assets/Scenes/MainLevel.unity");
        foreach (PlayerStats stats in UnityEngine.Object.FindObjectsOfType<PlayerStats>(true))
        {
            SerializedObject serialized = new SerializedObject(stats);
            serialized.FindProperty("accountUpgradeCatalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.SaveScene(main);
        AssetDatabase.SaveAssets();
        Debug.Log("Session 22 商店与 21 项配置已生成并验证。");
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
