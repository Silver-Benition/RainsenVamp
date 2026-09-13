using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>账号启用偏好的迁移、持久化、事务与来源隔离验证；不接触真实用户存档。</summary>
    public sealed class AccountUpgradePreferenceTests
    {
        private AccountUpgradeCatalogSO _catalog;
        private string _directory;
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        /// <summary>读取正式目录并为每个测试创建独立临时存储。</summary>
        [SetUp]
        public void Setup()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath);
            _directory = Path.Combine(Path.GetTempPath(), "Session22Preferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>清理本测试拥有的对象、唯一临时目录和静态服务。</summary>
        [TearDown]
        public void Cleanup()
        {
            foreach (UnityEngine.Object item in _objects) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>旧正式v1/v2迁移默认全部启用，v2的购买与历史实付原样保留。</summary>
        [TestCase(1)]
        [TestCase(2)]
        public void LegacySave_DefaultsEnabledAndPreservesV2History(int version)
        {
            var data = AccountProgressData.CreateDefault();
            data.saveVersion = version;
            data.accountGold = 77;
            data.disabledAccountUpgradeIds.Add("account_might");
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = "account_might", paidCosts = new List<int> { 73 } });
            File.WriteAllText(Path.Combine(_directory, "account-progress.json"), JsonUtility.ToJson(data));
            var storage = new JsonAccountProgressStorage(_directory);
            AccountProgressLoadResult loaded = storage.Load();
            Assert.That(loaded.Data.saveVersion, Is.EqualTo(3));
            Assert.IsEmpty(loaded.Data.disabledAccountUpgradeIds);
            Assert.That(loaded.Data.accountGold, Is.EqualTo(77));
            Assert.That(loaded.Data.upgradePurchases.Count, Is.EqualTo(version == 2 ? 1 : 0));
            if (version == 2) Assert.That(loaded.Data.upgradePurchases[0].paidCosts[0], Is.EqualTo(73));
            Assert.IsTrue(storage.Save(loaded.Data));
            Assert.IsEmpty(storage.Load().Data.disabledAccountUpgradeIds);
        }

        /// <summary>零级偏好跨JSON重载持久化；买退保留偏好和实付权益，恢复不增加购买等级。</summary>
        [Test]
        public void Preference_ReloadPurchaseRefundAndRepurchase_PreservesRights()
        {
            var storage = new JsonAccountProgressStorage(_directory);
            var service = new AccountProgressService(storage);
            Assert.IsTrue(service.IsAccountUpgradeEnabled("account_might"));
            service.RecordRunResults(1000, 0);
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_might", false));
            service = new AccountProgressService(storage);
            Assert.IsFalse(service.IsAccountUpgradeEnabled("account_might"));
            Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, "account_might"));
            Assert.That(service.GetLastPaidCost("account_might"), Is.EqualTo(100));
            Assert.IsEmpty(AccountUpgradeResolver.CreateSnapshot(service, _catalog));
            Assert.IsTrue(service.TryRefundUpgrade("account_might"));
            Assert.That(service.Gold, Is.EqualTo(1000));
            Assert.IsFalse(new AccountProgressService(storage).IsAccountUpgradeEnabled("account_might"));
            Assert.IsTrue(service.TryPurchaseUpgrade(_catalog, "account_might"));
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_might", true));
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_might", true));
            Assert.That(AccountUpgradeResolver.CreateSnapshot(service, _catalog).Count, Is.EqualTo(1));
            Assert.That(service.GetUpgradeLevel("account_might"), Is.EqualTo(1));
            Assert.That(service.Gold, Is.EqualTo(900));
        }

        /// <summary>保存失败或抛异常时，不改变偏好、不发布事件，也不吞掉原有金币和实付记录。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FailedSave_DoesNotPublishOrMutate(bool throws)
        {
            var storage = new FailingStorage();
            var service = new AccountProgressService(storage);
            service.RecordRunResults(1000, 0);
            service.TryPurchaseUpgrade(_catalog, "account_might");
            int events = 0; service.Changed += () => events++;
            storage.Fail = true; storage.Throws = throws;
            Assert.IsFalse(service.TrySetAccountUpgradeEnabled(_catalog, "account_might", false));
            Assert.IsTrue(service.IsAccountUpgradeEnabled("account_might"));
            Assert.That(events, Is.Zero);
            Assert.That(service.LastTransactionError, Does.Contain("保存失败"));
            Assert.That(service.Gold, Is.EqualTo(900));
            storage.Fail = false; storage.Throws = false;
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_might", false));
            Assert.That(events, Is.EqualTo(1));
            Assert.IsFalse(new AccountProgressService(storage).IsAccountUpgradeEnabled("account_might"));
        }

        /// <summary>只读服务拒绝启用事务，不能通过免费切换绕开未来存档保护。</summary>
        [Test]
        public void ReadonlyService_RejectsPreferenceChange()
        {
            var storage = new FailingStorage { ReadOnly = true };
            var service = new AccountProgressService(storage);
            int events = 0; service.Changed += () => events++;
            Assert.IsFalse(service.TrySetAccountUpgradeEnabled(_catalog, "account_revival", false));
            Assert.IsTrue(service.IsAccountUpgradeEnabled("account_revival"));
            Assert.That(events, Is.Zero);
            Assert.That(service.LastTransactionError, Does.Contain("只读"));
        }

        /// <summary>四类主动资源无开关且拒绝API停用；归一化清除伪造、空白及重复禁用ID。</summary>
        [TestCase("account_seal_slots")]
        [TestCase("account_reroll")]
        [TestCase("account_skip")]
        [TestCase("account_banish")]
        public void ActiveResources_RejectDisableAndInjectedPreferences(string id)
        {
            var data = AccountProgressData.CreateDefault();
            data.disabledAccountUpgradeIds = new List<string> { " " + id + " ", id, null, " ", " account_revival ", "account_revival" };
            var storage = new InMemoryAccountProgressStorage(); storage.Save(data);
            var service = new AccountProgressService(storage);
            Assert.IsTrue(service.IsAccountUpgradeEnabled(id));
            Assert.IsFalse(service.TrySetAccountUpgradeEnabled(_catalog, id, false));
            if (id != AccountUpgradeCatalogSO.SealSlotId) Assert.IsFalse(_catalog.Find(id).CanToggleEnabled);
            Assert.IsFalse(service.IsAccountUpgradeEnabled("account_revival"));
            AccountProgressMigrator.Normalize(data);
            Assert.That(data.disabledAccountUpgradeIds, Is.EqualTo(new[] { "account_revival" }));
        }

        /// <summary>只过滤下一局账号来源：旧玩家、角色被动及其他来源不变；恢复后新玩家恰好一份加成。</summary>
        [Test]
        public void Snapshot_OnlyNextPlayerChanges_CharacterAndOtherSourcesRemain()
        {
            var service = AccountProgressService.Current;
            service.RecordRunResults(1000, 0);
            service.TryPurchaseUpgrade(_catalog, "account_maxhealth");
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character);
            character.characterID = "preference_character";
            character.baseStats.maxHealth = 120;
            character.passive.modifiers.Add(new PlayerStatModifier(PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 20));
            PlayerStats original = CreatePlayer(character);
            original.SetModifiers("run.extra", new[] { new PlayerStatModifier(PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 7) });
            Assert.That(original.MaxHealth, Is.EqualTo(157));
            service.TrySetAccountUpgradeEnabled(_catalog, "account_maxhealth", false);
            Assert.That(original.MaxHealth, Is.EqualTo(157));
            original.SetCharacterData(character);
            Assert.That(original.MaxHealth, Is.EqualTo(157));
            Assert.That(CreatePlayer(character).MaxHealth, Is.EqualTo(140));
            service.TrySetAccountUpgradeEnabled(_catalog, "account_maxhealth", true);
            service.TrySetAccountUpgradeEnabled(_catalog, "account_maxhealth", true);
            Assert.That(CreatePlayer(character).MaxHealth, Is.EqualTo(150));
            Assert.That(original.MaxHealth, Is.EqualTo(157));
        }

        /// <summary>复活可停用，只去除账号复活次数；四类主动资源的合法购买仍进入快照。</summary>
        [Test]
        public void RevivalCanDisable_ActiveResourceSnapshotsAlwaysRemain()
        {
            var service = AccountProgressService.Current;
            service.RecordRunResults(2000, 0);
            foreach (string id in new[] { "account_revival", "account_reroll", "account_skip", "account_banish" }) service.TryPurchaseUpgrade(_catalog, id);
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_revival", false));
            var modifiers = AccountUpgradeResolver.CreateSnapshot(service, _catalog);
            Assert.IsFalse(modifiers.Exists(value => value.StatType == PlayerStatType.Revival));
            Assert.That(modifiers.Count, Is.EqualTo(3));
            Assert.IsTrue(service.TrySetAccountUpgradeEnabled(_catalog, "account_revival", true));
            Assert.That(AccountUpgradeResolver.CreateSnapshot(service, _catalog).Count, Is.EqualTo(4));
        }

        /// <summary>v3客户端不覆盖v4主档或备份，启用偏好引入后保护边界仍有效。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FutureV4_PrimaryOrBackupRemainProtected(bool backup)
        {
            string path = Path.Combine(_directory, "account-progress.json" + (backup ? ".bak" : ""));
            const string future = "{\"saveVersion\":4,\"accountGold\":77}";
            File.WriteAllText(path, future);
            var storage = new JsonAccountProgressStorage(_directory);
            Assert.IsTrue(storage.Load().IsReadOnly);
            Assert.IsFalse(storage.Save(AccountProgressData.CreateDefault()));
            Assert.That(File.ReadAllText(path), Is.EqualTo(future));
        }

        /// <summary>描述资产不再使用通用句；确认易误解的特殊属性说明和可配置本地化入口。</summary>
        [Test]
        public void Descriptions_ExplainImplementedEffectsAndRemainConfigurable()
        {
            var texts = new HashSet<string>();
            foreach (AccountUpgradeDataSO definition in _catalog.upgrades)
            {
                Assert.IsNotEmpty(definition.descriptionKey);
                Assert.That(definition.GetDescription(), Does.StartWith("每强化一级"));
                Assert.That(definition.GetDescription(), Does.Not.Contain("所有角色开局"));
                Assert.IsTrue(texts.Add(definition.GetDescription()));
            }
            Assert.That(_catalog.Find("account_revival").GetDescription(), Does.Contain("50%最大生命"));
            Assert.That(_catalog.Find("account_defang").GetDescription(), Does.Contain("可被削弱的普通敌人"));
            Assert.That(_catalog.Find("account_charm").GetDescription(), Does.Contain("上限"));
        }

        /// <summary>创建带正式目录引用的惰性玩家；首次读取时才捕获账号来源。</summary>
        private PlayerStats CreatePlayer(CharacterDataSO character)
        {
            var owner = new GameObject("PreferencePlayer"); owner.SetActive(false); _objects.Add(owner);
            PlayerStats player = owner.AddComponent<PlayerStats>();
            var serialized = new SerializedObject(player);
            serialized.FindProperty("accountUpgradeCatalog").objectReferenceValue = _catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            player.SetCharacterData(character);
            return player;
        }

        /// <summary>可切换失败方式的内存后端，用于核验事务边界。</summary>
        private sealed class FailingStorage : IAccountProgressStorage
        {
            private readonly InMemoryAccountProgressStorage _inner = new InMemoryAccountProgressStorage();
            public bool Fail;
            public bool Throws;
            public bool ReadOnly;
            /// <summary>读取独立内存快照。</summary>
            public AccountProgressLoadResult Load() { var loaded = _inner.Load(); return new AccountProgressLoadResult(loaded.Data, loaded.ShouldPersist, ReadOnly, ""); }
            /// <summary>模拟写失败或异常，不修改已有快照。</summary>
            public bool Save(AccountProgressData data) { if (Throws) throw new IOException("test"); return !Fail && _inner.Save(data); }
        }
    }
}
