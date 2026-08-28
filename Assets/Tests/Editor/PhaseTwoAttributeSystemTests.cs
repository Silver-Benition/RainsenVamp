using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证阶段二的纯逻辑公式、局内次数、候选过滤、敌人快照和复活分流。</summary>
    public sealed class PhaseTwoAttributeSystemTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;
        private readonly List<ScriptableObject> _createdData = new List<ScriptableObject>();

        /// <summary>销毁测试创建的数据对象。</summary>
        [TearDown]
        public void CleanUpPhaseTwoData()
        {
            for (int index = _createdData.Count - 1; index >= 0; index--)
            {
                if (_createdData[index] != null)
                {
                    Object.DestroyImmediate(_createdData[index]);
                }
            }

            _createdData.Clear();
        }

        /// <summary>Luck=1 保持基础掉率，更高 Luck 应平滑提高而不超过一。</summary>
        [Test]
        public void DropChanceResolver_不同Luck_保持边界并提高概率()
        {
            Assert.That(DropChanceResolver.GetLuckAdjustedChance(0.2f, 1f),
                Is.EqualTo(0.2f).Within(FloatTolerance));
            Assert.That(DropChanceResolver.GetLuckAdjustedChance(0.2f, 2f),
                Is.EqualTo(0.36f).Within(FloatTolerance));
            Assert.That(DropChanceResolver.GetLuckAdjustedChance(2f, 5f),
                Is.EqualTo(1f).Within(FloatTolerance));
            Assert.IsFalse(DropChanceResolver.ShouldDrop(0.2f, 1f, 0.2f));
        }

        /// <summary>同一随机值下，提高 Luck 应能把结果从普通候选推向声明了影响系数的稀有候选。</summary>
        [Test]
        public void UpgradeCandidateResolver_提高Luck_增加稀有候选相对权重()
        {
            UpgradeDataSO common = CreateUpgrade("upgrade_common", 100f, 0f);
            UpgradeDataSO rare = CreateUpgrade("upgrade_rare", 10f, 1f);
            var pool = new List<UpgradeDataSO> { common, rare };

            List<UpgradeDataSO> normalLuck = UpgradeCandidateResolver.SelectWeightedWithoutReplacement(
                pool,
                1,
                1f,
                new FixedRandomSource(0.75f));
            List<UpgradeDataSO> highLuck = UpgradeCandidateResolver.SelectWeightedWithoutReplacement(
                pool,
                1,
                10f,
                new FixedRandomSource(0.75f));

            Assert.AreSame(common, normalLuck[0]);
            Assert.AreSame(rare, highLuck[0]);
        }

        /// <summary>无放回抽样必须过滤重复稳定 ID，避免同面板出现同一逻辑奖励。</summary>
        [Test]
        public void UpgradeCandidateResolver_重复稳定ID_只返回一个候选()
        {
            UpgradeDataSO first = CreateUpgrade("upgrade_same", 100f, 0f);
            UpgradeDataSO duplicate = CreateUpgrade("upgrade_same", 100f, 0f);

            List<UpgradeDataSO> selected = UpgradeCandidateResolver.SelectWeightedWithoutReplacement(
                new List<UpgradeDataSO> { first, duplicate },
                3,
                1f,
                new FixedRandomSource(0f));

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.AreSame(first, selected[0]);
        }

        /// <summary>次数消费后普通属性重算不得返还，容量真实增加时只补充差额。</summary>
        [Test]
        public void RunState_资源容量变化_只增量调整剩余次数()
        {
            CharacterDataSO character = CreateCharacter();
            character.baseStats.revival = 1f;
            character.baseStats.reroll = 2f;
            character.baseStats.skip = 1f;
            character.baseStats.banish = 1f;
            PlayerStats stats = CreatePlayerStats(character);
            RunState runState = CreateInitializedRunState(stats);

            Assert.IsTrue(runState.TryConsumeReroll());
            Assert.That(runState.RemainingRerolls, Is.EqualTo(1));

            stats.SetModifiers("test.reroll", new[]
            {
                new PlayerStatModifier(PlayerStatType.Reroll, PlayerStatModifierMode.Flat, 1f)
            });
            Assert.That(runState.RemainingRerolls, Is.EqualTo(2));

            stats.SetModifiers("test.reroll", new[]
            {
                new PlayerStatModifier(PlayerStatType.Reroll, PlayerStatModifierMode.Flat, 1f)
            });
            Assert.That(runState.RemainingRerolls, Is.EqualTo(2));
            Assert.IsTrue(runState.TryConsumeBanish());
            Assert.IsTrue(runState.BanishUpgrade("upgrade_test"));
            Assert.IsTrue(runState.IsBanished("upgrade_test"));
        }

        /// <summary>放逐一个候选应消费整次升级机会，不能在同一面板继续领取其他奖励。</summary>
        [Test]
        public void LevelUpManager_放逐候选_立即结束当前升级机会()
        {
            CharacterDataSO character = CreateCharacter();
            character.baseStats.banish = 1f;
            PlayerStats stats = CreatePlayerStats(character);
            RunState runState = CreateInitializedRunState(stats);
            UpgradeDataSO upgrade = CreateUpgrade("upgrade_banish_target", 100f, 0f);

            GameObject panel = CreateTrackedGameObject("AutomationTest_BanishPanel");
            GameObject managerObject = CreateTrackedGameObject("AutomationTest_BanishManager");
            LevelUpManager manager = managerObject.AddComponent<LevelUpManager>();
            manager.levelUpPanel = panel;
            manager.allAvailableUpgrades = new List<UpgradeDataSO> { upgrade };
            TestObjectUtility.InvokeNonPublicMethod(manager, "Awake");
            TestObjectUtility.SetPrivateField(manager, "playerTransform", stats.transform);
            Assert.IsTrue(TestObjectUtility.InvokeNonPublicMethod<bool>(manager, "ResolvePlayerReferences"));

            panel.SetActive(true);
            ((List<UpgradeDataSO>)manager.CurrentCandidates).Add(upgrade);
            Time.timeScale = 0f;
            manager.ToggleBanishMode();
            manager.HandleCandidateSelected(upgrade);

            Assert.IsTrue(runState.IsBanished(upgrade.GetStableId()));
            Assert.That(runState.RemainingBanishes, Is.Zero);
            Assert.IsFalse(panel.activeSelf);
            Assert.That(manager.CurrentCandidates, Is.Empty);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        /// <summary>Curse、Charm 与 Defang 应生成预期数值，且 Defang 统一清零接触和远程伤害。</summary>
        [Test]
        public void EnemySpawnSnapshot_诅咒魅惑与剥夺_统一生成和伤害语义()
        {
            EnemyDataSO data = ScriptableObject.CreateInstance<EnemyDataSO>();
            data.maxHealth = 10f;
            data.moveSpeed = 2f;
            data.collisionDamage = 4f;
            data.canBeDefanged = true;
            _createdData.Add(data);

            EnemySpawnSnapshot defanged = EnemySpawnSnapshotFactory.Create(data, 1.5f, 1f, 0f);
            EnemySpawnSnapshot armed = EnemySpawnSnapshotFactory.Create(data, 1.5f, 0f, 0f);

            Assert.That(defanged.MaxHealth, Is.EqualTo(15f).Within(FloatTolerance));
            Assert.That(defanged.MoveSpeed, Is.EqualTo(3f).Within(FloatTolerance));
            Assert.That(defanged.CollisionDamage, Is.Zero);
            Assert.That(defanged.ResolveOutgoingDamage(8f), Is.Zero);
            Assert.That(armed.CollisionDamage, Is.EqualTo(6f).Within(FloatTolerance));
            Assert.That(armed.ResolveOutgoingDamage(8f), Is.EqualTo(12f).Within(FloatTolerance));
            Assert.That(EnemySpawnSnapshotFactory.GetEffectiveSpawnRate(2f, 1.5f, 2f),
                Is.EqualTo(4.2f).Within(FloatTolerance));
            Assert.That(EnemySpawnSnapshotFactory.GetEffectiveMaxAlive(10, 1.5f, 2f),
                Is.EqualTo(55));
        }

        /// <summary>远程弹体必须在发射时复制 Defang 结果，来源之后改变也不影响已经发射的弹体。</summary>
        [Test]
        public void EnemyProjectile_Defang来源_发射伤害为零()
        {
            EnemyDataSO data = ScriptableObject.CreateInstance<EnemyDataSO>();
            data.maxHealth = 10f;
            data.collisionDamage = 4f;
            _createdData.Add(data);

            GameObject enemyObject = CreateTrackedGameObject("AutomationTest_DefangedEnemy");
            enemyObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            enemyObject.AddComponent<BoxCollider2D>();
            EnemyBase enemy = enemyObject.AddComponent<EnemyBase>();
            enemy.enemyData = data;
            enemy.ApplySpawnSnapshot(EnemySpawnSnapshotFactory.Create(data, 2f, 1f, 0f));

            GameObject projectileObject = CreateTrackedGameObject("AutomationTest_EnemyProjectile");
            projectileObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            projectileObject.AddComponent<CircleCollider2D>().isTrigger = true;
            EnemyProjectile projectile = projectileObject.AddComponent<EnemyProjectile>();
            projectile.Launch(Vector2.right, 12f, enemy);

            Assert.That(projectile.ResolvedDamage, Is.Zero);
        }

        /// <summary>有 Revival 时首次死亡也应显示死亡流程，只有确认后才消费并半血复活。</summary>
        [Test]
        public void GameFlow_一次Revival_死亡面板确认后半血复活()
        {
            CharacterDataSO character = CreateCharacter();
            character.baseStats.maxHealth = 100f;
            character.baseStats.revival = 1f;

            GameObject player = CreateTrackedGameObject("AutomationTest_RevivalPlayer");
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            PlayerStats stats = player.AddComponent<PlayerStats>();
            stats.SetCharacterData(character);
            PlayerController controller = player.AddComponent<PlayerController>();
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            TestObjectUtility.SetFloat(health, "invulnerabilityDuration", 0f);
            TestObjectUtility.InvokeNonPublicMethod(controller, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
            RunState runState = CreateInitializedRunState(stats);

            GameObject levelPanel = CreateTrackedGameObject("AutomationTest_LevelPanel");
            GameObject pausePanel = CreateTrackedGameObject("AutomationTest_PausePanel");
            GameObject overPanel = CreateTrackedGameObject("AutomationTest_OverPanel");
            GameObject managerObject = CreateTrackedGameObject("AutomationTest_RevivalFlow");
            GameFlowManager manager = managerObject.AddComponent<GameFlowManager>();
            TestObjectUtility.SetObjectReference(manager, "playerHealth", health);
            TestObjectUtility.SetObjectReference(manager, "playerController", controller);
            TestObjectUtility.SetObjectReference(manager, "playerRigidbody", body);
            TestObjectUtility.SetObjectReference(manager, "levelUpPanel", levelPanel);
            TestObjectUtility.SetObjectReference(manager, "pausePanel", pausePanel);
            TestObjectUtility.SetObjectReference(manager, "gameOverPanel", overPanel);
            TestObjectUtility.SetFloat(manager, "reviveInvulnerabilityDuration", 0f);
            TestObjectUtility.SetFloat(manager, "reviveAnimationDuration", 0f);
            TestObjectUtility.InvokeNonPublicMethod(manager, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(manager, "OnEnable");

            health.TakeDamage(100f);

            Assert.IsTrue(health.IsDead);
            Assert.That(runState.RemainingRevivals, Is.EqualTo(1));
            Assert.IsTrue(manager.IsGameOver);
            Assert.IsTrue(overPanel.activeSelf);

            manager.RequestRevive();

            Assert.IsFalse(health.IsDead);
            Assert.That(health.CurrentHealth, Is.EqualTo(50f).Within(FloatTolerance));
            Assert.That(runState.RemainingRevivals, Is.Zero);
            Assert.IsFalse(manager.IsGameOver);
            Assert.IsFalse(overPanel.activeSelf);

            health.TakeDamage(100f);

            Assert.IsTrue(health.IsDead);
            Assert.IsTrue(manager.IsGameOver);
            Assert.IsTrue(overPanel.activeSelf);
        }

        /// <summary>创建带指定稳定 ID 与权重的临时升级数据。</summary>
        private UpgradeDataSO CreateUpgrade(string id, float weight, float luckInfluence)
        {
            UpgradeDataSO upgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            upgrade.upgradeID = id;
            upgrade.baseWeight = weight;
            upgrade.luckInfluence = luckInfluence;
            _createdData.Add(upgrade);
            return upgrade;
        }

        /// <summary>创建中性角色数据并登记销毁。</summary>
        private CharacterDataSO CreateCharacter()
        {
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            character.characterID = "character_phase_two_test";
            character.baseStats = new CharacterBaseStats();
            _createdData.Add(character);
            return character;
        }

        /// <summary>创建绑定角色数据的玩家属性组件。</summary>
        private PlayerStats CreatePlayerStats(CharacterDataSO character)
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_PhaseTwoPlayer");
            PlayerStats stats = player.AddComponent<PlayerStats>();
            stats.SetCharacterData(character);
            return stats;
        }

        /// <summary>
        /// 在 EditMode 中显式执行 RunState 生命周期。
        /// 普通 MonoBehaviour 的 Awake/OnEnable 不会像 PlayMode 那样由 Player Loop 自动调用。
        /// </summary>
        private static RunState CreateInitializedRunState(PlayerStats playerStats)
        {
            RunState runState = RunState.GetOrCreate(playerStats);
            TestObjectUtility.InvokeNonPublicMethod(runState, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(runState, "OnEnable");
            return runState;
        }

        /// <summary>始终返回同一随机值的确定性测试随机源。</summary>
        private sealed class FixedRandomSource : IRandomSource
        {
            private readonly float _value;

            /// <summary>保存本测试要返回的单位区间值。</summary>
            public FixedRandomSource(float value)
            {
                _value = value;
            }

            /// <summary>返回构造时指定的固定值。</summary>
            public float NextUnitFloat()
            {
                return _value;
            }
        }
    }
}
