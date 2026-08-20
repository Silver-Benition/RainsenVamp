using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证暂停原因组合、界面状态和玩家死亡后的单局流程。</summary>
    public sealed class GameFlowManagerTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>验证管理器建立后没有暂停原因，且所有流程面板初始隐藏。</summary>
        [Test]
        public void Awake_新一局_时间正常且流程面板隐藏()
        {
            GameFlowFixture fixture = CreateFixture();

            Assert.AreSame(fixture.Manager, GameFlowManager.Instance);
            Assert.IsFalse(fixture.Manager.IsPaused);
            Assert.IsFalse(fixture.Manager.IsGameOver);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
            Assert.IsFalse(fixture.LevelUpPanel.activeSelf);
            Assert.IsFalse(fixture.PausePanel.activeSelf);
            Assert.IsFalse(fixture.GameOverPanel.activeSelf);
        }

        /// <summary>验证手动暂停会冻结时间并显示面板，继续后完整恢复。</summary>
        [Test]
        public void PauseAndResume_手动暂停_同步时间与面板()
        {
            GameFlowFixture fixture = CreateFixture();

            fixture.Manager.PauseGame();

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.IsTrue(fixture.Manager.IsManuallyPaused);
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsTrue(fixture.PausePanel.activeSelf);

            fixture.Manager.ResumeGame();

            Assert.IsFalse(fixture.Manager.IsPaused);
            Assert.IsFalse(fixture.Manager.IsManuallyPaused);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
            Assert.IsFalse(fixture.PausePanel.activeSelf);
        }

        /// <summary>手动暂停事件只发布真实状态变化，升级暂停不得触发装备栏展开。</summary>
        [Test]
        public void ManualPauseChanged_手动暂停与恢复_只发布一次对应状态()
        {
            GameFlowFixture fixture = CreateFixture();
            var receivedStates = new List<bool>();
            fixture.Manager.ManualPauseChanged += receivedStates.Add;

            fixture.Manager.EnterLevelUpPause();
            fixture.Manager.ExitLevelUpPause();
            fixture.Manager.PauseGame();
            fixture.Manager.PauseGame();
            fixture.Manager.ResumeGame();
            fixture.Manager.ResumeGame();

            CollectionAssert.AreEqual(new[] { true, false }, receivedStates);
        }

        /// <summary>验证升级暂停拥有独立原因，进入与解除都会同步全局时间。</summary>
        [Test]
        public void LevelUpPause_进入后解除_同步全局时间()
        {
            GameFlowFixture fixture = CreateFixture();

            fixture.Manager.EnterLevelUpPause();

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));

            fixture.Manager.ExitLevelUpPause();

            Assert.IsFalse(fixture.Manager.IsPaused);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>验证升级选择期间请求手动暂停会被拒绝，避免出现重叠暂停界面。</summary>
        [Test]
        public void PauseGame_升级暂停期间_不进入手动暂停()
        {
            GameFlowFixture fixture = CreateFixture();
            fixture.Manager.EnterLevelUpPause();

            fixture.Manager.PauseGame();

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.IsFalse(fixture.PausePanel.activeSelf);
            fixture.Manager.ExitLevelUpPause();
            Assert.IsFalse(fixture.Manager.IsPaused);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>验证手动与升级暂停叠加时，解除其中一个原因不会错误恢复游戏。</summary>
        [Test]
        public void PauseReasons_手动与升级叠加_全部解除后才恢复时间()
        {
            GameFlowFixture fixture = CreateFixture();
            fixture.Manager.PauseGame();
            fixture.Manager.EnterLevelUpPause();

            fixture.Manager.ResumeGame();

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsFalse(fixture.PausePanel.activeSelf);

            fixture.Manager.ExitLevelUpPause();

            Assert.IsFalse(fixture.Manager.IsPaused);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>
        /// 验证玩家死亡会覆盖其他暂停原因、停止控制和刚体，并进入不可普通恢复的 Game Over。
        /// </summary>
        [Test]
        public void PlayerDeath_收到致死伤害_进入不可恢复的游戏结束状态()
        {
            GameFlowFixture fixture = CreateFixture();
            fixture.LevelUpPanel.SetActive(true);
            fixture.PausePanel.SetActive(true);
            fixture.PlayerRigidbody.velocity = new Vector2(3f, -2f);

            fixture.PlayerHealth.TakeDamage(fixture.PlayerHealth.MaxHealth);

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.IsTrue(fixture.Manager.IsGameOver);
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsFalse(fixture.PlayerController.enabled);
            Assert.That(fixture.PlayerRigidbody.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsFalse(fixture.LevelUpPanel.activeSelf);
            Assert.IsFalse(fixture.PausePanel.activeSelf);
            Assert.IsTrue(fixture.GameOverPanel.activeSelf);

            fixture.Manager.ResumeGame();
            fixture.Manager.ExitLevelUpPause();
            fixture.Manager.PauseGame();

            Assert.IsTrue(fixture.Manager.IsPaused);
            Assert.IsTrue(fixture.Manager.IsGameOver);
            Assert.That(Time.timeScale, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsTrue(fixture.GameOverPanel.activeSelf);
        }

        /// <summary>创建具备真实 Awake/OnEnable 顺序的最小玩家、界面和流程管理器。</summary>
        private GameFlowFixture CreateFixture()
        {
            GameObject playerObject = CreateTrackedGameObject("AutomationTest_Player");
            Rigidbody2D playerRigidbody = playerObject.AddComponent<Rigidbody2D>();
            playerRigidbody.gravityScale = 0f;
            PlayerController playerController = playerObject.AddComponent<PlayerController>();
            PlayerHealth playerHealth = playerObject.AddComponent<PlayerHealth>();
            TestObjectUtility.SetFloat(playerHealth, "invulnerabilityDuration", 0f);
            TestObjectUtility.InvokeNonPublicMethod(playerController, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(playerHealth, "Awake");

            GameObject levelUpPanel = CreateTrackedGameObject("AutomationTest_LevelUpPanel");
            GameObject pausePanel = CreateTrackedGameObject("AutomationTest_PausePanel");
            GameObject gameOverPanel = CreateTrackedGameObject("AutomationTest_GameOverPanel");

            GameObject managerObject = CreateTrackedGameObject("AutomationTest_GameFlowManager");
            GameFlowManager manager = managerObject.AddComponent<GameFlowManager>();
            TestObjectUtility.SetObjectReference(manager, "playerHealth", playerHealth);
            TestObjectUtility.SetObjectReference(manager, "playerController", playerController);
            TestObjectUtility.SetObjectReference(manager, "playerRigidbody", playerRigidbody);
            TestObjectUtility.SetObjectReference(manager, "levelUpPanel", levelUpPanel);
            TestObjectUtility.SetObjectReference(manager, "pausePanel", pausePanel);
            TestObjectUtility.SetObjectReference(manager, "gameOverPanel", gameOverPanel);
            TestObjectUtility.InvokeNonPublicMethod(manager, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(manager, "OnEnable");

            return new GameFlowFixture(
                manager,
                playerHealth,
                playerController,
                playerRigidbody,
                levelUpPanel,
                pausePanel,
                gameOverPanel);
        }

        /// <summary>集中保存一项流程测试需要断言的组件引用。</summary>
        private sealed class GameFlowFixture
        {
            /// <summary>建立不可变测试夹具，避免各测试重复查找组件。</summary>
            public GameFlowFixture(
                GameFlowManager manager,
                PlayerHealth playerHealth,
                PlayerController playerController,
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

            public GameFlowManager Manager { get; }
            public PlayerHealth PlayerHealth { get; }
            public PlayerController PlayerController { get; }
            public Rigidbody2D PlayerRigidbody { get; }
            public GameObject LevelUpPanel { get; }
            public GameObject PausePanel { get; }
            public GameObject GameOverPanel { get; }
        }
    }
}
