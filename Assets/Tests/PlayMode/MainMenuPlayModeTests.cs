using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证真实主菜单场景的交互焦点、加载锁和进入游戏流程。</summary>
    public sealed class MainMenuPlayModeTests
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string GameplaySceneName = "MainLevel";
        private const float MaxLoadSeconds = 15f;

        /// <summary>开始按钮应先打开角色选择页，确认当前角色后才进入游戏主场景。</summary>
        [UnityTest]
        public IEnumerator StartButton_打开角色选择并确认_携带角色进入游戏场景()
        {
            yield return SceneManager.LoadSceneAsync(MainMenuSceneName, LoadSceneMode.Single);
            yield return null;

            Type controllerType = RuntimeComponentTestUtility.RequireRuntimeType("MainMenuController");
            Component controller = UnityEngine.Object.FindObjectOfType(controllerType) as Component;
            GameObject startButtonObject = GameObject.Find("StartButton");
            GameObject quitButtonObject = GameObject.Find("QuitButton");
            GameObject versionTextObject = GameObject.Find("VersionText");
            Button startButton = startButtonObject != null
                ? startButtonObject.GetComponent<Button>()
                : null;
            Button quitButton = quitButtonObject != null
                ? quitButtonObject.GetComponent<Button>()
                : null;

            bool controllerFound = controller != null;
            bool startButtonFound = startButton != null;
            bool quitButtonFound = quitButton != null;
            bool versionTextFound = versionTextObject != null;
            bool eventSystemFound = EventSystem.current != null;
            bool startButtonSelected = eventSystemFound &&
                                       EventSystem.current.currentSelectedGameObject == startButtonObject;
            string configuredSceneName = controllerFound
                ? RuntimeComponentTestUtility.GetProperty<string>(controller, "GameplaySceneName")
                : string.Empty;

            bool selectionVisible = false;
            bool loadingBeforeConfirm = true;
            bool menuControlsLocked = false;
            bool selectorStructureValid = false;
            bool warriorHoverSelected = false;
            bool portraitAnimationStarted = false;
            bool loadingAfterConfirm = false;
            bool selectionControlsLocked = false;
            bool reachedGameplayScene = false;
            bool selectedCharacterApplied = false;
            float timeScaleAfterSubmit = -1f;

            if (controllerFound && startButtonFound && quitButtonFound)
            {
                Time.timeScale = 0f;
                startButton.onClick.Invoke();

                selectionVisible = RuntimeComponentTestUtility.GetProperty<bool>(
                    controller,
                    "IsCharacterSelectionVisible");
                loadingBeforeConfirm = RuntimeComponentTestUtility.GetProperty<bool>(
                    controller,
                    "IsLoading");
                menuControlsLocked = !startButton.interactable && !quitButton.interactable;
                timeScaleAfterSubmit = Time.timeScale;

                Type selectorType = RuntimeComponentTestUtility.RequireRuntimeType("CharacterSelectionUI");
                Component selector = UnityEngine.Object.FindObjectOfType(selectorType) as Component;
                GameObject selectionPanel = GameObject.Find("CharacterSelectPanel");
                GameObject slotGrid = GameObject.Find("CharacterSlotGrid");
                GameObject warriorSlot = GameObject.Find("CharacterSlot_02");
                GameObject confirmObject = GameObject.Find("CharacterConfirmButton");
                Button confirmButton = confirmObject != null
                    ? confirmObject.GetComponent<Button>()
                    : null;

                selectorStructureValid = selector != null &&
                                         selectionPanel != null &&
                                         selectionPanel.activeInHierarchy &&
                                         slotGrid != null &&
                                         slotGrid.transform.childCount == 12 &&
                                         RuntimeComponentTestUtility.GetProperty<int>(selector, "SlotCount") == 12 &&
                                         RuntimeComponentTestUtility.GetProperty<int>(selector, "AvailableSlotCount") == 2 &&
                                         warriorSlot != null &&
                                         confirmButton != null;

                if (selector != null && warriorSlot != null && EventSystem.current != null)
                {
                    ExecuteEvents.Execute(
                        warriorSlot,
                        new PointerEventData(EventSystem.current),
                        ExecuteEvents.pointerEnterHandler);
                    object hoveredCharacter = RuntimeComponentTestUtility.GetProperty<object>(
                        selector,
                        "SelectedCharacter");
                    string hoveredCharacterId = hoveredCharacter?.GetType()
                        .GetField("characterID")
                        ?.GetValue(hoveredCharacter) as string;
                    string hoveredStats = RuntimeComponentTestUtility.GetProperty<string>(
                        selector,
                        "StatsText");
                    Image leftPortrait = GameObject.Find("LeftCharacterPortrait")?.GetComponent<Image>();
                    warriorHoverSelected = hoveredCharacterId == "character_blue_warrior" &&
                                           hoveredStats.Contains("生命  140") &&
                                           hoveredStats.Contains("力量  125%") &&
                                           hoveredStats.Contains("移动速度  2.6") &&
                                           leftPortrait != null &&
                                           leftPortrait.sprite != null;
                }

                float animationDeadline = Time.realtimeSinceStartup + 0.25f;
                while (selector != null && Time.realtimeSinceStartup < animationDeadline)
                {
                    portraitAnimationStarted = RuntimeComponentTestUtility.GetProperty<float>(
                        selector,
                        "PortraitAnimationProgress") > 0f;
                    if (portraitAnimationStarted)
                    {
                        break;
                    }

                    yield return null;
                }

                if (confirmButton != null)
                {
                    confirmButton.onClick.Invoke();
                    loadingAfterConfirm = RuntimeComponentTestUtility.GetProperty<bool>(
                        controller,
                        "IsLoading");
                    selectionControlsLocked = !confirmButton.interactable;
                }

                float loadDeadline = Time.realtimeSinceStartup + MaxLoadSeconds;
                while (Time.realtimeSinceStartup < loadDeadline)
                {
                    if (SceneManager.GetActiveScene().name == GameplaySceneName)
                    {
                        reachedGameplayScene = true;
                        break;
                    }

                    yield return null;
                }

                if (reachedGameplayScene)
                {
                    Type playerStatsType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStats");
                    Component playerStats = UnityEngine.Object.FindObjectOfType(playerStatsType) as Component;
                    object characterData = playerStats != null
                        ? RuntimeComponentTestUtility.GetProperty<object>(playerStats, "CharacterData")
                        : null;
                    if (characterData != null)
                    {
                        string characterId = characterData.GetType()
                            .GetField("characterID")
                            ?.GetValue(characterData) as string;
                        selectedCharacterApplied = characterId == "character_blue_warrior";
                    }
                }
            }

            string activeSceneName = SceneManager.GetActiveScene().name;

            // 断言前离开正式场景，确保本用例失败时也尽量不污染后续测试。
            Scene cleanupScene = SceneManager.CreateScene("PlayModeTest_MainMenuCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            Scene activeScene = SceneManager.GetSceneByName(activeSceneName);
            if (activeScene.IsValid() && activeScene.isLoaded && activeScene != cleanupScene)
            {
                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(activeScene);
                if (unloadOperation != null)
                {
                    yield return unloadOperation;
                }
            }

            Time.timeScale = 1f;
            Type selectionSessionType = RuntimeComponentTestUtility.RequireRuntimeType(
                "CharacterSelectionSession");
            selectionSessionType.GetMethod("Clear")?.Invoke(null, null);

            Assert.IsTrue(controllerFound, "MainMenu 缺少 MainMenuController。");
            Assert.IsTrue(startButtonFound, "MainMenu 缺少开始按钮。");
            Assert.IsTrue(quitButtonFound, "MainMenu 缺少退出按钮。");
            Assert.IsTrue(versionTextFound, "MainMenu 缺少版本文本。");
            Assert.IsTrue(eventSystemFound, "MainMenu 缺少 EventSystem。");
            Assert.IsTrue(startButtonSelected, "主菜单打开后没有默认选中开始按钮。");
            Assert.That(configuredSceneName, Is.EqualTo(GameplaySceneName));
            Assert.IsTrue(selectionVisible, "开始按钮没有打开角色选择页。");
            Assert.IsFalse(loadingBeforeConfirm, "尚未确认角色时已经开始加载游戏场景。");
            Assert.IsTrue(menuControlsLocked, "角色选择期间主菜单按钮仍可交互。");
            Assert.IsTrue(selectorStructureValid, "角色选择页未生成 12 个槽位和 2 个可用角色。");
            Assert.IsTrue(warriorHoverSelected, "悬停第二槽位后未切换到蓝衣战士及其展示属性／立绘。");
            Assert.IsTrue(portraitAnimationStarted, "角色左右立绘没有开始使用非缩放时间滑入渐显。");
            Assert.That(timeScaleAfterSubmit, Is.EqualTo(1f).Within(0.0001f));
            Assert.IsTrue(loadingAfterConfirm, "确认角色后没有立即建立加载锁。");
            Assert.IsTrue(selectionControlsLocked, "场景加载期间角色选择控件仍可交互。");
            Assert.IsTrue(reachedGameplayScene, "开始按钮未能在等待上限内进入 MainLevel。");
            Assert.IsTrue(selectedCharacterApplied, "MainLevel 的 PlayerStats 未采用菜单确认的角色。");
        }
    }
}
