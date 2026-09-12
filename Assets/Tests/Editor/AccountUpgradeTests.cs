using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>局外成长配置、事务、迁移和开局惰性属性的隔离回归。</summary>
    public sealed class AccountUpgradeTests
    {
        private AccountUpgradeCatalogSO _catalog;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        /// <summary>复制真实全属性目录，数值变更不污染正式资产。</summary>
        [SetUp]
        public void Setup()
        {
            _catalog = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath));
            _objects.Add(_catalog);
            for (int i = 0; i < _catalog.upgrades.Count; i++)
            {
                _catalog.upgrades[i] = UnityEngine.Object.Instantiate(_catalog.upgrades[i]);
                _objects.Add(_catalog.upgrades[i]);
            }
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>清理隔离资产与账号。</summary>
        [TearDown]
        public void Cleanup()
        {
            foreach (UnityEngine.Object item in _objects) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
            CharacterSelectionSession.Clear();
        }

        /// <summary>逐项逐级购买与退款，验证全 21 项累计配置及上限。</summary>
        [Test]
        public void All21Stats_PurchaseEveryLevel_RefundEveryPayment()
        {
            Assert.That(_catalog.upgrades.Count, Is.EqualTo(21));
            Assert.IsTrue(_catalog.Validate(out _));
            var storage = new InMemoryAccountProgressStorage();
            var service = new AccountProgressService(storage);
            service.RecordRunResults(100000, 0);
            foreach (AccountUpgradeDataSO definition in _catalog.upgrades)
            {
                int start = service.Gold;
                for (int level = 1; level <= definition.maxLevel; level++)
                {
                    Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, definition.stableId));
                    Assert.That(service.GetUpgradeLevel(definition.stableId), Is.EqualTo(level));
                    Assert.That(service.GetLastPaidCost(definition.stableId), Is.EqualTo(definition.levels[level - 1].cost));
                    var snapshot = AccountUpgradeResolver.CreateSnapshot(service, _catalog);
                    Assert.That(snapshot.Count, Is.EqualTo(definition.levels[level - 1].modifiers.Count));
                    Assert.That(snapshot[0].Value, Is.EqualTo(definition.levels[level - 1].modifiers[0].Value));
                }
                Assert.IsFalse(service.TryPurchaseUpgrade(_catalog, definition.stableId));
                Assert.That(new AccountProgressService(storage).GetUpgradeLevel(definition.stableId), Is.EqualTo(definition.maxLevel));
                for (int level = definition.maxLevel; level > 0; level--) Assert.IsTrue(service.TryRefundUpgrade(definition.stableId));
                Assert.IsFalse(service.TryRefundUpgrade(definition.stableId));
                Assert.That(service.Gold, Is.EqualTo(start));
                Assert.That(service.LifetimeGoldEarned, Is.EqualTo(100000));
            }
        }

        /// <summary>配置错误不生成隐藏等级或可购买数据。</summary>
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("nan")]
        [TestCase("negative")]
        [TestCase("stat")]
        [TestCase("mode")]
        [TestCase("slots")]
        public void InvalidConfiguration_RejectsPurchase(string kind)
        {
            AccountUpgradeDataSO definition = _catalog.upgrades[0];
            switch (kind)
            {
                case "missing": definition.levels.RemoveAt(2); break;
                case "duplicate": _catalog.upgrades[1].stableId = definition.stableId; break;
                case "nan": definition.levels[0].modifiers[0] = new PlayerStatModifier(definition.statType, PlayerStatModifierMode.Flat, float.NaN); break;
                case "negative": definition.levels[0].cost = -1; break;
                case "stat": definition.statType = (PlayerStatType)99; break;
                case "mode": definition.levels[0].modifiers[0] = new PlayerStatModifier(definition.statType, (PlayerStatModifierMode)99, 1); break;
                case "slots": _catalog.maxSealSlotLevel = 10; break;
            }
            Assert.IsFalse(_catalog.Validate(out _));
            var service = AccountProgressService.Current;
            service.RecordRunResults(10000, 0);
            Assert.IsFalse(service.TryPurchaseUpgrade(_catalog, definition.stableId));
            Assert.That(service.Gold, Is.EqualTo(10000));
        }

        /// <summary>变价、降上限与移除定义不丢失正常历史实付金额。</summary>
        [Test]
        public void ReducedCapAndChangedPrice_PreservesRefundHistory()
        {
            var service = AccountProgressService.Current;
            service.RecordRunResults(1000, 0);
            var definition = _catalog.upgrades[0];
            for (int i = 0; i < 3; i++) Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, definition.stableId));
            definition.maxLevel = 1;
            definition.levels[0].cost = 999;
            Assert.That(AccountUpgradeResolver.CreateSnapshot(service, _catalog)[0].Value, Is.EqualTo(definition.levels[0].modifiers[0].Value));
            Assert.That(service.GetUpgradeLevel(definition.stableId), Is.EqualTo(3));
            Assert.IsTrue(service.TryRefundUpgrade(definition.stableId));
            Assert.That(service.Gold, Is.EqualTo(700));
            _catalog.upgrades.Remove(definition);
            Assert.IsTrue(service.TryRefundUpgrade(definition.stableId));
            Assert.IsTrue(service.TryRefundUpgrade(definition.stableId));
            Assert.That(service.Gold, Is.EqualTo(1000));
        }

        /// <summary>失败后不改变余额、等级、排除状态或发布 Changed，重载也保持旧快照。</summary>
        [Test]
        public void FailedSave_RollsBackPurchaseRefundAndSeal()
        {
            var storage = new FailingStorage();
            var service = new AccountProgressService(storage);
            service.RecordRunResults(1000, 0);
            service.DiscoverUpgrade("test");
            string id = _catalog.upgrades[0].stableId;
            int events = 0;
            service.Changed += () => events++;
            storage.Fail = true;
            Assert.IsFalse(service.TryPurchaseUpgrade(_catalog, id));
            Assert.IsFalse(service.TrySetUpgradeSealed("test", true));
            Assert.That(events, Is.Zero);
            Assert.That(service.Gold, Is.EqualTo(1000));
            Assert.That(service.GetUpgradeLevel(id), Is.Zero);
            Assert.IsNotEmpty(service.LastTransactionError);
            storage.Fail = false;
            Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, id));
            storage.Fail = true;
            Assert.IsFalse(service.TryRefundUpgrade(id));
            Assert.That(events, Is.EqualTo(1));
            Assert.That(service.Gold, Is.EqualTo(900));
            Assert.That(new AccountProgressService(storage).GetUpgradeLevel(id), Is.EqualTo(1));
        }

        /// <summary>不足、只读、零级与退款溢出均拒绝且不饱和吞掉退款金额。</summary>
        [Test]
        public void TransactionBoundaries_RejectWithoutMutation()
        {
            string id = _catalog.upgrades[0].stableId;
            var service = AccountProgressService.Current;
            Assert.IsFalse(service.TryPurchaseUpgrade(_catalog, id));
            Assert.IsFalse(service.TryRefundUpgrade(id));
            service.RecordRunResults(100, 0);
            Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, id));
            service.RecordRunResults(int.MaxValue, 0);
            Assert.IsFalse(service.TryRefundUpgrade(id));
            Assert.That(service.GetUpgradeLevel(id), Is.EqualTo(1));
            var readOnly = new FailingStorage { ReadOnly = true };
            var protectedService = new AccountProgressService(readOnly);
            Assert.IsFalse(protectedService.TryPurchaseUpgrade(_catalog, id));
            Assert.IsFalse(protectedService.TryRefundUpgrade(id));
        }

        /// <summary>额外槽按实付买退，容量不足时必须先解封，且只允许已发现项目。</summary>
        [Test]
        public void SealSlots_PaidHistoryAndOccupiedRefundGuard()
        {
            var service = AccountProgressService.Current;
            service.RecordRunResults(1000, 0);
            Assert.IsFalse(service.TrySetUpgradeSealed("unknown", true));
            service.DiscoverUpgrade("a"); service.DiscoverUpgrade("b");
            Assert.IsTrue(service.TrySetUpgradeSealed("a", true));
            Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, AccountUpgradeCatalogSO.SealSlotId));
            Assert.IsTrue(service.TrySetUpgradeSealed("b", true));
            Assert.IsFalse(service.TryRefundUpgrade(AccountUpgradeCatalogSO.SealSlotId));
            Assert.That(service.ActiveSealCount, Is.EqualTo(2));
            Assert.IsTrue(service.TrySetUpgradeSealed("a", false));
            _catalog.sealSlotCosts[0] = 999;
            Assert.IsTrue(service.TryRefundUpgrade(AccountUpgradeCatalogSO.SealSlotId));
            Assert.That(service.SealCapacity, Is.EqualTo(1));
            Assert.That(service.Gold, Is.EqualTo(1000));
        }

        /// <summary>旧档迁移保留账号字段，异常重复记录保留但不生效、不产生退款。</summary>
        [Test]
        public void MigrationAndAbnormalHistory_PreservesNormalRecords()
        {
            var data = AccountProgressData.CreateDefault();
            data.saveVersion = 1; data.accountGold = 77; data.discoveredUpgradeIds.Add("known"); data.sealedUpgradeIds.Add("known");
            AccountProgressMigrator.MigrateToCurrent(data);
            Assert.That(data.accountGold, Is.EqualTo(77)); Assert.That(data.upgradePurchases.Count, Is.Zero);
            Assert.That(data.sealedUpgradeIds, Does.Contain("known"));
            string id = _catalog.upgrades[0].stableId;
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = id, paidCosts = new List<int> { 100, 200 } });
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = "bad", paidCosts = new List<int> { -2 } });
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = "dup", paidCosts = new List<int> { 20 } });
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = "dup", paidCosts = new List<int> { 30 } });
            AccountProgressMigrator.Normalize(data);
            Assert.That(data.upgradePurchases.Count, Is.EqualTo(4));
            Assert.That(AccountProgressMigrator.FindValidPurchase(data, id).paidCosts.Count, Is.EqualTo(2));
            Assert.IsNull(AccountProgressMigrator.FindValidPurchase(data, "dup"));
            Assert.IsNull(AccountProgressMigrator.FindValidPurchase(data, "bad"));
        }

        /// <summary>真实 JSON 主档/备份往返保留实付列表，未来版本备份禁止覆盖。</summary>
        [Test]
        public void JsonStorage_UpgradeRoundTripAndFutureBackupProtection()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Session22-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new JsonAccountProgressStorage(directory);
                var service = new AccountProgressService(storage);
                service.RecordRunResults(1000, 0);
                string id = _catalog.upgrades[0].stableId;
                Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, id));
                Assert.That(new AccountProgressService(storage).GetLastPaidCost(id), Is.EqualTo(100));
                Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, id));
                File.WriteAllText(Path.Combine(directory, "account-progress.json"), "{");
                Assert.That(storage.Load().Data.upgradePurchases[0].paidCosts.Count, Is.EqualTo(1));
                File.WriteAllText(Path.Combine(directory, "account-progress.json.bak"), "{\"saveVersion\":999,\"accountGold\":99}");
                Assert.IsTrue(storage.Load().IsReadOnly);
                File.WriteAllText(Path.Combine(directory, "account-progress.json"), "{\"saveVersion\":2,\"accountGold\":10}");
                Assert.IsTrue(storage.Load().IsReadOnly);
                Assert.IsFalse(storage.Save(AccountProgressData.CreateDefault()));
                Assert.That(File.ReadAllText(Path.Combine(directory, "account-progress.json.bak")), Does.Contain("999"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        /// <summary>惰性初始化先叠局外快照，之后重算不重复，也不刷新为菜单后来购买的等级。</summary>
        [Test]
        public void LazyStats_AccountAndPassiveAndAbilityComposeOnce()
        {
            var service = AccountProgressService.Current;
            service.RecordRunResults(1000, 0);
            string id = _catalog.upgrades[0].stableId;
            service.TryPurchaseUpgrade(_catalog, id);
            var player = new GameObject("AccountUpgradeTest"); _objects.Add(player);
            player.SetActive(false);
            var stats = player.AddComponent<PlayerStats>();
            var serialized = new SerializedObject(stats);
            serialized.FindProperty("accountUpgradeCatalog").objectReferenceValue = _catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character);
            character.characterID = "account_test_character";
            character.passive = new CharacterPassiveDefinition { modifiers = new List<PlayerStatModifier> {
                new PlayerStatModifier(PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 20) } };
            stats.SetCharacterData(character);
            float first = stats.MaxHealth;
            Assert.That(first, Is.EqualTo(130));
            stats.SetModifiers("ability", new[] { new PlayerStatModifier(PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 5) });
            Assert.That(stats.MaxHealth, Is.EqualTo(first + 5));
            service.TryPurchaseUpgrade(_catalog, id);
            stats.SetCharacterData(character);
            Assert.That(stats.MaxHealth, Is.EqualTo(first + 5));
        }

        /// <summary>倍率 Flat 使用百分点，概率与恢复分别带正确单位。</summary>
        [TestCase(PlayerStatType.Might, 0.05f, "+5 个百分点")]
        [TestCase(PlayerStatType.Defang, 0.03f, "+3 个百分点")]
        [TestCase(PlayerStatType.Recovery, 0.1f, "+0.1/秒")]
        public void EffectText_ReflectsStatUnits(PlayerStatType stat, float value, string expected)
        {
            var definition = _catalog.upgrades.Find(item => item.statType == stat);
            definition.levels[0].modifiers[0] = new PlayerStatModifier(stat, PlayerStatModifierMode.Flat, value);
            var method = typeof(AccountShopUI).GetMethod("FormatEffect", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method.Invoke(null, new object[] { definition, 1 }), Is.EqualTo(expected));
        }

        /// <summary>可注入保存失败的内存存储，用于验证交易发布顺序。</summary>
        private sealed class FailingStorage : IAccountProgressStorage
        {
            private readonly InMemoryAccountProgressStorage _inner = new InMemoryAccountProgressStorage();
            public bool Fail;
            public bool ReadOnly;
            /// <summary>返回隔离快照或只读状态。</summary>
            public AccountProgressLoadResult Load() { var result = _inner.Load(); return new AccountProgressLoadResult(result.Data, false, ReadOnly, ""); }
            /// <summary>失败时不写入后端。</summary>
            public bool Save(AccountProgressData data) { return !Fail && _inner.Save(data); }
        }
    }
}
