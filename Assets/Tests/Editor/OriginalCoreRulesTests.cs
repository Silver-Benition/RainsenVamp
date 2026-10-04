using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>本地原版普通规则样本及正式商店链路；不依赖概率偶然命中来证明过滤。</summary>
    public sealed class OriginalCoreRulesTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private Random.State _random;
        private PlayerStats _stats;
        private AbilityManager _items;
        private RunShopCatalogSO _catalog;
        private RunMaterialWallet _wallet;

        /// <summary>创建现代属性角色和正式目录副本，所有账号操作只使用内存。</summary>
        [SetUp] public void Setup()
        {
            _random = Random.state; Random.InitState(271003);
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); CharacterSelectionSession.Clear();
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character);
            character.useBrotatoStats = true; character.baseStats.maxHealth = 100;
            var player = new GameObject("OriginalCoreRules"); _objects.Add(player);
            _stats = player.AddComponent<PlayerStats>(); _stats.SetCharacterData(character);
            _items = player.AddComponent<AbilityManager>();
            _catalog = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset"));
            _objects.Add(_catalog); _wallet = new RunMaterialWallet(); _wallet.Credit(100000);
        }

        /// <summary>恢复随机状态并销毁副本，避免影响相邻测试或正式资产。</summary>
        [TearDown] public void Cleanup()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]);
            _objects.Clear(); Random.state = _random;
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); CharacterSelectionSession.Clear();
        }

        /// <summary>独立策划样本覆盖百分比之前截断、之后四舍五入和最低伤害。</summary>
        [TestCase(14, 25, 18)] [TestCase(1.9f, 100, 2)] [TestCase(-3, 200, 3)] [TestCase(10, -150, 1)]
        public void Damage_TwoStageRounding(float flat, float percent, float expected)
        { Assert.AreEqual(expected, BrotatoStatRules.ScaleDamage(flat, percent)); }

        /// <summary>负一百幸运仍有非零品质概率，阈值附近分别覆盖四档，掉率继续独立线性计算。</summary>
        [TestCase(.0034f, 4)] [TestCase(.0035f, 3)] [TestCase(.069f, 3)]
        [TestCase(.071f, 2)] [TestCase(.269f, 2)] [TestCase(.271f, 1)]
        public void NegativeLuck_UsesReciprocalForQualityOnly(float sample, int expected)
        {
            Assert.AreEqual(expected, BrotatoStatRules.RollTier(10, -100, sample));
            Assert.AreEqual(0, BrotatoStatRules.DropChance(.2f, -100));
        }

        /// <summary>原版护甲和再生消费整数点；小数负值向零截断。</summary>
        [Test] public void Defense_ConsumesIntegerPoints()
        {
            Assert.AreEqual(.5f, BrotatoStatRules.ArmorMultiplier(15.9f));
            Assert.AreEqual(1.5f, BrotatoStatRules.ArmorMultiplier(-15.9f));
            Assert.AreEqual(1, BrotatoStatRules.ArmorMultiplier(-.9f));
            Assert.AreEqual(5, BrotatoStatRules.RegenerationInterval(1.9f));
            Assert.AreEqual(float.PositiveInfinity, BrotatoStatRules.RegenerationInterval(.9f));
        }

        /// <summary>保护期取实际损血占最大生命比例，闪避和轻伤都至少零点二秒。</summary>
        [TestCase(0, .2f)] [TestCase(1, .2f)] [TestCase(10, .26666667f)] [TestCase(15, .4f)] [TestCase(80, .4f)]
        public void Protection_ClampsNormalTwentyWaveWindow(float lost, float expected)
        { Assert.That(BrotatoStatRules.DamageProtection(lost, 100), Is.EqualTo(expected).Within(.00001f)); }

        /// <summary>商品涨价、最低回收值和按波次递增的付费刷新费对应独立原版样本。</summary>
        [Test] public void Economy_ReferenceSamplesAndOverflow()
        {
            Assert.AreEqual(14, RunEconomyRules.Price(12, 1));
            Assert.AreEqual(25, RunEconomyRules.Price(17, 3));
            Assert.AreEqual(188, RunEconomyRules.Price(56, 20));
            Assert.AreEqual(6, RunEconomyRules.Recycle(17, 3, .25f));
            Assert.AreEqual(1, RunEconomyRules.Recycle(1, 20, .25f));
            Assert.AreEqual(1, RunEconomyRules.Recycle(2, 1, 0));
            Assert.AreEqual(1, RunEconomyRules.RerollPrice(1, 0));
            Assert.AreEqual(2, RunEconomyRules.RerollPrice(1, 1));
            Assert.AreEqual(5, RunEconomyRules.RerollPrice(5, 0));
            Assert.AreEqual(7, RunEconomyRules.RerollPrice(5, 1));
            Assert.AreEqual(39, RunEconomyRules.RerollPrice(20, 2));
            Assert.AreEqual(int.MaxValue, RunEconomyRules.Price(int.MaxValue, int.MaxValue));
            Assert.AreEqual(int.MaxValue, RunEconomyRules.RerollPrice(int.MaxValue, int.MaxValue));
        }

        /// <summary>武器四档价格允许非线性覆写，基础档不承担隐式统一倍数。</summary>
        [Test] public void WeaponPrice_UsesExplicitTierTable()
        {
            RunShopProduct original = _catalog.products.Find(x => x.IsWeapon);
            var product = new RunShopProduct { content = original.content, basePrice = 12, weaponTierPrices = new[] { 12, 19, 31, 53 } };
            Assert.AreEqual(31, product.BasePriceAtTier(3)); Assert.AreEqual(84, RunEconomyRules.Price(product.BasePriceAtTier(4), 5));
            foreach (var entry in _catalog.products) if (entry.IsWeapon) Assert.AreEqual(4, entry.weaponTierPrices.Length);
        }

        /// <summary>直接走正式商店：两把随机武器、两件白道具；刷新避开旧页，锁定武器计入次波配额。</summary>
        [Test] public void Shop_EarlyQuotaPreviousPageAndLockedQuote()
        {
            var shop = Shop(); shop.Enter(1);
            var old = new HashSet<string>(); int weapons = 0;
            foreach (var offer in shop.Offers)
            { Assert.NotNull(offer); old.Add(offer.Product.Id); if (offer.Product.IsWeapon) weapons++; else Assert.AreEqual(1, offer.Tier); }
            Assert.AreEqual(2, weapons); Assert.AreEqual(4, old.Count);
            Assert.IsTrue(shop.Refresh());
            foreach (var offer in shop.Offers) { Assert.NotNull(offer); Assert.IsFalse(old.Contains(offer.Product.Id)); }
            // 把武器锁在末格，检验先扫描全部锁定再填充前面空格，避免超额第三把。
            var offers = (RunShopOffer[])shop.Offers;
            RunShopOffer locked = offers[0]; Assert.IsTrue(locked.Product.IsWeapon);
            offers[0] = offers[3]; offers[3] = locked; Assert.IsTrue(shop.ToggleLock(3));
            shop.Enter(2); weapons = 0;
            foreach (var offer in offers) if (offer != null && offer.Product.IsWeapon) weapons++;
            Assert.AreEqual(2, weapons); Assert.AreSame(locked, offers[3]);
            int quote = locked.Price; shop.Enter(10); Assert.AreSame(locked, offers[3]); Assert.AreEqual(quote, offers[3].Price);
        }

        /// <summary>前期武器来自随机池而非固定取目录开头。</summary>
        [Test] public void Shop_EarlyWeaponsVaryAcrossSeeds()
        {
            var ids = new HashSet<string>();
            for (int seed = 0; seed < 12; seed++) { Random.InitState(seed); var shop = Shop(); shop.Enter(1); ids.Add(shop.Offers[0].Product.Id); }
            Assert.Greater(ids.Count, 4);
        }

        /// <summary>小池回退能复用旧页，却不会把被放逐的道具重新纳入候选。</summary>
        [Test] public void Shop_SmallPoolFallbackPreservesHardBan()
        {
            RunShopProduct item = _catalog.products.Find(x => !x.IsWeapon && x.content.abilityToGrant.quality == 1);
            _catalog.products.Clear(); _catalog.products.Add(item);
            var shop = Shop(); shop.Enter(1); Assert.NotNull(shop.Offers[0]); Assert.IsTrue(shop.Refresh()); Assert.NotNull(shop.Offers[0]);
            RunState.GetOrCreate(_stats).BanishUpgrade(item.Id); shop.Enter(1);
            foreach (var offer in shop.Offers) Assert.IsNull(offer);
        }

        /// <summary>道具按品质向下回退；不足白品质时返回空，不能偷偷回退到紫红品质。</summary>
        [Test] public void Selection_MissingTierNeverUpsamples()
        {
            RunShopProduct high = _catalog.products.Find(x => !x.IsWeapon && x.content.abilityToGrant.quality == 4);
            RunShopProduct low = _catalog.products.Find(x => !x.IsWeapon && x.content.abilityToGrant.quality == 1);
            Assert.NotNull(high); Assert.NotNull(low);
            var pool = new List<RunShopProduct> { high };
            Assert.IsNull(RunRewardSelection.Pick(pool, false, 1, null, null, 0));
            pool.Add(low); Assert.AreSame(low, RunRewardSelection.Pick(pool, false, 3, null, null, .9f));
            Assert.AreSame(high, RunRewardSelection.Pick(pool, false, 4, null, null, .9f));
        }

        /// <summary>已持有同种武器偏好在可用候选中生效，旧页排除优先于软偏好。</summary>
        [Test] public void Selection_OwnedWeaponPreferenceRespectsPreviousPage()
        {
            var pool = _catalog.products.FindAll(x => x.IsWeapon);
            var preferred = new HashSet<string> { pool[0].Id };
            Assert.AreSame(pool[0], RunRewardSelection.Pick(pool, true, 3, null, preferred, .99f));
            Assert.AreNotSame(pool[0], RunRewardSelection.Pick(pool, true, 3, preferred, preferred, 0));
        }

        /// <summary>建立真实服务，夹具只放开交易阶段，不替换抽取算法。</summary>
        private RunShopService Shop() => new RunShopService(_catalog, _wallet, null, _items, _stats, () => true);
    }
}
