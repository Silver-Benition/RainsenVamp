using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证装备展示模块在真实 Player Loop 中的构建、归一化和暂停等级表现。</summary>
    public sealed class PlayerLoadoutDisplayPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>手动暂停应展开等级；九级内显示点阵，超过九级显示当前等级数字。</summary>
        [UnityTest]
        public IEnumerator LoadoutDisplay_手动暂停_显示等级并保持十二格布局()
        {
            Sprite firstIcon = CreateTrackedSprite(Color.red);
            Sprite secondIcon = CreateTrackedSprite(Color.cyan);
            ScriptableObject firstWeaponData = CreateWeaponData(
                "playmode_weapon_1",
                firstIcon,
                3,
                1.5f,
                new Vector2(2f, -1f));
            ScriptableObject secondWeaponData = CreateWeaponData(
                "playmode_weapon_2",
                secondIcon,
                12,
                0.75f,
                new Vector2(-3f, 2f));

            GameObject player = CreateTrackedGameObject("PlayModeTest_Player", false);
            player.tag = "Player";
            GameObject defaultWeaponObject = CreateTrackedGameObject("PlayModeTest_DefaultWeapon");
            defaultWeaponObject.transform.SetParent(player.transform, false);
            Component defaultWeapon = RuntimeComponentTestUtility.AddRuntimeComponent(
                defaultWeaponObject,
                "WeaponBase");
            RuntimeComponentTestUtility.SetField(defaultWeapon, "weaponData", firstWeaponData);
            player.SetActive(true);

            GameObject levelUpPanel = CreateTrackedGameObject("PlayModeTest_LevelUpPanel");
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_LevelUpManager", false);
            Component levelUpManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "LevelUpManager");
            RuntimeComponentTestUtility.SetField(levelUpManager, "levelUpPanel", levelUpPanel);
            managerObject.SetActive(true);

            GameObject pausePanel = CreateTrackedGameObject("PlayModeTest_PausePanel");
            GameObject gameFlowObject = CreateTrackedGameObject("PlayModeTest_GameFlowManager", false);
            Component gameFlowManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                gameFlowObject,
                "GameFlowManager");
            RuntimeComponentTestUtility.SetField(gameFlowManager, "pausePanel", pausePanel);
            gameFlowObject.SetActive(true);

            GameObject canvasObject = new GameObject(
                "PlayModeTest_Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            TrackObject(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Component display = RuntimeComponentTestUtility.AddRuntimeComponent(
                canvasObject,
                "PlayerLoadoutDisplayUI");

            yield return null;

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(display, "WeaponSlotCount"),
                Is.EqualTo(6));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(display, "AbilitySlotCount"),
                Is.EqualTo(6));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(display, "DisplayedWeaponCount"),
                Is.EqualTo(1));
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(display, "IsShowingLevels"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(display, "CurrentCellHeight"),
                Is.EqualTo(48f).Within(FloatTolerance));

            Transform panelRoot = canvasObject.transform.Find("PlayerLoadoutDisplay");
            Assert.IsNotNull(panelRoot, "Canvas 下没有创建 PlayerLoadoutDisplay。");
            Assert.That(panelRoot.childCount, Is.EqualTo(12));

            RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
            Assert.That(panelRect.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(panelRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(panelRect.pivot, Is.EqualTo(Vector2.one));
            Assert.That(panelRect.anchoredPosition, Is.EqualTo(new Vector2(-24f, -24f)));

            Image firstWeaponIcon = RequireSlotIcon(panelRoot, "WeaponSlot_1");
            Assert.IsTrue(firstWeaponIcon.enabled);
            Assert.AreSame(firstIcon, firstWeaponIcon.sprite);
            Assert.That(firstWeaponIcon.rectTransform.localScale.x, Is.EqualTo(1.5f).Within(FloatTolerance));
            Assert.That(firstWeaponIcon.rectTransform.localScale.y, Is.EqualTo(1.5f).Within(FloatTolerance));
            Assert.That(firstWeaponIcon.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(2f, -1f)));
            Assert.IsNotNull(firstWeaponIcon.GetComponentInParent<RectMask2D>());

            for (int slotIndex = 1; slotIndex <= 6; slotIndex++)
            {
                Image abilityIcon = RequireSlotIcon(panelRoot, $"AbilitySlot_{slotIndex}");
                Assert.IsFalse(abilityIcon.enabled);
                Assert.IsNull(abilityIcon.sprite);
            }

            object firstWeapon = GrantWeaponToLevel(
                levelUpManager,
                firstWeaponData,
                1,
                2);
            object secondWeapon = GrantWeaponToLevel(
                levelUpManager,
                secondWeaponData,
                0,
                10);
            Assert.IsNotNull(firstWeapon);
            Assert.IsNotNull(secondWeapon);

            RuntimeComponentTestUtility.Invoke(gameFlowManager, "EnterLevelUpPause");
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(display, "IsShowingLevels"));
            RuntimeComponentTestUtility.Invoke(gameFlowManager, "ExitLevelUpPause");

            RuntimeComponentTestUtility.Invoke(gameFlowManager, "PauseGame");

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(display, "IsShowingLevels"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(display, "CurrentCellHeight"),
                Is.EqualTo(84f).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(display, "DisplayedWeaponCount"),
                Is.EqualTo(2));

            Transform firstLevel = RequireLevelRoot(panelRoot, "WeaponSlot_1");
            Transform firstDots = firstLevel.Find("Dots");
            Transform firstNumber = firstLevel.Find("LevelNumber");
            Assert.IsTrue(firstLevel.gameObject.activeSelf);
            Assert.IsNotNull(firstLevel.GetComponent<Image>(), "等级区域缺少外层强调框。");
            Assert.IsNotNull(firstLevel.Find("Background").GetComponent<Image>());
            Assert.That(firstLevel.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(32f).Within(FloatTolerance));
            Assert.IsTrue(firstDots.gameObject.activeSelf);
            GridLayoutGroup firstDotsGrid = firstDots.GetComponent<GridLayoutGroup>();
            Assert.That(firstDotsGrid.cellSize, Is.EqualTo(new Vector2(8f, 8f)));
            Assert.That(firstDotsGrid.spacing, Is.EqualTo(new Vector2(2f, 2f)));
            Assert.IsFalse(firstNumber.gameObject.activeSelf);
            Assert.IsTrue(firstDots.Find("Dot_1").gameObject.activeSelf);
            Assert.IsTrue(firstDots.Find("Dot_2").gameObject.activeSelf);
            Assert.IsTrue(firstDots.Find("Dot_3").gameObject.activeSelf);
            Assert.IsFalse(firstDots.Find("Dot_4").gameObject.activeSelf);
            Assert.IsNotNull(firstDots.Find("Dot_1").GetComponent<Image>(), "等级点缺少独立边框。");
            Color activeDotColor = firstDots.Find("Dot_1/Fill").GetComponent<Image>().color;
            Color secondActiveDotColor = firstDots.Find("Dot_2/Fill").GetComponent<Image>().color;
            Color inactiveDotColor = firstDots.Find("Dot_3/Fill").GetComponent<Image>().color;
            Assert.That(secondActiveDotColor, Is.EqualTo(activeDotColor));
            Assert.That(inactiveDotColor, Is.Not.EqualTo(activeDotColor));

            Transform secondLevel = RequireLevelRoot(panelRoot, "WeaponSlot_2");
            Transform secondDots = secondLevel.Find("Dots");
            Text secondNumber = secondLevel.Find("LevelNumber").GetComponent<Text>();
            Assert.IsTrue(secondLevel.gameObject.activeSelf);
            Assert.IsNotNull(secondLevel.GetComponent<Image>(), "数字等级缺少外层强调框。");
            Assert.IsFalse(secondDots.gameObject.activeSelf);
            Assert.IsTrue(secondNumber.gameObject.activeSelf);
            Assert.That(secondNumber.text, Is.EqualTo("10"));
            Assert.That(secondNumber.fontSize, Is.EqualTo(24));

            Transform firstAbilityLevel = RequireLevelRoot(panelRoot, "AbilitySlot_1");
            Assert.IsFalse(firstAbilityLevel.gameObject.activeSelf);

            RuntimeComponentTestUtility.Invoke(gameFlowManager, "ResumeGame");

            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(display, "IsShowingLevels"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(display, "CurrentCellHeight"),
                Is.EqualTo(48f).Within(FloatTolerance));
            Assert.IsFalse(firstLevel.gameObject.activeSelf);
            Assert.IsFalse(secondLevel.gameObject.activeSelf);
        }

        /// <summary>
        /// 逐级调用正式授予入口，使每一级都通过生产代码发布武器变化事件。
        /// currentLevel 为零表示尚未获得；返回最终运行时武器组件。
        /// </summary>
        private static object GrantWeaponToLevel(
            Component levelUpManager,
            ScriptableObject weaponData,
            int currentLevel,
            int targetLevel)
        {
            object weapon = null;
            for (int level = currentLevel; level < targetLevel; level++)
            {
                weapon = RuntimeComponentTestUtility.Invoke(
                    levelUpManager,
                    "GrantOrUpgradeWeapon",
                    weaponData);
            }

            return weapon;
        }

        /// <summary>创建带唯一 ID、等级数量与 HUD 变换配置的真实 WeaponDataSO。</summary>
        private ScriptableObject CreateWeaponData(
            string weaponId,
            Sprite icon,
            int maxLevel,
            float iconScale,
            Vector2 iconOffset)
        {
            ScriptableObject weaponData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("WeaponDataSO"));
            RuntimeComponentTestUtility.SetField(weaponData, "weaponID", weaponId);
            RuntimeComponentTestUtility.SetField(weaponData, "weaponNameKey", weaponId + ".name");
            RuntimeComponentTestUtility.SetField(weaponData, "icon", icon);
            RuntimeComponentTestUtility.SetField(weaponData, "loadoutIconScale", iconScale);
            RuntimeComponentTestUtility.SetField(weaponData, "loadoutIconOffset", iconOffset);

            Type levelDataType = RuntimeComponentTestUtility.RequireRuntimeType("WeaponLevelData");
            Type levelListType = typeof(List<>).MakeGenericType(levelDataType);
            IList levelConfigs = (IList)Activator.CreateInstance(levelListType);
            for (int level = 0; level < maxLevel; level++)
            {
                levelConfigs.Add(Activator.CreateInstance(levelDataType));
            }
            RuntimeComponentTestUtility.SetField(weaponData, "levelConfigs", levelConfigs);
            return weaponData;
        }

        /// <summary>创建可辨识颜色的最小运行时 Sprite，并纳入测试清理。</summary>
        private Sprite CreateTrackedSprite(Color color)
        {
            Texture2D texture = TrackObject(new Texture2D(2, 2));
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return TrackObject(Sprite.Create(
                texture,
                new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f),
                2f));
        }

        /// <summary>取得指定槽位裁切视口中的 Icon 组件。</summary>
        private static Image RequireSlotIcon(Transform panelRoot, string slotName)
        {
            Transform iconTransform = panelRoot.Find($"{slotName}/IconFrame/IconViewport/Icon");
            Assert.IsNotNull(iconTransform, $"槽位 {slotName} 缺少 Icon 子对象。");
            Image iconImage = iconTransform.GetComponent<Image>();
            Assert.IsNotNull(iconImage, $"槽位 {slotName} 的 Icon 缺少 Image 组件。");
            return iconImage;
        }

        /// <summary>取得指定槽位的暂停等级根对象。</summary>
        private static Transform RequireLevelRoot(Transform panelRoot, string slotName)
        {
            Transform levelRoot = panelRoot.Find($"{slotName}/Level");
            Assert.IsNotNull(levelRoot, $"槽位 {slotName} 缺少 Level 子对象。");
            return levelRoot;
        }
    }
}
