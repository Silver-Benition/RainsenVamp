using System.Collections;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证 Build Settings 中真实主场景的加载与重新开始状态清理。</summary>
    public sealed class SceneReloadPlayModeTests : PlayModeComponentTestBase
    {
        private const string MainSceneName = "MainLevel";
        private const float FloatTolerance = 0.0001f;

        /// <summary>暂停后重新开始应重载 MainLevel，并由新管理器恢复干净的时间与流程状态。</summary>
        [UnityTest]
        public IEnumerator RestartGame_真实主场景重载_时间与流程状态无残留()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Type managerType = RuntimeComponentTestUtility.RequireRuntimeType("GameFlowManager");
            Component originalManager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
            bool originalManagerFound = originalManager != null;
            int originalInstanceId = originalManagerFound ? originalManager.GetInstanceID() : 0;
            bool pausedBeforeRestart = false;

            if (originalManagerFound)
            {
                RuntimeComponentTestUtility.Invoke(originalManager, "PauseGame");
                pausedBeforeRestart =
                    RuntimeComponentTestUtility.GetProperty<bool>(originalManager, "IsPaused") &&
                    Mathf.Approximately(Time.timeScale, 0f);
                RuntimeComponentTestUtility.Invoke(originalManager, "RestartGame");
                yield return null;
            }

            Component reloadedManager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
            bool reloadedManagerFound = reloadedManager != null;
            int reloadedInstanceId = reloadedManagerFound ? reloadedManager.GetInstanceID() : 0;
            string activeSceneName = SceneManager.GetActiveScene().name;
            float timeScaleAfterRestart = Time.timeScale;
            bool isPausedAfterRestart = reloadedManagerFound &&
                                        RuntimeComponentTestUtility.GetProperty<bool>(
                                            reloadedManager,
                                            "IsPaused");
            bool isGameOverAfterRestart = reloadedManagerFound &&
                                          RuntimeComponentTestUtility.GetProperty<bool>(
                                              reloadedManager,
                                              "IsGameOver");

            // 在断言前恢复空测试场景，保证即使用例失败也不会污染后续 PlayMode 测试。
            Scene cleanupScene = SceneManager.CreateScene("PlayModeTest_CleanupScene");
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(MainSceneName);
            if (unloadOperation != null)
            {
                yield return unloadOperation;
            }

            Assert.IsTrue(originalManagerFound, "MainLevel 缺少 GameFlowManager。");
            Assert.IsTrue(pausedBeforeRestart, "主场景管理器没有进入真实暂停状态。");
            Assert.IsTrue(reloadedManagerFound, "RestartGame 后没有重建 GameFlowManager。");
            Assert.That(activeSceneName, Is.EqualTo(MainSceneName));
            Assert.That(reloadedInstanceId, Is.Not.EqualTo(originalInstanceId));
            Assert.That(timeScaleAfterRestart, Is.EqualTo(1f).Within(FloatTolerance));
            Assert.IsFalse(isPausedAfterRestart);
            Assert.IsFalse(isGameOverAfterRestart);
        }

        /// <summary>主场景 HUD 应显示全宽经验条、可读文本、避让后的装备栏和底部计时器。</summary>
        [UnityTest]
        public IEnumerator MainLevelHud_经验条居中且调试信息移除()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            GameObject expBarObject = GameObject.Find("ExpBarContainer");
            GameObject frameObject = GameObject.Find("ExpBarFrame");
            GameObject trackObject = GameObject.Find("ExpBarTrack");
            GameObject fillObject = GameObject.Find("FillBar");
            GameObject levelTextObject = GameObject.Find("LevelText");
            GameObject expTextObject = GameObject.Find("ExpText");
            GameObject runStatsObject = GameObject.Find("RunStatsDisplay");
            GameObject killCounterObject = GameObject.Find("KillCounter");
            GameObject goldCounterObject = GameObject.Find("GoldCounter");
            GameObject killCountTextObject = GameObject.Find("KillCountText");
            GameObject goldCountTextObject = GameObject.Find("GoldCountText");
            GameObject coinIconObject = GameObject.Find("CoinIcon");
            GameObject skullIconObject = GameObject.Find("SkullIcon");
            RectTransform expBarRect = expBarObject != null
                ? expBarObject.GetComponent<RectTransform>()
                : null;
            RectTransform canvasRect = expBarRect != null
                ? expBarRect.parent as RectTransform
                : null;
            RectTransform levelRect = levelTextObject != null
                ? levelTextObject.GetComponent<RectTransform>()
                : null;
            RectTransform frameRect = frameObject != null
                ? frameObject.GetComponent<RectTransform>()
                : null;
            RectTransform trackRect = trackObject != null
                ? trackObject.GetComponent<RectTransform>()
                : null;
            RectTransform fillRect = fillObject != null
                ? fillObject.GetComponent<RectTransform>()
                : null;
            RectTransform expTextRect = expTextObject != null
                ? expTextObject.GetComponent<RectTransform>()
                : null;
            RectTransform runStatsRect = runStatsObject != null
                ? runStatsObject.GetComponent<RectTransform>()
                : null;
            RectTransform killCounterRect = killCounterObject != null
                ? killCounterObject.GetComponent<RectTransform>()
                : null;
            RectTransform goldCounterRect = goldCounterObject != null
                ? goldCounterObject.GetComponent<RectTransform>()
                : null;
            Component expBackground = expBarObject != null
                ? expBarObject.GetComponent("Image")
                : null;
            Component frameImage = frameObject != null
                ? frameObject.GetComponent("Image")
                : null;
            Component trackImage = trackObject != null
                ? trackObject.GetComponent("Image")
                : null;
            Component fillImage = fillObject != null
                ? fillObject.GetComponent("Image")
                : null;
            Component expText = expTextObject != null
                ? expTextObject.GetComponent("TextMeshProUGUI")
                : null;
            Component runStats = runStatsObject != null
                ? runStatsObject.GetComponent("RunStatsUI")
                : null;
            Component killCountText = killCountTextObject != null
                ? killCountTextObject.GetComponent("TextMeshProUGUI")
                : null;
            Component goldCountText = goldCountTextObject != null
                ? goldCountTextObject.GetComponent("TextMeshProUGUI")
                : null;
            Component coinIcon = coinIconObject != null
                ? coinIconObject.GetComponent("Image")
                : null;
            Component skullIcon = skullIconObject != null
                ? skullIconObject.GetComponent("Image")
                : null;
            GameObject timerObject = GameObject.Find("GameTimer");
            GameObject timerTextObject = GameObject.Find("GameTimerText");
            RectTransform timerRect = timerObject != null
                ? timerObject.GetComponent<RectTransform>()
                : null;
            Component timerText = timerTextObject != null
                ? timerTextObject.GetComponent("TextMeshProUGUI")
                : null;
            GameObject loadoutObject = GameObject.Find("PlayerLoadoutDisplay");
            RectTransform loadoutRect = loadoutObject != null
                ? loadoutObject.GetComponent<RectTransform>()
                : null;
            bool expBarFound = expBarObject != null;
            bool expContainerTransparent = expBackground == null;
            bool frameFound = frameRect != null && frameImage != null;
            bool trackFound = trackRect != null && trackImage != null;
            bool fillFound = fillRect != null && fillImage != null;
            bool levelTextFound = levelRect != null;
            bool expTextFound = expText != null;
            bool runStatsFound = runStats != null;
            bool killCounterFound = killCounterRect != null;
            bool goldCounterFound = goldCounterRect != null;
            bool killCountTextFound = killCountText != null;
            bool goldCountTextFound = goldCountText != null;
            bool coinIconFound = coinIcon != null;
            bool skullIconFound = skullIcon != null;
            bool timerFound = timerObject != null;
            bool timerTextFound = timerText != null;
            bool loadoutFound = loadoutRect != null;

            PropertyInfo killTextProperty = killCountText != null
                ? killCountText.GetType().GetProperty("text")
                : null;
            PropertyInfo goldTextProperty = goldCountText != null
                ? goldCountText.GetType().GetProperty("text")
                : null;
            PropertyInfo coinSpriteProperty = coinIcon != null
                ? coinIcon.GetType().GetProperty("sprite")
                : null;
            PropertyInfo skullSpriteProperty = skullIcon != null
                ? skullIcon.GetType().GetProperty("sprite")
                : null;
            string initialKillText = killTextProperty != null
                ? killTextProperty.GetValue(killCountText, null) as string
                : null;
            string initialGoldText = goldTextProperty != null
                ? goldTextProperty.GetValue(goldCountText, null) as string
                : null;
            UnityEngine.Object coinSprite = coinSpriteProperty != null
                ? coinSpriteProperty.GetValue(coinIcon, null) as UnityEngine.Object
                : null;
            UnityEngine.Object skullSprite = skullSpriteProperty != null
                ? skullSpriteProperty.GetValue(skullIcon, null) as UnityEngine.Object
                : null;

            GameObject playerObject = GameObject.FindWithTag("Player");
            Component playerStats = playerObject != null
                ? playerObject.GetComponent("PlayerStats")
                : null;
            Component expBarUi = expBarObject != null
                ? expBarObject.GetComponent("ExpBarUI")
                : null;
            if (playerStats != null && expBarUi != null)
            {
                RuntimeComponentTestUtility.SetField(expBarUi, "fillSmoothSpeed", 0f);
                RuntimeComponentTestUtility.Invoke(playerStats, "AddExp", 2f);
                yield return null;
            }

            int killCountAfterRegistration = runStats != null
                ? RuntimeComponentTestUtility.GetProperty<int>(runStats, "KillCount")
                : -1;
            if (runStats != null)
            {
                RuntimeComponentTestUtility.Invoke(runStats, "RegisterKill");
                yield return null;
                killCountAfterRegistration = RuntimeComponentTestUtility.GetProperty<int>(runStats, "KillCount");
            }
            string killTextAfterRegistration = killTextProperty != null
                ? killTextProperty.GetValue(killCountText, null) as string
                : null;

            Type debugType = RuntimeComponentTestUtility.RequireRuntimeType("WorldWaveDebugUI");
            UnityEngine.Object[] debugObjects = Resources.FindObjectsOfTypeAll(debugType);
            bool debugComponentInMainScene = false;
            for (int index = 0; index < debugObjects.Length; index++)
            {
                Component debugComponent = debugObjects[index] as Component;
                if (debugComponent != null && debugComponent.gameObject.scene.name == MainSceneName)
                {
                    debugComponentInMainScene = true;
                    break;
                }
            }

            float expBarHeight = expBarRect != null ? expBarRect.rect.height : 0f;
            float levelLeftEdge = levelRect != null
                ? levelRect.anchoredPosition.x - levelRect.rect.width * levelRect.pivot.x
                : -1f;
            bool expBarFullWidth = expBarRect != null &&
                                   Mathf.Approximately(expBarRect.anchorMin.x, 0f) &&
                                   Mathf.Approximately(expBarRect.anchorMax.x, 1f) &&
                                   Mathf.Approximately(expBarRect.sizeDelta.x, 0f);
            Vector3[] expBarWorldCorners = new Vector3[4];
            Vector3[] canvasWorldCorners = new Vector3[4];
            Vector3[] frameWorldCorners = new Vector3[4];
            if (expBarRect != null)
            {
                expBarRect.GetWorldCorners(expBarWorldCorners);
            }

            if (canvasRect != null)
            {
                canvasRect.GetWorldCorners(canvasWorldCorners);
            }

            if (frameRect != null)
            {
                frameRect.GetWorldCorners(frameWorldCorners);
            }

            bool expBarMatchesCanvasWidth = expBarRect != null &&
                                            canvasRect != null &&
                                            Mathf.Approximately(
                                                expBarWorldCorners[0].x,
                                                canvasWorldCorners[0].x) &&
                                            Mathf.Approximately(
                                                expBarWorldCorners[2].x,
                                                canvasWorldCorners[2].x);
            float frameLeftMargin = frameRect != null
                ? frameWorldCorners[0].x - expBarWorldCorners[0].x
                : 0f;
            float frameRightMargin = frameRect != null
                ? expBarWorldCorners[2].x - frameWorldCorners[2].x
                : 0f;
            bool frameHasSymmetricMargins = frameRect != null &&
                                            frameLeftMargin > 0f &&
                                            Mathf.Abs(frameLeftMargin - frameRightMargin) < 0.01f;
            bool frameHierarchyValid = frameRect != null &&
                                       trackRect != null &&
                                       fillRect != null &&
                                       levelRect != null &&
                                       expTextRect != null &&
                                       trackRect.parent == frameRect &&
                                       fillRect.parent == trackRect &&
                                       levelRect.parent == trackRect &&
                                       expTextRect.parent == trackRect;
            bool frameConfigured = IsSpriteFreeSimpleImage(frameImage);
            bool trackConfigured = IsSpriteFreeSimpleImage(trackImage);
            bool fillConfigured = IsSpriteFreeSimpleImage(fillImage);
            bool fillAnchoredLeft = fillRect != null &&
                                    Mathf.Approximately(fillRect.anchorMin.x, 0f) &&
                                    Mathf.Approximately(fillRect.pivot.x, 0f) &&
                                    Mathf.Approximately(fillRect.sizeDelta.x, 0f);
            float displayedFillProgress = fillRect != null ? fillRect.anchorMax.x : -1f;
            bool levelTextInsideBar = levelRect != null &&
                                      trackRect != null &&
                                      levelRect.parent == trackRect &&
                                      Mathf.Approximately(levelRect.anchorMin.x, 0f) &&
                                      Mathf.Approximately(levelRect.anchorMax.x, 0f) &&
                                      levelLeftEdge >= 8f &&
                                      levelLeftEdge + levelRect.rect.width <= trackRect.rect.width;
            PropertyInfo alignmentProperty = expText != null
                ? expText.GetType().GetProperty("alignment")
                : null;
            PropertyInfo fontSizeProperty = expText != null
                ? expText.GetType().GetProperty("fontSize")
                : null;
            PropertyInfo fontStyleProperty = expText != null
                ? expText.GetType().GetProperty("fontStyle")
                : null;
            PropertyInfo timerFontSizeProperty = timerText != null
                ? timerText.GetType().GetProperty("fontSize")
                : null;
            PropertyInfo timerFontStyleProperty = timerText != null
                ? timerText.GetType().GetProperty("fontStyle")
                : null;
            PropertyInfo timerContentProperty = timerText != null
                ? timerText.GetType().GetProperty("text")
                : null;
            object alignmentValue = alignmentProperty != null
                ? alignmentProperty.GetValue(expText, null)
                : null;
            object fontSizeValue = fontSizeProperty != null
                ? fontSizeProperty.GetValue(expText, null)
                : null;
            object fontStyleValue = fontStyleProperty != null
                ? fontStyleProperty.GetValue(expText, null)
                : null;
            object timerFontSizeValue = timerFontSizeProperty != null
                ? timerFontSizeProperty.GetValue(timerText, null)
                : null;
            object timerFontStyleValue = timerFontStyleProperty != null
                ? timerFontStyleProperty.GetValue(timerText, null)
                : null;
            object timerContentValue = timerContentProperty != null
                ? timerContentProperty.GetValue(timerText, null)
                : null;
            bool expTextCentered = alignmentValue != null &&
                                   alignmentValue.ToString() == "Center" &&
                                   expTextRect != null &&
                                   expTextRect.parent == trackRect &&
                                   Mathf.Approximately(expTextRect.anchorMin.x, 0.5f) &&
                                   Mathf.Approximately(expTextRect.anchorMax.x, 0.5f);
            float expTextSize = fontSizeValue != null
                ? System.Convert.ToSingle(fontSizeValue)
                : 0f;
            bool expTextBold = fontStyleValue != null &&
                               fontStyleValue.ToString().Contains("Bold");
            float timerTextSize = timerFontSizeValue != null
                ? System.Convert.ToSingle(timerFontSizeValue)
                : 0f;
            bool timerTextBold = timerFontStyleValue != null &&
                                 timerFontStyleValue.ToString().Contains("Bold");
            string timerInitialText = timerContentValue as string;
            bool timerAtBottomCenter = timerRect != null &&
                                       Mathf.Approximately(timerRect.anchorMin.x, 0.5f) &&
                                       Mathf.Approximately(timerRect.anchorMax.x, 0.5f) &&
                                       Mathf.Approximately(timerRect.anchorMin.y, 0f) &&
                                       Mathf.Approximately(timerRect.anchorMax.y, 0f) &&
                                       timerRect.anchoredPosition.y > 0f;
            bool loadoutAvoidsExpBar = loadoutRect != null &&
                                       loadoutRect.anchoredPosition.y <= -56f;
            bool runStatsBelowExpBar = runStatsRect != null &&
                                       Mathf.Approximately(runStatsRect.anchorMin.x, 0f) &&
                                       Mathf.Approximately(runStatsRect.anchorMax.x, 0f) &&
                                       Mathf.Approximately(runStatsRect.anchorMin.y, 1f) &&
                                       Mathf.Approximately(runStatsRect.anchorMax.y, 1f) &&
                                       Mathf.Abs(runStatsRect.anchoredPosition.x - 64f) < 0.01f &&
                                       runStatsRect.anchoredPosition.y <= -56f;
            bool countersInOrder = runStatsRect != null &&
                                   killCounterRect != null &&
                                   goldCounterRect != null &&
                                   killCounterRect.parent == runStatsRect &&
                                   goldCounterRect.parent == runStatsRect &&
                                   goldCounterRect.anchoredPosition.x > killCounterRect.anchoredPosition.x;

            CleanupLoadedScene(MainSceneName);
            yield return null;

            Assert.IsTrue(expBarFound, "MainLevel 缺少 ExpBarContainer。");
            Assert.That(expBarHeight, Is.GreaterThanOrEqualTo(56f));
            Assert.IsTrue(expBarFullWidth, "经验条定位容器没有覆盖 Canvas 顶部全宽区域。");
            Assert.IsTrue(expBarMatchesCanvasWidth, "经验条定位容器没有贴齐 Canvas 左右边界。");
            Assert.IsTrue(expContainerTransparent, "ExpBarContainer 不应绘制全宽背景，黑色背景必须只存在于轨道内部。");
            Assert.IsTrue(frameFound, "经验条缺少 ExpBarFrame 外框。");
            Assert.IsTrue(frameConfigured, "经验条外框必须使用无 Sprite 的纯色矩形。");
            Assert.IsTrue(frameHasSymmetricMargins, "经验条外框没有在屏幕两侧保留对称空白。");
            Assert.IsTrue(trackFound, "经验条缺少 ExpBarTrack 内轨道。");
            Assert.IsTrue(trackConfigured, "经验条轨道必须使用无 Sprite 的纯色矩形。");
            Assert.IsTrue(fillFound, "经验条缺少 FillBar 填充层。");
            Assert.IsTrue(fillConfigured, "经验填充必须使用无 Sprite 的纯色矩形。");
            Assert.IsTrue(frameHierarchyValid, "经验条外框、轨道和填充层级不正确。");
            Assert.IsTrue(fillAnchoredLeft, "经验填充没有固定从轨道左侧开始。");
            Assert.That(displayedFillProgress, Is.EqualTo(0.2f).Within(0.001f));
            Assert.IsTrue(levelTextFound, "MainLevel 缺少 LevelText。");
            Assert.IsTrue(levelTextInsideBar, "等级文本左边缘没有留出安全边距。");
            Assert.IsTrue(expTextFound, "MainLevel 缺少 ExpText。");
            Assert.IsTrue(expTextCentered, "经验数值文本没有设置为水平居中。");
            Assert.That(expTextSize, Is.GreaterThanOrEqualTo(22f));
            Assert.IsTrue(expTextBold, "经验文本没有使用粗体样式。");
            Assert.IsTrue(timerFound, "MainLevel 缺少 GameTimer。");
            Assert.IsTrue(timerTextFound, "GameTimer 缺少 GameTimerText。");
            Assert.IsTrue(timerAtBottomCenter, "计时器没有放置在画面底部中央。");
            Assert.That(timerInitialText, Is.EqualTo("00:00"));
            Assert.That(timerTextSize, Is.GreaterThanOrEqualTo(30f));
            Assert.IsTrue(timerTextBold, "计时器文本没有使用粗体样式。");
            Assert.IsTrue(loadoutFound, "MainLevel 缺少运行时装备栏。");
            Assert.IsTrue(loadoutAvoidsExpBar, "装备栏没有向下避让顶部经验条。");
            Assert.IsTrue(runStatsFound, "MainLevel 缺少 RunStatsUI。");
            Assert.IsTrue(killCounterFound, "MainLevel 缺少击杀计数器容器。");
            Assert.IsTrue(goldCounterFound, "MainLevel 缺少金币计数器容器。");
            Assert.IsTrue(killCountTextFound, "MainLevel 缺少击杀数字文本。");
            Assert.IsTrue(goldCountTextFound, "MainLevel 缺少金币数字文本。");
            Assert.IsTrue(coinIconFound && coinSprite != null, "金币计数器缺少金币 Sprite 图标。");
            Assert.IsTrue(skullIconFound && skullSprite != null, "击杀计数器缺少骷髅 Sprite 图标。");
            Assert.IsTrue(runStatsBelowExpBar, "计数器没有与经验条左边界对齐或放在经验条下方。");
            Assert.IsTrue(countersInOrder, "金币计数器没有位于击杀计数器右侧。");
            Assert.That(initialKillText, Is.EqualTo("0"));
            Assert.That(initialGoldText, Is.EqualTo("0"));
            Assert.That(killCountAfterRegistration, Is.EqualTo(1));
            Assert.That(killTextAfterRegistration, Is.EqualTo("1"));
            Assert.IsFalse(debugComponentInMainScene, "MainLevel 仍然挂载双世界线调试组件。");
        }

        /// <summary>主场景运行后计时器应累计经过的游戏时间并更新显示。</summary>
        [UnityTest]
        public IEnumerator GameTimer_运行一秒_显示经过时间()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            GameObject timerObject = GameObject.Find("GameTimer");
            Component timer = timerObject != null
                ? timerObject.GetComponent("GameTimerUI")
                : null;
            bool timerFound = timer != null;
            float elapsedSeconds = 0f;
            string timerTextValue = null;

            if (timerFound)
            {
                yield return new WaitForSeconds(1.1f);
                elapsedSeconds = RuntimeComponentTestUtility.GetProperty<float>(
                    timer,
                    "CurrentTimeSeconds");

                GameObject timerTextObject = GameObject.Find("GameTimerText");
                Component timerText = timerTextObject != null
                    ? timerTextObject.GetComponent("TextMeshProUGUI")
                    : null;
                PropertyInfo textProperty = timerText != null
                    ? timerText.GetType().GetProperty("text")
                    : null;
                timerTextValue = textProperty != null
                    ? textProperty.GetValue(timerText, null) as string
                    : null;
            }

            CleanupLoadedScene(MainSceneName);
            yield return null;

            Assert.IsTrue(timerFound, "MainLevel 缺少 GameTimerUI 组件。");
            Assert.That(elapsedSeconds, Is.GreaterThan(0.5f));
            Assert.That(timerTextValue, Is.Not.EqualTo("00:00"));
        }

        /// <summary>暂停菜单按钮应解除暂停并把玩家送回真实主菜单场景。</summary>
        [UnityTest]
        public IEnumerator PauseMainMenuButton_真实主场景点击_返回主菜单()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Type managerType = RuntimeComponentTestUtility.RequireRuntimeType("GameFlowManager");
            Component manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
            bool managerFound = manager != null;
            bool pausedBeforeReturn = false;
            bool returnButtonFound = false;
            bool reachedMainMenu = false;

            if (managerFound)
            {
                RuntimeComponentTestUtility.Invoke(manager, "PauseGame");
                pausedBeforeReturn =
                    RuntimeComponentTestUtility.GetProperty<bool>(manager, "IsPaused") &&
                    Mathf.Approximately(Time.timeScale, 0f);

                GameObject returnButtonObject = GameObject.Find("PauseMainMenuButton");
                UnityEngine.UI.Button returnButton = returnButtonObject != null
                    ? returnButtonObject.GetComponent<UnityEngine.UI.Button>()
                    : null;
                returnButtonFound = returnButton != null;

                if (returnButtonFound)
                {
                    returnButton.onClick.Invoke();
                    for (int frame = 0; frame < 600; frame++)
                    {
                        if (SceneManager.GetActiveScene().name == "MainMenu")
                        {
                            reachedMainMenu = true;
                            break;
                        }

                        yield return null;
                    }
                }
            }

            string activeSceneName = SceneManager.GetActiveScene().name;
            CleanupLoadedScene(activeSceneName);
            yield return null;
            Time.timeScale = 1f;

            Assert.IsTrue(managerFound, "MainLevel 缺少 GameFlowManager。");
            Assert.IsTrue(pausedBeforeReturn, "返回主菜单前没有进入真实暂停状态。");
            Assert.IsTrue(returnButtonFound, "暂停菜单缺少返回主界面按钮。");
            Assert.IsTrue(reachedMainMenu, "点击返回主界面后没有进入 MainMenu。");
            Assert.That(activeSceneName, Is.EqualTo("MainMenu"));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>验证 UI Image 使用无 Sprite 的 Simple 模式，从而绘制完整纯色矩形。</summary>
        private static bool IsSpriteFreeSimpleImage(Component imageComponent)
        {
            if (imageComponent == null)
            {
                return false;
            }

            PropertyInfo spriteProperty = imageComponent.GetType().GetProperty("sprite");
            PropertyInfo typeProperty = imageComponent.GetType().GetProperty("type");
            UnityEngine.Object sprite = spriteProperty != null
                ? spriteProperty.GetValue(imageComponent, null) as UnityEngine.Object
                : null;
            object imageType = typeProperty != null
                ? typeProperty.GetValue(imageComponent, null)
                : null;

            return spriteProperty != null &&
                   sprite == null &&
                   imageType != null &&
                   imageType.ToString() == "Simple";
        }

        /// <summary>把正式场景卸载到临时空场景，避免场景型 PlayMode 测试相互污染。</summary>
        private static void CleanupLoadedScene(string sceneName)
        {
            Scene cleanupScene = SceneManager.CreateScene("PlayModeTest_CleanupScene");
            SceneManager.SetActiveScene(cleanupScene);
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.IsValid() && loadedScene.isLoaded && loadedScene != cleanupScene)
            {
                SceneManager.UnloadSceneAsync(loadedScene);
            }
        }
    }
}
