using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在真实生命周期与时间系统中验证暂停组合和玩家死亡流程。</summary>
    public sealed class GameFlowPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>暂停应冻结缩放时间；多个暂停原因全部释放后才恢复 Player Loop 时间。</summary>
        [UnityTest]
        public IEnumerator PauseReasons_真实时间冻结_全部解除后恢复()
        {
            GameFlowFixture fixture = CreateFixture();
            yield return null;

            RuntimeComponentTestUtility.Invoke(fixture.Manager, "PauseGame");

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(fixture.Manager, "IsPaused"));
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsTrue(fixture.PausePanel.activeSelf);

            float frozenTime = Time.time;
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(Time.time, Is.EqualTo(frozenTime).Within(0.002f));

            RuntimeComponentTestUtility.Invoke(fixture.Manager, "EnterLevelUpPause");
            RuntimeComponentTestUtility.Invoke(fixture.Manager, "ResumeGame");
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(fixture.Manager, "IsPaused"));
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));

            RuntimeComponentTestUtility.Invoke(fixture.Manager, "ExitLevelUpPause");
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(fixture.Manager, "IsPaused"));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));

            float resumedTime = Time.time;
            yield return new WaitForSeconds(0.03f);
            Assert.That(Time.time, Is.GreaterThan(resumedTime));
        }

        /// <summary>组件禁用再启用后应恢复一次死亡订阅，并执行完整 Game Over 流程。</summary>
        [UnityTest]
        public IEnumerator Lifecycle_管理器重新启用后_死亡流程仍只执行一次()
        {
            GameFlowFixture fixture = CreateFixture();
            fixture.Manager.gameObject.SetActive(false);
            yield return null;
            fixture.Manager.gameObject.SetActive(true);
            yield return null;

            fixture.PlayerRigidbody.velocity = new Vector2(3f, -2f);
            RuntimeComponentTestUtility.Invoke(fixture.PlayerHealth, "TakeDamage", 100f);

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(fixture.Manager, "IsGameOver"));
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsFalse(fixture.PlayerController.enabled);
            Assert.That(fixture.PlayerRigidbody.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsFalse(fixture.LevelUpPanel.activeSelf);
            Assert.IsFalse(fixture.PausePanel.activeSelf);
            Assert.IsTrue(fixture.GameOverPanel.activeSelf);
        }

        /// <summary>按真实 Awake/OnEnable 顺序创建最小玩家、面板与流程管理器。</summary>
        private GameFlowFixture CreateFixture()
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_GameFlowPlayer", false);
            player.tag = "Player";
            Rigidbody2D playerRigidbody = player.AddComponent<Rigidbody2D>();
            playerRigidbody.gravityScale = 0f;
            Component playerControllerComponent = RuntimeComponentTestUtility.AddRuntimeComponent(
                player,
                "PlayerController");
            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(
                player,
                "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "maxHealth", 100f);
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0f);
            player.SetActive(true);

            GameObject levelUpPanel = CreateTrackedGameObject("PlayModeTest_LevelUpPanel");
            GameObject pausePanel = CreateTrackedGameObject("PlayModeTest_PausePanel");
            GameObject gameOverPanel = CreateTrackedGameObject("PlayModeTest_GameOverPanel");

            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_GameFlowManager", false);
            Component manager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "GameFlowManager");
            RuntimeComponentTestUtility.SetField(manager, "playerHealth", playerHealth);
            RuntimeComponentTestUtility.SetField(manager, "playerController", playerControllerComponent);
            RuntimeComponentTestUtility.SetField(manager, "playerRigidbody", playerRigidbody);
            RuntimeComponentTestUtility.SetField(manager, "levelUpPanel", levelUpPanel);
            RuntimeComponentTestUtility.SetField(manager, "pausePanel", pausePanel);
            RuntimeComponentTestUtility.SetField(manager, "gameOverPanel", gameOverPanel);
            managerObject.SetActive(true);

            return new GameFlowFixture(
                manager,
                playerHealth,
                (Behaviour)playerControllerComponent,
                playerRigidbody,
                levelUpPanel,
                pausePanel,
                gameOverPanel);
        }

        /// <summary>集中保存一项 PlayMode 流程测试需要观察的真实组件引用。</summary>
        private sealed class GameFlowFixture
        {
            /// <summary>建立不可变夹具，避免测试通过场景搜索取得错误对象。</summary>
            public GameFlowFixture(
                Component manager,
                Component playerHealth,
                Behaviour playerController,
                Rigidbody2D playerRigidbody,
                GameObject levelUpPanel,
                GameObject pausePanel,
                GameObject gameOverPanel)
            {
                Manager = manager;
                PlayerHealth = playerHealth;
                PlayerController = playerController;
                PlayerRigidbody = playerRigidbody;
                LevelUpPanel = levelUpPanel;
                PausePanel = pausePanel;
                GameOverPanel = gameOverPanel;
            }

            public Component Manager { get; }
            public Component PlayerHealth { get; }
            public Behaviour PlayerController { get; }
            public Rigidbody2D PlayerRigidbody { get; }
            public GameObject LevelUpPanel { get; }
            public GameObject PausePanel { get; }
            public GameObject GameOverPanel { get; }
        }
    }
}
