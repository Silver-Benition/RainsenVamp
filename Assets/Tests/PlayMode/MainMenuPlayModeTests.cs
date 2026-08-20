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
        private const int MaxLoadFrames = 600;

        /// <summary>主菜单应建立完整交互，并在开始按钮提交后只进入一次游戏主场景。</summary>
        [UnityTest]
        public IEnumerator StartButton_主菜单真实加载_恢复时间并进入游戏场景()
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

            bool loadingLockedImmediately = false;
            bool controlsLockedImmediately = false;
            bool reachedGameplayScene = false;
            float timeScaleAfterSubmit = -1f;

            if (controllerFound && startButtonFound && quitButtonFound)
            {
                Time.timeScale = 0f;
                startButton.onClick.Invoke();

                loadingLockedImmediately = RuntimeComponentTestUtility.GetProperty<bool>(
                    controller,
                    "IsLoading");
                controlsLockedImmediately = !startButton.interactable && !quitButton.interactable;
                timeScaleAfterSubmit = Time.timeScale;

                // 再次提交应被加载锁直接忽略，不能创建第二个场景切换请求。
                startButton.onClick.Invoke();

                for (int frame = 0; frame < MaxLoadFrames; frame++)
                {
                    if (SceneManager.GetActiveScene().name == GameplaySceneName)
                    {
                        reachedGameplayScene = true;
                        break;
                    }

                    yield return null;
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

            Assert.IsTrue(controllerFound, "MainMenu 缺少 MainMenuController。");
            Assert.IsTrue(startButtonFound, "MainMenu 缺少开始按钮。");
            Assert.IsTrue(quitButtonFound, "MainMenu 缺少退出按钮。");
            Assert.IsTrue(versionTextFound, "MainMenu 缺少版本文本。");
            Assert.IsTrue(eventSystemFound, "MainMenu 缺少 EventSystem。");
            Assert.IsTrue(startButtonSelected, "主菜单打开后没有默认选中开始按钮。");
            Assert.That(configuredSceneName, Is.EqualTo(GameplaySceneName));
            Assert.IsTrue(loadingLockedImmediately, "开始按钮提交后没有立即建立加载锁。");
            Assert.IsTrue(controlsLockedImmediately, "场景加载期间按钮仍可交互。");
            Assert.That(timeScaleAfterSubmit, Is.EqualTo(1f).Within(0.0001f));
            Assert.IsTrue(reachedGameplayScene, "开始按钮未能在等待上限内进入 MainLevel。");
        }
    }
}
