using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>审阅值、交易边界、机制时序和正式内容引用的回归验证。</summary>
    public sealed class ContentExpansionTests
    {
        private GameObject _player, _roundObject;
        private CharacterDataSO _character;
        private PlayerStats _stats;
        private PlayerHealth _health;
        private AbilityManager _items;
        private RoundController _round;
        private RoundRunConfigSO _config;

        /// <summary>建立内存账号与独立角色，显式驱动 EditMode 所需生命周期。</summary>
        [SetUp] public void Setup()
        {
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); CharacterSelectionSession.Clear();
            _character = ScriptableObject.CreateInstance<CharacterDataSO>();
            _character.useBrotatoStats = true; _character.baseStats.maxHealth = 100;
            _player = new GameObject("ExpansionTest"); _stats = _player.AddComponent<PlayerStats>(); _stats.SetCharacterData(_character);
            _health = _player.AddComponent<PlayerHealth>(); Invoke(_health, "Awake"); Invoke(_health, "OnEnable");
            typeof(PlayerHealth).GetField("invulnerabilityDuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_health, 0f);
            _items = _player.AddComponent<AbilityManager>();
            _roundObject = new GameObject("ExpansionRounds"); _round = _roundObject.AddComponent<RoundController>(); Invoke(_round, "Awake");
            _config = ScriptableObject.CreateInstance<RoundRunConfigSO>(); _round.config = _config;
            SetRound(1, RoundPhase.Combat);
        }

        /// <summary>销毁机制拥有者后清理回合和静态账号，避免测试之间共享状态。</summary>
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(_player); Object.DestroyImmediate(_roundObject);
            Object.DestroyImmediate(_character); Object.DestroyImmediate(_config);
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>不限叠加按数量计算，正负效果都累计；有上限与唯一物品拒绝超额拾取。</summary>
        [Test] public void StackRules_ApplyBothSignsAndRejectOverflow()
        {
            AbilityDataSO bandage = Item("moss_bandage");
            for (int i = 0; i < 25; i++) Assert.NotNull(_items.GrantOrUpgrade(bandage));
            Assert.AreEqual(175, _stats.MaxHealth); Assert.AreEqual(-25, _stats.GetFinalStat(PlayerStatType.HpRegeneration));
            Assert.AreEqual(3, bandage.GetLevelConfig(1).statModifiers[0].Value);
            AbilityDataSO gear = Item("red_gear");
            for (int i = 0; i < 3; i++) Assert.NotNull(_items.GrantOrUpgrade(gear));
            Assert.IsNull(_items.GrantOrUpgrade(gear));
            Assert.AreEqual(45, _stats.GetFinalStat(PlayerStatType.AttackSpeed));
            Assert.AreEqual(-6, _stats.GetFinalStat(PlayerStatType.DamagePercent));
            var clover = Item("clover_coin"); Assert.NotNull(_items.GrantOrUpgrade(clover)); Assert.IsNull(_items.GrantOrUpgrade(clover));
            StringAssert.Contains("+15%", RoundShopPresentation.ItemOfferDetails(gear, 2));
            StringAssert.Contains("+45%", RoundShopPresentation.ItemDetails(gear, 3));
            StringAssert.Contains("不限", bandage.CopyLimitText);
        }

        /// <summary>应急回血仅在有效受伤且存活时每波一次，不能因治疗或致死伤害触发。</summary>
        [Test] public void Candy_OncePerRound_ExactFractionAndNoLethalRescue()
        {
            _items.GrantOrUpgrade(Item("emergency_candy"));
            _health.TakeDamage(75); Assert.AreEqual(40, _health.CurrentHealth);
            _health.TakeDamage(20); Assert.AreEqual(20, _health.CurrentHealth);
            _health.Heal(1); Assert.AreEqual(21, _health.CurrentHealth);
            _health.PrepareRound(); SetRound(2, RoundPhase.Combat); NotifyRound();
            _health.TakeDamage(75); Assert.AreEqual(40, _health.CurrentHealth);
            _health.TakeDamage(100); Assert.IsTrue(_health.IsDead); Assert.AreEqual(0, _health.CurrentHealth);
        }

        /// <summary>沙漏在 15 秒启用，局间清除，新波重新计时；销毁能力组件不残留来源。</summary>
        [Test] public void Hourglass_ThresholdBoundaryResetAndDispose()
        {
            _items.GrantOrUpgrade(Item("hourglass_pendant"));
            _round.Tick(14.9f); Assert.AreEqual(0, _stats.GetFinalStat(PlayerStatType.DamagePercent));
            _round.Tick(.1f); Assert.AreEqual(15, _stats.GetFinalStat(PlayerStatType.DamagePercent));
            _round.Tick(3); Assert.AreEqual(5, _stats.GetFinalStat(PlayerStatType.SpeedPercent));
            SetPhase(RoundPhase.Shop); NotifyRound(); Assert.AreEqual(0, _stats.GetFinalStat(PlayerStatType.DamagePercent));
            float elapsed = _round.Current.Elapsed; _round.Tick(100); Assert.AreEqual(elapsed, _round.Current.Elapsed);
            SetRound(2, RoundPhase.Combat); NotifyRound(); _round.Tick(15);
            Assert.AreEqual(15, _stats.GetFinalStat(PlayerStatType.DamagePercent));
            // EditMode 不自动调用普通 MonoBehaviour 的销毁回调，显式验证同一生产释放入口。
            Invoke(_items, "OnDestroy"); Object.DestroyImmediate(_items); Assert.AreEqual(0, _stats.GetFinalStat(PlayerStatType.DamagePercent));
        }

        /// <summary>固定品质不再次乘价格，满额锁定商品刷新后移除且没有额外扣款。</summary>
        [Test] public void Shop_FixedItemQualityPriceAndLockedCap()
        {
            var catalog = ScriptableObject.CreateInstance<RunShopCatalogSO>();
            try
            {
                var product = AssetDatabase.LoadAssetAtPath<UpgradeDataSO>(ContentExpansionSetup.Root + "/Upgrades/clover_coin.asset");
                catalog.products.Add(new RunShopProduct { content = product, basePrice = 56 });
                var wallet = new RunMaterialWallet(); wallet.Credit(1000);
                var shop = new RunShopService(catalog, wallet, null, _items, _stats, () => true);
                shop.Enter(1); Assert.AreEqual(2, shop.Offers[0].Tier); Assert.AreEqual(58, shop.Offers[0].Price);
                shop.ToggleLock(0); _items.GrantOrUpgrade(product.abilityToGrant); shop.Enter(2);
                Assert.IsNull(shop.Offers[0]); Assert.AreEqual(1000, wallet.Balance); Assert.IsFalse(shop.Buy(0));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        /// <summary>正式商店必须完整收录 20 件审阅道具与 10 把四档武器，资产、图标、机制与价格一致。</summary>
        [Test] public void ProductionAssets_MatchApprovedDefinitionsAndPoolContracts()
        {
            var definitions = JsonUtility.FromJson<ContentExpansionSetup.Definitions>(File.ReadAllText(ContentExpansionSetup.Root + "/definitions.json"));
            var shop = AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
            Assert.AreEqual(20, definitions.items.Length); Assert.AreEqual(10, definitions.weapons.Length);
            foreach (var item in definitions.items)
            {
                AbilityDataSO actual = Item(item.id); Assert.NotNull(actual.icon); Assert.IsTrue(actual.IsAvailableInBrotato());
                Assert.AreEqual(item.quality, actual.quality); Assert.AreEqual(item.maxCopies, actual.maxCopies);
                Assert.IsTrue(actual.stackPerCopy); Assert.AreEqual(item.description, actual.displayDescription);
                Assert.AreEqual(item.price, shop.products.Find(p => p.Id == "upgrade." + item.id).basePrice);
                Assert.AreEqual(JsonUtility.ToJson(new AbilityLevelData {statModifiers=item.modifiers}),
                    JsonUtility.ToJson(new AbilityLevelData {statModifiers=actual.levelConfigs[0].statModifiers}));
            }
            foreach (var weapon in definitions.weapons)
            {
                var data = AssetDatabase.LoadAssetAtPath<WeaponDataSO>(ContentExpansionSetup.Root + "/Weapons/" + weapon.id + ".asset");
                Assert.NotNull(data.icon); Assert.NotNull(data.projectilePrefab); Assert.AreEqual(4, data.roundTierConfigs.Count);
                Assert.NotNull(data.projectilePrefab.GetComponent<IPoolable>());
                if (data.runtimeType == WeaponRuntimeType.Lobbed)
                    Assert.IsTrue(data.projectilePrefab.GetComponent<SpriteRenderer>().enabled,
                        "离屏检测读取根渲染器的世界边界，不能隐藏根渲染器后仅显示子节点。");
                for (int i = 0; i < 4; i++) Assert.AreEqual(JsonUtility.ToJson(weapon.tiers[i]), JsonUtility.ToJson(data.GetRoundTierConfig(i+1)));
                Assert.NotNull(shop.products.Find(p => p.content.weaponToGrant == data));
            }
        }

        /// <summary>读取正式道具配置。</summary>
        private static AbilityDataSO Item(string id) => AssetDatabase.LoadAssetAtPath<AbilityDataSO>(ContentExpansionSetup.Root + "/Items/" + id + ".asset");
        /// <summary>测试显式替换回合代次，不运行场景初始化。</summary>
        private void SetRound(int generation, RoundPhase phase)
        {
            typeof(RoundController).GetProperty("Current").SetValue(_round, new RoundRuntime(new RoundDefinition {survivalSeconds=60}, generation));
            SetPhase(phase);
        }
        /// <summary>设置测试阶段。</summary>
        private void SetPhase(RoundPhase phase) => typeof(RoundController).GetProperty("Phase").SetValue(_round, phase);
        /// <summary>模拟正式阶段变更事件。</summary>
        private void NotifyRound() => (typeof(RoundController).GetField("Changed", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_round) as System.Action)?.Invoke();
        /// <summary>驱动需要的非公开生命周期。</summary>
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target, null);
    }
}
