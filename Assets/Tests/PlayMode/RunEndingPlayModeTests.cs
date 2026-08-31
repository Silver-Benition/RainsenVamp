using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>通过真实 MainLevel、对象池和 Player Loop 验证首领遭遇及世界切换锁。</summary>
    public sealed class RunEndingPlayModeTests : PlayModeComponentTestBase
    {
        private const string MainSceneName = "MainLevel";
        private const float FloatTolerance = 0.0001f;

        /// <summary>120 秒遭遇配置可在当前活动世界生成首领，并锁定世界切换但保留正常运行。</summary>
        [UnityTest]
        public IEnumerator MainLevel_RunDirector_生成武装巨像并锁定世界切换()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Component coordinator = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("WorldLineCoordinator")) as Component;
            Component resultsUi = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunResultsUI")) as Component;
            Assert.IsNotNull(director, "MainLevel 缺少 RunDirector。");
            Assert.IsNotNull(coordinator, "MainLevel 缺少 WorldLineCoordinator。");
            Assert.IsNotNull(resultsUi, "MainLevel 缺少 RunResultsUI。");
            Assert.IsNotNull(
                RuntimeComponentTestUtility.GetProperty<object>(director, "Telemetry"),
                "RunDirector 未建立本局统计容器。");
            Assert.IsFalse(
                RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "IsWorldSwitchLocked"));

            Assert.IsTrue(
                (bool)RuntimeComponentTestUtility.Invoke(director, "DebugTriggerBossEncounter"),
                "Boss 遭遇未能从当前活动世界对象池生成。");
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(director, "IsBossSpawned"));
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "IsWorldSwitchLocked"));

            bool worldBeforeAttempt = RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "MainWorldIsActive");
            RuntimeComponentTestUtility.Invoke(coordinator, "SwitchWorldLine");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "MainWorldIsActive"),
                Is.EqualTo(worldBeforeAttempt),
                "Boss 生成后仍可切换世界。");

            Component simulation = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                "ActiveWorldSimulation") as Component;
            Assert.IsNotNull(simulation);
            Component boss = simulation.GetComponentInChildren(
                RuntimeComponentTestUtility.RequireRuntimeType("BossEnemyController"),
                true);
            Assert.IsNotNull(boss, "活动世界中找不到已生成的武装巨像。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentHealth"),
                Is.EqualTo(800f).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentMoveSpeed"),
                Is.EqualTo(0.9f).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentCollisionDamage"),
                Is.EqualTo(18f).Within(FloatTolerance));
            Assert.IsFalse(
                RuntimeComponentTestUtility.GetProperty<bool>(boss, "IsDefanged"),
                "Boss 不应从普通敌人 Defang 逻辑继承免疫错误。");

            object phaseDamage = RuntimeComponentTestUtility.Invoke(
                boss,
                "ApplyCombatDamage",
                400f,
                false);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(phaseDamage, "Accepted"));
            yield return null;
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(boss, "IsPhaseTwoActive"),
                "Boss 生命值降至 50% 后未进入第二阶段。");
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance), "仅进入二阶段不应冻结游戏。");
        }
    }
}
