using System.Collections;
using System;
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
    }
}
