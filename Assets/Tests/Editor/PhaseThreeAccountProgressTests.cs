using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证阶段三账号迁移、解锁购买、Seal 容量和角色固有被动。</summary>
    public sealed class PhaseThreeAccountProgressTests : EditModeComponentTestBase
    {
        /// <summary>每项测试使用独立内存账号，避免静态服务状态跨用例泄漏。</summary>
        [SetUp]
        public void ResetAccountProgress()
        {
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>迁移器应修复负数、重复 ID、空 ID 和超出首版容量的 Seal。</summary>
        [Test]
        public void Migrator_异常边界数据_归一化为首版安全账号()
        {
            var data = new AccountProgressData
            {
                saveVersion = 0,
                accountGold = -5,
                lifetimeGoldEarned = -8,
                lifetimeKills = -3,
                sealCapacity = 99,
                unlockedCharacterIds = new List<string> { "", "character_extra", "character_extra" },
                sealedUpgradeIds = new List<string> { "upgrade_a", "upgrade_b" }
            };

            AccountProgressMigrator.MigrateToCurrent(data);

            Assert.That(data.saveVersion, Is.EqualTo(AccountProgressData.CurrentVersion));
            Assert.That(data.accountGold, Is.Zero);
            Assert.That(data.lifetimeGoldEarned, Is.Zero);
            Assert.That(data.lifetimeKills, Is.Zero);
            Assert.That(data.sealCapacity, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(
                new[] { AccountProgressData.DefaultCharacterId, "character_extra" },
                data.unlockedCharacterIds);
            CollectionAssert.AreEqual(new[] { "upgrade_a" }, data.sealedUpgradeIds);
        }

        /// <summary>主档损坏时应读取上一份有效备份，而不是把损坏内容覆盖到备份。</summary>
        [Test]
        public void JsonStorage_主档损坏_恢复上一份有效备份()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "RainsenVampSur-AccountTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new JsonAccountProgressStorage(directory);
                AccountProgressData data = AccountProgressData.CreateDefault();
                data.accountGold = 40;
                Assert.IsTrue(storage.Save(data));

                data.accountGold = 60;
                Assert.IsTrue(storage.Save(data));
                File.WriteAllText(Path.Combine(directory, "account-progress.json"), "{");

                AccountProgressLoadResult recovered = storage.Load();

                Assert.IsFalse(recovered.IsReadOnly);
                Assert.IsTrue(recovered.ShouldPersist);
                Assert.That(recovered.Data.accountGold, Is.EqualTo(40));
                Assert.That(
                    Directory.GetFiles(directory, "account-progress.corrupt-*.json").Length,
                    Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        /// <summary>累计击杀自动解锁与金币购买应分别生效，并且消费不会降低历史累计金币。</summary>
        [Test]
        public void AccountProgress_击杀与金币条件_自动解锁并正确扣款()
        {
            AccountProgressService service = AccountProgressService.Current;
            CharacterDataSO killCharacter = CreateCharacter(
                "character_kills",
                CharacterUnlockConditionType.LifetimeKills,
                100);
            CharacterDataSO goldCharacter = CreateCharacter(
                "character_gold",
                CharacterUnlockConditionType.GoldPurchase,
                30);

            try
            {
                service.RecordRunResults(50, 99);
                Assert.That(
                    service.EvaluateAutomaticUnlocks(new[] { killCharacter, goldCharacter }),
                    Is.Zero);

                service.RecordRunResults(0, 1);
                Assert.That(
                    service.EvaluateAutomaticUnlocks(new[] { killCharacter, goldCharacter }),
                    Is.EqualTo(1));
                Assert.IsTrue(service.IsCharacterUnlocked(killCharacter.characterID));
                Assert.IsFalse(service.IsCharacterUnlocked(goldCharacter.characterID));

                Assert.IsTrue(service.TryPurchaseCharacter(goldCharacter));
                Assert.That(service.Gold, Is.EqualTo(20));
                Assert.That(service.LifetimeGoldEarned, Is.EqualTo(50));
                Assert.IsTrue(service.IsCharacterUnlocked(goldCharacter.characterID));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(killCharacter);
                UnityEngine.Object.DestroyImmediate(goldCharacter);
            }
        }

        /// <summary>首版只能启用一个 Seal，解除后槽位可复用且不会影响单局 Banish 集合。</summary>
        [Test]
        public void Seal_单槽容量_与RunStateBanish保持独立()
        {
            AccountProgressService service = AccountProgressService.Current;
            service.DiscoverUpgrade("upgrade_a");
            service.DiscoverUpgrade("upgrade_b");

            Assert.IsTrue(service.TrySetUpgradeSealed("upgrade_a", true));
            Assert.IsFalse(service.TrySetUpgradeSealed("upgrade_b", true));

            GameObject player = CreateTrackedGameObject("AutomationTest_PhaseThreeRunState");
            PlayerStats stats = player.AddComponent<PlayerStats>();
            RunState runState = RunState.GetOrCreate(stats);
            TestObjectUtility.InvokeNonPublicMethod(runState, "Awake");
            Assert.IsTrue(runState.BanishUpgrade("upgrade_b"));
            Assert.IsTrue(service.IsUpgradeSealed("upgrade_a"));
            Assert.IsTrue(runState.IsBanished("upgrade_b"));

            runState.ResetRun();

            Assert.IsTrue(service.IsUpgradeSealed("upgrade_a"));
            Assert.IsFalse(runState.IsBanished("upgrade_b"));
            Assert.IsTrue(service.TrySetUpgradeSealed("upgrade_a", false));
            Assert.IsTrue(service.TrySetUpgradeSealed("upgrade_b", true));
        }

        /// <summary>候选池应分别过滤跨局 Seal 与本局 Banish，重置本局只恢复 Banish 项。</summary>
        [Test]
        public void LevelUpPool_Seal与Banish_在同一入口分域过滤()
        {
            AccountProgressService service = AccountProgressService.Current;
            service.DiscoverUpgrade("upgrade_sealed");
            Assert.IsTrue(service.TrySetUpgradeSealed("upgrade_sealed", true));

            UpgradeDataSO sealedUpgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            sealedUpgrade.upgradeID = "upgrade_sealed";
            UpgradeDataSO banishedUpgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            banishedUpgrade.upgradeID = "upgrade_banished";
            AbilityDataSO sealedAbility = CreateAbility("ability_sealed");
            AbilityDataSO banishedAbility = CreateAbility("ability_banished");
            sealedUpgrade.abilityToGrant = sealedAbility;
            banishedUpgrade.abilityToGrant = banishedAbility;

            try
            {
                GameObject player = CreateTrackedGameObject("AutomationTest_PhaseThreePoolPlayer");
                PlayerStats stats = player.AddComponent<PlayerStats>();
                player.AddComponent<PlayerHealth>();
                AbilityManager abilityManager = player.AddComponent<AbilityManager>();
                RunState runState = RunState.GetOrCreate(stats);
                TestObjectUtility.InvokeNonPublicMethod(runState, "Awake");
                runState.BanishUpgrade("upgrade_banished");

                GameObject managerObject = CreateTrackedGameObject("AutomationTest_PhaseThreePoolManager");
                LevelUpManager manager = managerObject.AddComponent<LevelUpManager>();
                manager.allAvailableUpgrades = new List<UpgradeDataSO>
                {
                    sealedUpgrade,
                    banishedUpgrade
                };
                TestObjectUtility.SetPrivateField(manager, "_runState", runState);
                TestObjectUtility.SetPrivateField(manager, "_abilityManager", abilityManager);

                List<UpgradeDataSO> blockedPool =
                    TestObjectUtility.InvokeNonPublicMethod<List<UpgradeDataSO>>(
                        manager,
                        "BuildSelectableUpgradePool");
                Assert.That(blockedPool, Is.Empty);

                runState.ResetRun();
                List<UpgradeDataSO> nextRunPool =
                    TestObjectUtility.InvokeNonPublicMethod<List<UpgradeDataSO>>(
                        manager,
                        "BuildSelectableUpgradePool");
                CollectionAssert.AreEqual(new[] { banishedUpgrade }, nextRunPool);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sealedUpgrade);
                UnityEngine.Object.DestroyImmediate(banishedUpgrade);
                UnityEngine.Object.DestroyImmediate(sealedAbility);
                UnityEngine.Object.DestroyImmediate(banishedAbility);
            }
        }

        /// <summary>切换角色时固有被动来源必须被替换，普通重算不能重复叠加。</summary>
        [Test]
        public void PlayerStats_切换角色被动_只保留当前稳定来源()
        {
            CharacterDataSO rerollCharacter = CreateCharacter(
                "character_reroll",
                CharacterUnlockConditionType.None,
                0);
            rerollCharacter.passive.passiveID = "passive_reroll";
            rerollCharacter.passive.modifiers.Add(new PlayerStatModifier(
                PlayerStatType.Reroll,
                PlayerStatModifierMode.Flat,
                1f));

            CharacterDataSO revivalCharacter = CreateCharacter(
                "character_revival",
                CharacterUnlockConditionType.None,
                0);
            revivalCharacter.passive.passiveID = "passive_revival";
            revivalCharacter.passive.modifiers.Add(new PlayerStatModifier(
                PlayerStatType.Revival,
                PlayerStatModifierMode.Flat,
                1f));

            try
            {
                GameObject player = CreateTrackedGameObject("AutomationTest_PhaseThreePassivePlayer");
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(rerollCharacter);
                Assert.That(stats.Reroll, Is.EqualTo(1f));

                stats.SetModifiers("test.recalculate", new[]
                {
                    new PlayerStatModifier(PlayerStatType.Might, PlayerStatModifierMode.Flat, 0.1f)
                });
                Assert.That(stats.Reroll, Is.EqualTo(1f));

                stats.SetCharacterData(revivalCharacter);
                Assert.That(stats.Reroll, Is.Zero);
                Assert.That(stats.Revival, Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rerollCharacter);
                UnityEngine.Object.DestroyImmediate(revivalCharacter);
            }
        }

        /// <summary>同一局的结算入口重复触发时，账号金币与击杀只能入账一次。</summary>
        [Test]
        public void GameFlow_重复提交同一局_账号统计只结算一次()
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_PhaseThreeSettlementPlayer");
            player.tag = "Player";
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            PlayerStats stats = player.AddComponent<PlayerStats>();
            PlayerController controller = player.AddComponent<PlayerController>();
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            RunState runState = RunState.GetOrCreate(stats);
            TestObjectUtility.InvokeNonPublicMethod(runState, "Awake");
            runState.AddGold(7);
            runState.RegisterKill();

            GameObject managerObject = CreateTrackedGameObject("AutomationTest_PhaseThreeSettlementFlow");
            GameFlowManager manager = managerObject.AddComponent<GameFlowManager>();
            TestObjectUtility.SetObjectReference(manager, "playerHealth", health);
            TestObjectUtility.SetObjectReference(manager, "playerController", controller);
            TestObjectUtility.SetObjectReference(manager, "playerRigidbody", body);
            TestObjectUtility.SetPrivateField(manager, "_runState", runState);

            TestObjectUtility.InvokeNonPublicMethod(manager, "CommitRunProgressIfNeeded");
            TestObjectUtility.InvokeNonPublicMethod(manager, "CommitRunProgressIfNeeded");

            Assert.That(AccountProgressService.Current.Gold, Is.EqualTo(7));
            Assert.That(AccountProgressService.Current.LifetimeKills, Is.EqualTo(1));
        }

        /// <summary>创建指定首版解锁条件的临时角色资产。</summary>
        private static CharacterDataSO CreateCharacter(
            string characterId,
            CharacterUnlockConditionType conditionType,
            int requiredAmount)
        {
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            character.characterID = characterId;
            character.unlock.conditionType = conditionType;
            character.unlock.requiredAmount = requiredAmount;
            return character;
        }

        /// <summary>创建具有单级空快照的合法正式能力，用于候选分域过滤。</summary>
        private static AbilityDataSO CreateAbility(string abilityId)
        {
            AbilityDataSO ability = ScriptableObject.CreateInstance<AbilityDataSO>();
            ability.abilityID = abilityId;
            ability.levelConfigs = new List<AbilityLevelData>
            {
                new AbilityLevelData()
            };
            return ability;
        }
    }
}
