using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>新体系可观察数值、武器独立属性与历史账号权益的迁移回归。</summary>
    public sealed class BrotatoStatMigrationTests
    {
        private readonly List<Object> _objects = new List<Object>();
        /// <summary>每个用例使用内存账号，不读写用户存档。</summary>
        [SetUp] public void Setup()
        { AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); CharacterSelectionSession.Clear(); }
        /// <summary>销毁测试独占对象并清理静态账号。</summary>
        [TearDown] public void Cleanup()
        { for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]); _objects.Clear(); AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); CharacterSelectionSession.Clear(); }
        /// <summary>创建显式启用新规则的独立角色。</summary>
        private PlayerStats Player()
        {
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character);
            character.useBrotatoStats = true; character.baseStats.maxHealth = 10;
            var go = new GameObject("BrotatoStatTest"); _objects.Add(go);
            PlayerStats stats = go.AddComponent<PlayerStats>(); stats.SetCharacterData(character); return stats;
        }
        /// <summary>EditMode 不自动驱动组件生命周期，显式初始化生命并订阅升级通知。</summary>
        private static PlayerHealth Health(PlayerStats stats)
        {
            PlayerHealth health = stats.gameObject.AddComponent<PlayerHealth>();
            TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(health, "OnEnable");
            return health;
        }
        /// <summary>替换测试来源，以模拟道具或角色被动的正负组合。</summary>
        private static void Set(PlayerStats stats, PlayerStatType stat, float value)
        { stats.SetModifiers("test." + stat, new[] { new PlayerStatModifier(stat, PlayerStatModifierMode.Flat, value) }); }

        /// <summary>连续升级逐级补充当前生命，其他最大生命来源不会被误当作治疗。</summary>
        [Test] public void LevelHealth_RestoresOnePerLevel_OnlyForLevelRewards()
        {
            PlayerStats stats = Player(); var health = Health(stats);
            health.TakeDamage(5); Assert.AreEqual(5, health.CurrentHealth);
            stats.AddExp(41); Assert.AreEqual(12, health.MaxHealth); Assert.AreEqual(7, health.CurrentHealth);
            Set(stats, PlayerStatType.MaxHealth, 5); Assert.AreEqual(17, health.MaxHealth); Assert.AreEqual(7, health.CurrentHealth);
            Set(stats, PlayerStatType.Armor, 1); Assert.AreEqual(7, health.CurrentHealth);
        }
        /// <summary>结算事务延迟属性与升级通知时，各级治疗仍只执行一次。</summary>
        [Test] public void DeferredLevelHealth_RestoresAfterCommit_WithoutDuplicates()
        {
            PlayerStats stats = Player(); var health = Health(stats); health.TakeDamage(5);
            var type = typeof(PlayerStats).Assembly.GetType("RunTransactionEvents");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            type.GetMethod("Begin", flags).Invoke(null, null);
            try { stats.AddExp(41); Assert.AreEqual(5, health.CurrentHealth); }
            finally { type.GetMethod("End", flags).Invoke(null, new object[] { true }); }
            Assert.AreEqual(12, health.MaxHealth); Assert.AreEqual(7, health.CurrentHealth);
        }
        /// <summary>回合默认回满；覆盖只用一次且通知前消费，不能借此复活死亡角色。</summary>
        [Test] public void RoundHealth_OverrideIsOneShot_ClampedAndCannotRevive()
        {
            PlayerStats stats = Player(); var health = Health(stats); health.TakeDamage(4);
            Assert.IsTrue(health.SetNextRoundHealth(3)); health.PrepareRound(); Assert.AreEqual(3, health.CurrentHealth);
            health.PrepareRound(); Assert.AreEqual(10, health.CurrentHealth);
            Assert.IsFalse(health.SetNextRoundHealth(float.NaN)); Assert.IsFalse(health.SetNextRoundHealth(-1));
            health.SetNextRoundHealth(999); health.PrepareRound(); Assert.AreEqual(10, health.CurrentHealth);
            health.SetNextRoundHealth(2); health.ClearNextRoundHealth(); health.PrepareRound(); Assert.AreEqual(10, health.CurrentHealth);
            health.TakeDamage(99); Assert.IsTrue(health.IsDead);
            health.SetNextRoundHealth(5); health.PrepareRound(); Assert.IsTrue(health.IsDead); Assert.AreEqual(0, health.CurrentHealth);
        }
        /// <summary>健康事件中的重入不会触发第二次回满，也不会丢掉为下一波新设的覆盖。</summary>
        [Test] public void RoundHealth_ReentrantNotification_PreservesNextRequest()
        {
            PlayerStats stats = Player(); var health = Health(stats);
            int calls = 0;
            System.Action<float, float> handler = (current, max) => { calls++; health.SetNextRoundHealth(2); health.PrepareRound(); };
            health.HealthChanged += handler; health.SetNextRoundHealth(3); health.PrepareRound();
            Assert.AreEqual(1, calls); Assert.AreEqual(3, health.CurrentHealth);
            health.HealthChanged -= handler; health.PrepareRound(); Assert.AreEqual(2, health.CurrentHealth);
        }

        /// <summary>医疗武器先与角色负值合并，普通武器独立截断，不污染角色原始值。</summary>
        [TestCase(20, .7f)] [TestCase(-20, .3f)] [TestCase(-60, 0)] [TestCase(100, 1)]
        public void WeaponLifeSteal_MergesSignedPlayerBeforeClamp(float playerValue, float expected)
        {
            PlayerStats stats = Player(); Set(stats, PlayerStatType.LifeSteal, playerValue);
            var medical = new WeaponLevelData { lifeSteal = 50, critChance = 30 };
            var normal = new WeaponLevelData();
            var medicalHit = new WeaponHitSnapshot(stats, null, medical);
            var normalHit = new WeaponHitSnapshot(stats, null, normal);
            Assert.That(medicalHit.LifeStealChance, Is.EqualTo(expected).Within(.0001f));
            Assert.That(normalHit.LifeStealChance, Is.EqualTo(Mathf.Clamp01(playerValue / 100)).Within(.0001f));
            Assert.That(stats.GetFinalStat(PlayerStatType.LifeSteal), Is.EqualTo(playerValue));
            medical.lifeSteal = 100;
            Assert.That(medicalHit.LifeStealChance, Is.EqualTo(expected).Within(.0001f), "已发出的快照不得随配置变化。");
        }
        /// <summary>暴击、伤害系数与百分比各自保留负值直到最终运算。</summary>
        [Test] public void WeaponDamageAndCritical_UseIndependentCoefficients()
        {
            PlayerStats stats = Player(); Set(stats, PlayerStatType.RangedDamage, 8); Set(stats, PlayerStatType.DamagePercent, 25); Set(stats, PlayerStatType.CritChance, -20);
            var weapon = new WeaponLevelData { damage = 10, rangedScaling = .5f, critChance = 30 };
            Assert.That(BrotatoStatRules.Damage(weapon, stats), Is.EqualTo(17));
            Assert.That(new WeaponHitSnapshot(stats, null, weapon).CriticalChance, Is.EqualTo(.1f).Within(.0001f));
            Set(stats, PlayerStatType.RangedDamage, -100); Assert.That(BrotatoStatRules.Damage(weapon, stats), Is.EqualTo(1));
        }
        /// <summary>停用属性不能由任何来源再次激活；有效新属性保留低于零的真实总值。</summary>
        [Test] public void LegacyModifiers_NeutralizedWhileNegativeStatsSurvive()
        {
            PlayerStats stats = Player();
            foreach (PlayerStatType old in new[] { PlayerStatType.Curse, PlayerStatType.Might, PlayerStatType.Area, PlayerStatType.Duration, PlayerStatType.Cooldown })
            { Set(stats, old, 100); Assert.That(stats.GetFinalStat(old), Is.EqualTo(1)); }
            foreach (PlayerStatType old in new[] { PlayerStatType.Amount, PlayerStatType.Defang, PlayerStatType.Charm, PlayerStatType.Engineering })
            { Set(stats, old, 100); Assert.That(stats.GetFinalStat(old), Is.Zero); }
            Set(stats, PlayerStatType.Dodge, -40); Assert.That(stats.GetFinalStat(PlayerStatType.Dodge), Is.EqualTo(-40));
            Set(stats, PlayerStatType.MaxHealth, -100); Assert.That(stats.MaxHealth, Is.EqualTo(1));
            Assert.That(stats.GetFinalStat(PlayerStatType.MaxHealth), Is.EqualTo(-90));
        }
        /// <summary>护甲正负边界、再生和范围行为匹配设计样本。</summary>
        [Test] public void DefensiveAndRangeRules_MatchReferenceExamples()
        {
            Assert.That(BrotatoStatRules.ArmorMultiplier(15), Is.EqualTo(.5f));
            Assert.That(BrotatoStatRules.ArmorMultiplier(-15), Is.EqualTo(1.5f));
            Assert.That(BrotatoStatRules.RegenerationInterval(1), Is.EqualTo(5));
            Assert.That(BrotatoStatRules.RegenerationInterval(10), Is.EqualTo(1));
            Assert.That(BrotatoStatRules.RegenerationInterval(-10), Is.EqualTo(float.PositiveInfinity));
            Assert.That(BrotatoStatRules.WeaponRange(2, 50, -100, false), Is.EqualTo(1.5f));
            Assert.That(BrotatoStatRules.WeaponRange(2, 50, -100, true), Is.EqualTo(1.75f));
            Assert.That(BrotatoStatRules.WeaponRange(2, 0, -10000, false), Is.EqualTo(.25f));
        }
        /// <summary>停用历史购买不进入快照、不能重买/启用，但可按原实付金额逐级退款。</summary>
        [Test] public void ArchivedPurchase_PreservesRefundAndNeverApplies()
        {
            var data = AccountProgressData.CreateDefault(); data.accountGold = 500;
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = "account_curse", paidCosts = new List<int> { 73, 119 } });
            var storage = new InMemoryAccountProgressStorage(); Assert.IsTrue(storage.Save(data));
            var service = new AccountProgressService(storage);
            var catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>("Assets/Data/AccountUpgrades/AccountUpgradeCatalog.asset");
            Assert.IsTrue(catalog.Validate(out string error), error);
            Assert.IsFalse(service.TryPurchaseUpgrade(catalog, "account_curse"));
            Assert.IsFalse(service.TrySetAccountUpgradeEnabled(catalog, "account_curse", true));
            Assert.IsEmpty(AccountUpgradeResolver.CreateSnapshot(service, catalog));
            Assert.IsTrue(service.TryRefundUpgrade("account_curse")); Assert.That(service.Gold, Is.EqualTo(619));
            Assert.That(new AccountProgressService(storage).GetLastPaidCost("account_curse"), Is.EqualTo(73));
        }
        /// <summary>正式配置只提供可用新属性，四档非线性表与 UI/领取共用入口。</summary>
        [Test] public void ProductionCatalogs_UseAvailableStatsAndExplicitTiers()
        {
            var shop = AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
            Assert.IsTrue(shop.Validate(out string error), error); Assert.That(shop.stats.Count, Is.EqualTo(15));
            foreach (RoundStatUpgrade entry in shop.stats)
            { Assert.IsTrue(BrotatoStatRules.IsAvailable(entry.modifier.StatType)); Assert.That(entry.tierValues.Length, Is.EqualTo(4)); }
            RoundStatUpgrade damage = shop.stats.Find(x => x.modifier.StatType == PlayerStatType.DamagePercent);
            Assert.That(damage.AtTier(2).Value, Is.EqualTo(8)); Assert.That(damage.AtTier(4).Value, Is.EqualTo(16));
            foreach (RunShopProduct product in shop.products)
                if (!product.IsWeapon) Assert.IsTrue(product.content.abilityToGrant.IsAvailableInBrotato(), product.Name);
        }
        /// <summary>暴击实际改变伤害收据；目标拒绝命中时不伪造有效伤害。</summary>
        [Test] public void CriticalSnapshot_OnlyAcceptedHitsProduceDamage()
        {
            PlayerStats stats = Player(); Set(stats, PlayerStatType.CritChance, -20);
            var weapon = new WeaponLevelData { critChance = 120, critMultiplier = 2 };
            var target = new ReceiptTarget();
            var snapshot = new WeaponHitSnapshot(stats, null, weapon);
            CombatDamageResult result = snapshot.Apply(target, 7, null);
            Assert.That(result.AppliedDamage, Is.EqualTo(14)); Assert.IsTrue(target.Critical);
            target.Accept = false; Assert.IsFalse(snapshot.Apply(target, 7, null).Accepted);
        }
        /// <summary>无场景副作用的目标收据夹具。</summary>
        private sealed class ReceiptTarget : IDamageable, ICombatDamageTarget
        {
            public bool Accept = true; public bool Critical;
            /// <summary>兼容基础伤害接口。</summary>
            public void TakeDamage(float damage) { TakeDamage(damage, false); }
            /// <summary>记录暴击标记。</summary>
            public void TakeDamage(float damage, bool critical) { Critical = critical; }
            /// <summary>按显式接受状态返回真实收据。</summary>
            public CombatDamageResult ApplyCombatDamage(float damage, bool critical)
            { Critical = critical; return new CombatDamageResult(damage, Accept ? damage : 0, Accept ? damage : 0, Accept, false); }
        }
        /// <summary>幸运品质有波次门槛和上限，负幸运与掉落递减都由确定性样本验证。</summary>
        [Test] public void LuckRarityAndDrops_RespectSignedInputsAndCaps()
        {
            Assert.That(BrotatoStatRules.RollTier(1, 1000, 0), Is.EqualTo(1));
            Assert.That(BrotatoStatRules.RollTier(2, 0, .059f), Is.EqualTo(2));
            Assert.That(BrotatoStatRules.RollTier(2, -50, .04f), Is.EqualTo(1));
            Assert.That(BrotatoStatRules.RollTier(20, -100, 0), Is.EqualTo(1));
            Assert.That(BrotatoStatRules.RollTier(100, 10000, .081f), Is.EqualTo(3));
            Assert.That(BrotatoStatRules.GuaranteedUpgradeTier(5), Is.EqualTo(2));
            Assert.That(BrotatoStatRules.GuaranteedUpgradeTier(15), Is.EqualTo(3));
            Assert.That(BrotatoStatRules.GuaranteedUpgradeTier(25), Is.EqualTo(4));
            Assert.That(BrotatoStatRules.DropChance(.2f, 50, 1), Is.EqualTo(.15f).Within(.0001f));
        }

        /// <summary>新等级曲线以现有一级起点计算；连续升级只增加对应生命，不重复叠加。</summary>
        [Test] public void Experience_GrantsOneHealthPerLevelAndDoesNotDelevel()
        {
            PlayerStats stats = Player(); stats.currentExp = 0; stats.expToNextLevel = stats.GetExperienceRequiredForLevel(1);
            stats.AddExp(41); Assert.That(stats.currentLevel, Is.EqualTo(3)); Assert.That(stats.MaxHealth, Is.EqualTo(12));
            stats.LoseExperience(999); Assert.That(stats.currentLevel, Is.EqualTo(3)); Assert.That(stats.currentExp, Is.Zero);
        }
    }
}
