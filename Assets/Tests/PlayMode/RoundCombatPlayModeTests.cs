using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>真实 MainLevel 的回合、商店和装备集成验证，仅使用内存账号。</summary>
    public sealed class RoundCombatPlayModeTests
    {
        private object _rounds;
        /// <summary>建立隔离账号并加载正式场景，等待起始武器和回合准备完成。</summary>
        [UnitySetUp]
        public IEnumerator Setup()
        {
            CallStatic("AccountProgressService", "SetStorageForTests", Activator.CreateInstance(TypeOf("InMemoryAccountProgressStorage")));
            yield return SceneManager.LoadSceneAsync("MainLevel");
            for (int i = 0; i < 5; i++) yield return null;
            _rounds = UnityEngine.Object.FindObjectOfType(TypeOf("RoundController"));
            Assert.IsNotNull(_rounds);
            Assert.AreEqual("Combat", Get<object>(_rounds, "Phase").ToString());
        }

        /// <summary>卸载真实场景后再重置账号，避免后续测试继承本局实例。</summary>
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            Scene empty = SceneManager.CreateScene("RoundTestEmpty");
            SceneManager.SetActiveScene(empty);
            Scene main = SceneManager.GetSceneByName("MainLevel");
            if (main.isLoaded) yield return SceneManager.UnloadSceneAsync(main);
            CallStatic("AccountProgressService", "SetStorageForTests", Activator.CreateInstance(TypeOf("InMemoryAccountProgressStorage")));
        }

        /// <summary>加速推进生命周期验证完整二十回合；不是实际二十回合性能或手感证据。</summary>
        [UnityTest]
        public IEnumerator AcceleratedTwentyRounds_EndExactlyOnceWithoutResettingRunState()
        {
            object run = Get<object>(_rounds, "Player");
            for (int wave = 1; wave <= 20; wave++)
            {
                Assert.AreEqual(wave, Get<int>(_rounds, "RoundNumber"));
                if (wave == 20) { Call(run, "AddExp", 100f); Assert.IsTrue((bool)Call(_rounds, "QueueCrate")); }
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                yield return null; yield return null;
                Assert.AreEqual(wave, Get<int>(_rounds, "CompletedRounds"));
                if (wave == 20)
                {
                    object pendingDirector = UnityEngine.Object.FindObjectOfType(TypeOf("RunDirector"));
                    Assert.AreEqual("Upgrades", Get<object>(_rounds, "Phase").ToString());
                    Assert.IsNull(Get<object>(pendingDirector, "FinalSnapshot"));
                    while (Get<object>(_rounds, "Phase").ToString() == "Upgrades") { Call(_rounds, "Choose", 0); yield return null; }
                    Assert.AreEqual("Crates", Get<object>(_rounds, "Phase").ToString());
                    Assert.IsNull(Get<object>(pendingDirector, "FinalSnapshot"));
                    Assert.IsTrue((bool)Call(_rounds, "ResolveCrate", Enum.Parse(TypeOf("CrateRewardAction"), "Take")));
                    yield return null;
                    break;
                }
                while (Get<object>(_rounds, "Phase").ToString() == "Upgrades")
                { Call(_rounds, "Choose", 0); yield return null; }
                Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
                Assert.AreEqual(0, Time.timeScale);
                Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
                Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
                yield return null;
            }
            Assert.AreEqual("Finished", Get<object>(_rounds, "Phase").ToString());
            object director = UnityEngine.Object.FindObjectOfType(TypeOf("RunDirector"));
            object final = Get<object>(director, "FinalSnapshot");
            Assert.AreEqual("Victory", Get<object>(final, "Outcome").ToString());
            Assert.AreEqual(20, Get<int>(final, "CompletedRounds"));
            Call(director, "EndRunAsDefeat");
            Assert.AreSame(final, Get<object>(director, "FinalSnapshot"));
        }

        /// <summary>升级不暂停战斗，回合结束后 Submit 逐次处理属性并进入商店。</summary>
        [UnityTest]
        public IEnumerator Experience_IsDeferredAndRealSubmitConsumesOneChoice()
        {
            object player = Get<object>(_rounds, "Player");
            Call(player, "AddExp", 100f);
            int pending = Get<int>(player, "PendingLevelUps");
            Assert.Greater(pending, 0); Assert.AreEqual(1, Time.timeScale);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            yield return null; yield return null;
            Assert.AreEqual("Upgrades", Get<object>(_rounds, "Phase").ToString());
            Transform panel = GameObject.Find("RoundIntermission").transform;
            Button choice = panel.Find("Offer0/Action").GetComponent<Button>();
            ExecuteEvents.Execute(choice.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(pending - 1, Get<int>(player, "PendingLevelUps"));
            ExecuteEvents.Execute(choice.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(pending - 1, Get<int>(player, "PendingLevelUps"), "同帧重复提交不能误选下一组属性。");
            yield return null;
            while (Get<object>(_rounds, "Phase").ToString() == "Upgrades")
            { Call(_rounds, "Choose", 0); yield return null; }
            Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
            object current = Get<object>(_rounds, "Current");
            float elapsed = Get<float>(current, "Elapsed");
            yield return null; yield return null;
            Assert.AreEqual(elapsed, Get<float>(current, "Elapsed"));
            Component health = ((Component)player).GetComponent("PlayerHealth");
            float hp = Get<float>(health, "CurrentHealth");
            Call(health, "TakeDamage", 1000f);
            Assert.AreEqual(hp, Get<float>(health, "CurrentHealth"));
        }

        /// <summary>一次积累多级时逐级领取；十级和二十级含重投均同品质保底，其他页允许混合。</summary>
        [UnityTest]
        public IEnumerator UpgradeQueue_MixedCardsMilestonesAndAppliedTierAgree()
        {
            object player = Get<object>(_rounds, "Player");
            RuntimeComponentTestUtility.SetField(player, "currentLevel", 21);
            RuntimeComponentTestUtility.SetField(player, "_levelUpQueue", 20);
            Call(Get<object>(_rounds, "Wallet"), "Credit", 10000);
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(230919);
            try
            {
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null;
                Transform panel = GameObject.Find("RoundIntermission").transform;
                Assert.IsNull(panel.Find("Exit"));
                bool mixed = false;
                for (int level = 2; level <= 21; level++)
                {
                    Assert.AreEqual(level, Get<int>(_rounds, "UpgradeLevel"));
                    IList tiers = Get<IList>(_rounds, "ChoiceTiers");
                    Assert.AreEqual(4, tiers.Count);
                    int upgrade = level - 1; Assert.AreEqual(upgrade, Get<int>(_rounds, "UpgradeCount"));
                    if (upgrade % 5 == 0)
                    {
                        for (int reroll = 0; reroll < 4; reroll++)
                        {
                            foreach (int tier in tiers) { Assert.AreEqual(tiers[0], tier); Assert.AreEqual(upgrade == 5 ? 2 : 3, tier); }
                            Assert.IsTrue((bool)Call(_rounds, "RerollUpgrade"));
                            Assert.AreEqual(level, Get<int>(_rounds, "UpgradeLevel"));
                        }
                        foreach (int tier in tiers) { Assert.AreEqual(tiers[0], tier); Assert.AreEqual(upgrade == 5 ? 2 : 3, tier); }
                    }
                    else foreach (int tier in tiers) mixed |= tier != (int)tiers[0];
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.IsFalse(panel.Find("Offer" + i + "/Secondary").gameObject.activeSelf);
                        Assert.IsFalse(panel.Find("Offer" + i + "/Banish").gameObject.activeSelf);
                    }
                    object choice = Get<IList>(_rounds, "Choices")[3];
                    object modifier = choice.GetType().GetField("modifier").GetValue(choice);
                    float expected = Get<float>(Call(choice, "AtTier", (int)tiers[3]), "Value");
                    Assert.IsTrue((bool)Call(_rounds, "Choose", 3));
                    IDictionary sources = (IDictionary)player.GetType().GetField("_modifierSources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
                    Assert.AreEqual(expected, Get<float>(((IList)sources["round.level." + (level - 1)])[0], "Value"));
                    yield return null;
                }
                Assert.IsTrue(mixed, "普通页应能同时出现不同品质。");
                Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
            }
            finally { UnityEngine.Random.state = saved; }
        }

        /// <summary>真实按钮只放逐道具；锁定不能保留被放逐商品，后续刷新和跨波继续排除，重开清零。</summary>
        [UnityTest]
        public IEnumerator Shop_BanishConsumesOnceAndExcludesOnlyRunItem()
        {
            object player = Get<object>(_rounds, "Player");
            object state = UnityEngine.Object.FindObjectOfType(TypeOf("RunState"));
            object shop = Get<object>(_rounds, "Shop");
            Assert.IsFalse((bool)Call(shop, "Banish", 0), "战斗阶段不可放逐。");
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null;
            object config = _rounds.GetType().GetField("config").GetValue(_rounds);
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            object item = null, weapon = null;
            foreach (object product in (IList)catalog.GetType().GetField("products").GetValue(catalog))
                if (Get<bool>(product, "IsWeapon")) weapon = product;
                else if (item == null && RuntimeComponentTestUtility.GetFieldValue<int>(
                    RuntimeComponentTestUtility.GetFieldValue<object>(RuntimeComponentTestUtility.GetFieldValue<object>(product, "content"), "abilityToGrant"), "quality") == 1) item = product;
            IList offers = Get<IList>(shop, "Offers");
            offers[0] = Activator.CreateInstance(TypeOf("RunShopOffer"), weapon, 1, 10);
            offers[1] = Activator.CreateInstance(TypeOf("RunShopOffer"), item, 1, 10);
            Assert.IsFalse((bool)Call(shop, "Banish", 1), "没有次数不得操作。");
            SetCountStat(player, "Banish", 2);
            Assert.IsFalse((bool)Call(shop, "Banish", 0), "武器不能放逐。");
            Assert.IsTrue((bool)Call(shop, "ToggleLock", 1));
            object ui = UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Call(ui, "Refresh");
            Transform panel = Get<GameObject>(ui, "Panel").transform;
            Assert.IsNull(panel.Find("Exit"));
            Assert.IsFalse(panel.Find("Offer0/Banish").gameObject.activeSelf);
            Assert.IsTrue(panel.Find("Offer1/Banish").gameObject.activeSelf);
            Assert.IsTrue(panel.Find("Offer1/Secondary").gameObject.activeSelf);
            string id = Get<string>(item, "Id");
            object wallet = Get<object>(_rounds, "Wallet");
            int money = Get<int>(wallet, "Balance"), notifications = 0;
            Action observer = () => {
                notifications++;
                Assert.AreEqual(1, Get<int>(state, "RemainingBanishes")); Assert.IsNull(offers[1]);
                Assert.IsTrue((bool)Call(state, "IsBanished", id));
                Assert.IsFalse((bool)Call(shop, "Buy", 0)); Assert.IsFalse((bool)Call(shop, "Banish", 1));
                Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
            };
            EventInfo changed = state.GetType().GetEvent("StateChanged"); changed.AddEventHandler(state, observer);
            try { ExecuteEvents.Execute(panel.Find("Offer1/Banish").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); }
            finally { changed.RemoveEventHandler(state, observer); }
            Assert.AreEqual(1, notifications); Assert.AreEqual(money, Get<int>(wallet, "Balance"));
            Assert.AreEqual(1, Get<int>(state, "RemainingBanishes")); Assert.IsNull(offers[1]);
            Assert.IsFalse((bool)Call(shop, "Banish", 1));
            Assert.IsFalse(panel.Find("Offer1/Banish").gameObject.activeSelf);
            object account = TypeOf("AccountProgressService").GetProperty("Current").GetValue(null);
            Assert.IsFalse((bool)Call(account, "IsUpgradeSealed", id));
            // 用仅含目标道具的局部目录验证刷新与下波候选，不把随机未出现当成排除成功。
            ScriptableObject clone = UnityEngine.Object.Instantiate((ScriptableObject)catalog);
            try
            {
                IList products = (IList)clone.GetType().GetField("products").GetValue(clone); products.Clear(); products.Add(item);
                object isolated = Activator.CreateInstance(TypeOf("RunShopService"), clone, wallet,
                    Get<object>(_rounds, "Loadout"), Get<object>(_rounds, "Items"), player, new Func<bool>(() => true));
                Call(wallet, "Credit", 1000); Call(isolated, "Enter", 1);
                Assert.IsTrue((bool)Call(isolated, "Refresh")); Call(isolated, "Enter", 2);
                foreach (object offer in Get<IList>(isolated, "Offers")) Assert.IsNull(offer);
                Call(state, "ResetRun"); Call(isolated, "Enter", 1);
                Assert.IsNotNull(Get<IList>(isolated, "Offers")[0]); Assert.AreEqual(2, Get<int>(state, "RemainingBanishes"));
            }
            finally { UnityEngine.Object.Destroy(clone); }
        }

        /// <summary>真实玩家共享吸血节流；再生逐点恢复，暂停、局间和负值均不能额外回血。</summary>
        [UnityTest]
        public IEnumerator Brotato_LifeStealRegenerationAndIntermissionGates()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth");
            RuntimeComponentTestUtility.SetField(health, "_currentHealth", 2f);
            Assert.IsTrue((bool)Call(health, "TryLifeSteal", .3f, .29f));
            Assert.AreEqual(3f, Get<float>(health, "CurrentHealth"));
            Assert.IsFalse((bool)Call(health, "TryLifeSteal", 1f, 0f), "同帧另一把武器不能突破全局节流。");
            yield return new WaitForSeconds(.11f);
            Assert.IsFalse((bool)Call(health, "TryLifeSteal", .3f, .3f));
            Assert.IsTrue((bool)Call(health, "TryLifeSteal", 1f, 0f));
            SetCountStat(player, "HpRegeneration", 10);
            yield return new WaitForSeconds(1.05f);
            Assert.AreEqual(5f, Get<float>(health, "CurrentHealth"));
            Time.timeScale = 0; yield return new WaitForSecondsRealtime(.2f);
            Assert.IsFalse((bool)Call(health, "TryLifeSteal", 1f, 0f));
            Assert.AreEqual(5f, Get<float>(health, "CurrentHealth"));
            Time.timeScale = 1; SetCountStat(player, "HpRegeneration", -10);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            Assert.IsFalse((bool)Call(health, "TryLifeSteal", 1f, 0f));
            Assert.AreEqual(5f, Get<float>(health, "CurrentHealth"));
        }

        /// <summary>真实敌人接受暴击才回血；回池目标拒绝命中，另一把无自带吸血的武器不能借用医疗效果。</summary>
        [UnityTest]
        public IEnumerator Brotato_WeaponSnapshotAcceptsSignedStatsAndRejectsPooledTarget()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth");
            RuntimeComponentTestUtility.SetField(health, "_currentHealth", 2f);
            SetCountStat(player, "LifeSteal", -20); SetCountStat(player, "CritChance", -20);
            object config = Activator.CreateInstance(TypeOf("WeaponLevelData"));
            RuntimeComponentTestUtility.SetField(config, "lifeSteal", 120f);
            RuntimeComponentTestUtility.SetField(config, "critChance", 120f);
            RuntimeComponentTestUtility.SetField(config, "critMultiplier", 2f);
            object hit = Activator.CreateInstance(TypeOf("WeaponHitSnapshot"), player, health, config);
            GameObject target = SpawnFixture("Assets/Prefab/Enemy/EnemyWeak_1.prefab", new Vector3(100, 100));
            object result = Call(hit, "Apply", target.GetComponent("EnemyBase"), 7f, null);
            Assert.AreEqual(14f, Get<float>(result, "AppliedDamage"));
            Assert.AreEqual(3f, Get<float>(health, "CurrentHealth"));
            target.SetActive(false); yield return new WaitForSeconds(.11f);
            result = Call(hit, "Apply", target.GetComponent("EnemyBase"), 7f, null);
            Assert.IsFalse(Get<bool>(result, "Accepted")); Assert.AreEqual(3f, Get<float>(health, "CurrentHealth"));
            RuntimeComponentTestUtility.SetField(config, "lifeSteal", 0f);
            object ordinary = Activator.CreateInstance(TypeOf("WeaponHitSnapshot"), player, health, config);
            Assert.AreEqual(0f, RuntimeComponentTestUtility.GetFieldValue<float>(ordinary, "LifeStealChance"));
            Assert.AreEqual(1f, RuntimeComponentTestUtility.GetFieldValue<float>(hit, "LifeStealChance"));
        }

        /// <summary>收获成功波只结算一次，正值递增；下一波负值只扣已有材料和本级经验，不降级。</summary>
        [UnityTest]
        public IEnumerator Brotato_HarvestOnceNegativeAndRestartIsolation()
        {
            object player = Get<object>(_rounds, "Player"), wallet = Get<object>(_rounds, "Wallet");
            SetCountStat(player, "Harvesting", 20);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            Assert.AreEqual(20, Get<int>(wallet, "Balance"));
            Assert.AreEqual(2, RuntimeComponentTestUtility.GetFieldValue<int>(player, "currentLevel"));
            Assert.AreEqual(4f, RuntimeComponentTestUtility.GetFieldValue<float>(player, "currentExp"));
            Assert.AreEqual(21f, (float)Call(player, "GetFinalStat", Enum.Parse(TypeOf("PlayerStatType"), "Harvesting")));
            RuntimeComponentTestUtility.Invoke(_rounds, "SettleHarvesting");
            Assert.AreEqual(20, Get<int>(wallet, "Balance"));
            while (Get<object>(_rounds, "Phase").ToString() == "Upgrades") { Call(_rounds, "Choose", 0); yield return null; }
            SetCountStat(player, "Harvesting", -101);
            Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            Assert.AreEqual(0, Get<int>(wallet, "Balance"));
            Assert.AreEqual(2, RuntimeComponentTestUtility.GetFieldValue<int>(player, "currentLevel"));
            Assert.AreEqual(0f, RuntimeComponentTestUtility.GetFieldValue<float>(player, "currentExp"));
            yield return SceneManager.LoadSceneAsync("MainLevel"); yield return null; yield return null;
            _rounds = UnityEngine.Object.FindObjectOfType(TypeOf("RoundController"));
            Assert.AreEqual(0, Get<int>(Get<object>(_rounds, "Wallet"), "Balance"));
            Assert.AreEqual(0f, (float)Call(Get<object>(_rounds, "Player"), "GetFinalStat", Enum.Parse(TypeOf("PlayerStatType"), "Harvesting")));
        }

        /// <summary>死亡/失败过渡不发放收获；有符号护甲在真实受伤路径产生减伤与增伤。</summary>
        [UnityTest]
        public IEnumerator Brotato_ArmorAndFailedHarvest()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth");
            RuntimeComponentTestUtility.SetField(health, "invulnerabilityDuration", 0f);
            SetCountStat(player, "Armor", 15); Call(health, "TakeDamage", 4f);
            Assert.AreEqual(8f, Get<float>(health, "CurrentHealth"));
            RuntimeComponentTestUtility.SetField(health, "_nextDamageAllowedTime", Time.time - 1);
            SetCountStat(player, "Armor", -15); Call(health, "TakeDamage", 4f);
            Assert.AreEqual(2f, Get<float>(health, "CurrentHealth"));
            SetCountStat(player, "Harvesting", 20); Call(_rounds, "Tick", 100f);
            Call(UnityEngine.Object.FindObjectOfType(TypeOf("RunDirector")), "EndRunAsDefeat");
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.AreEqual(0, Get<int>(Get<object>(_rounds, "Wallet"), "Balance"));
        }

        /// <summary>经属性重算增加测试次数，验证 RunState 的实际容量同步而非直接篡改剩余值。</summary>
        private static void SetCountStat(object player, string stat, int amount)
        {
            Type modifierType = TypeOf("PlayerStatModifier");
            Array modifiers = Array.CreateInstance(modifierType, 1);
            modifiers.SetValue(Activator.CreateInstance(modifierType, Enum.Parse(TypeOf("PlayerStatType"), stat),
                Enum.Parse(TypeOf("PlayerStatModifierMode"), "Flat"), (float)amount), 0);
            Call(player, "SetModifiers", "test.round." + stat, modifiers);
        }

        /// <summary>通过瞬间无死亡收益地清敌；材料入袋、回血吸收和宝箱入队各执行一次。</summary>
        [UnityTest]
        public IEnumerator Settlement_DespawnBagHealAndQueueWithoutDeathLoot()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth");
            RuntimeComponentTestUtility.SetField(health, "_currentHealth", 2f);
            GameObject enemy = SpawnFixture("Assets/Prefab/Enemy/EnemyWeak_1.prefab", new Vector3(10, 6));
            GameObject heal = SpawnFixture("Assets/Prefab/Pickup/CaptainPickup.prefab", new Vector3(8, 5));
            GameObject crystal = SpawnFixture("Assets/Prefab/Pickup/CrystalBallPickup.prefab", new Vector3(9, 5));
            GameObject gem = SpawnFixture("Assets/Prefab/ExpGem.prefab", new Vector3(10, 0));
            RuntimeComponentTestUtility.SetField(gem.GetComponent("ExpGem"), "expValue", 7f);
            SpawnFixture("Assets/Prefab/Pickup/CoinPickup.prefab", new Vector3(9, -5));
            GameObject touched = SpawnFixture("Assets/Prefab/Pickup/TreasureChestPickup.prefab", new Vector3(8, -5));
            RuntimeComponentTestUtility.Invoke(touched.GetComponent("TreasureChestPickup"), "OnTriggerEnter2D", ((Component)player).GetComponent<Collider2D>());
            Assert.AreEqual(1, Get<int>(_rounds, "PendingCrates"));
            Assert.AreEqual(0, Get<IList>(Get<object>(_rounds, "Items"), "OwnedAbilities").Count);
            GameObject chest = SpawnFixture("Assets/Prefab/Pickup/TreasureChestPickup.prefab", new Vector3(7, -5));
            object state = UnityEngine.Object.FindObjectOfType(TypeOf("RunState"));
            object wallet = Get<object>(_rounds, "Wallet");
            int kills = Get<int>(state, "KillCount");
            Call(_rounds, "Tick", 100f); yield return null; yield return null;
            Assert.AreEqual("Settling", Get<object>(_rounds, "Phase").ToString());
            Assert.IsTrue(GameObject.Find("PassOverlay").activeInHierarchy);
            Assert.IsFalse(enemy.activeSelf); Assert.IsFalse(crystal.activeSelf); Assert.IsFalse(gem.activeSelf);
            Assert.AreEqual(kills, Get<int>(state, "KillCount"));
            Assert.AreEqual(7, Get<int>(wallet, "Bagged")); Assert.AreEqual(0, Get<int>(wallet, "Balance"));
            Assert.AreEqual(0, Get<int>(player, "PendingLevelUps")); Assert.AreEqual(2, Get<int>(_rounds, "PendingCrates"));
            Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
            yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            Assert.AreEqual(5f, Get<float>(health, "CurrentHealth")); Assert.IsFalse(heal.activeSelf);
            Assert.IsFalse((bool)Call(heal.GetComponent("MapInstantEffectPickup"), "CollectForSettlement", player));
            Assert.IsFalse((bool)Call(chest.GetComponent("TreasureChestPickup"), "CollectForSettlement"));
            Assert.AreEqual(0f, Get<float>(UnityEngine.Object.FindObjectOfType(TypeOf("WorldFreezeController")), "RemainingDuration"));
            Assert.AreEqual("Crates", Get<object>(_rounds, "Phase").ToString());
            Assert.AreEqual(0, Get<IList>(Get<object>(_rounds, "Items"), "OwnedAbilities").Count);
        }

        /// <summary>宝箱三种选择通过真实按钮领取；同帧和观察者重入不能双领，禁用排除后续宝箱及锁定商店报价。</summary>
        [UnityTest]
        public IEnumerator Crates_TakeRecycleBanishAreAtomicAndShareExclusions()
        {
            object player = Get<object>(_rounds, "Player"); SetCountStat(player, "Banish", 1);
            object items = Get<object>(_rounds, "Items"), wallet = Get<object>(_rounds, "Wallet");
            ScriptableObject original = (ScriptableObject)_rounds.GetType().GetField("config").GetValue(_rounds);
            ScriptableObject config = UnityEngine.Object.Instantiate(original);
            ScriptableObject catalog = UnityEngine.Object.Instantiate((ScriptableObject)config.GetType().GetField("shopCatalog").GetValue(config));
            IList products = (IList)catalog.GetType().GetField("products").GetValue(catalog);
            object item = null;
            foreach (object product in products) if (!Get<bool>(product, "IsWeapon")) { item = product; break; }
            products.Clear(); products.Add(item); config.GetType().GetField("shopCatalog").SetValue(config, catalog);
            _rounds.GetType().GetField("config").SetValue(_rounds, config);
            try
            {
                for (int i = 0; i < 4; i++) Assert.IsTrue((bool)Call(_rounds, "QueueCrate"));
                Call(player, "AddExp", 16f);
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                Assert.AreEqual("Upgrades", Get<object>(_rounds, "Phase").ToString());
                Assert.IsNull(Get<object>(_rounds, "CurrentCrate"));
                while (Get<object>(_rounds, "Phase").ToString() == "Upgrades") { Call(_rounds, "Choose", 0); yield return null; }
                object reward = Get<object>(_rounds, "CurrentCrate");
                Assert.AreSame(item, Get<object>(reward, "Product"));
                int refund = Get<int>(reward, "RecycleValue");
                object content = item.GetType().GetField("content").GetValue(item);
                object ability = content.GetType().GetField("abilityToGrant").GetValue(content);
                Transform card = GameObject.Find("RoundIntermission").transform.Find("CrateReward");
                int callbacks = 0;
                Action observer = () => {
                    callbacks++;
                    Assert.AreEqual(3, Get<int>(_rounds, "PendingCrates"));
                    Assert.IsFalse((bool)Call(_rounds, "ResolveCrate", Enum.Parse(TypeOf("CrateRewardAction"), "Take")));
                    Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
                };
                EventInfo changed = items.GetType().GetEvent("OwnedAbilitiesChanged"); changed.AddEventHandler(items, observer);
                try { ExecuteEvents.Execute(card.Find("Take").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); }
                finally { changed.RemoveEventHandler(items, observer); }
                Assert.AreEqual(1, callbacks); Assert.AreEqual(1, Get<int>(Call(items, "GetOwnedAbility", ability), "CurrentLevel"));
                ExecuteEvents.Execute(card.Find("Take").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                Assert.AreEqual(3, Get<int>(_rounds, "PendingCrates"));
                yield return null;
                int money = Get<int>(wallet, "Balance"), pending = Get<int>(player, "PendingLevelUps");
                yield return HoldCrateButton(card.Find("Recycle").gameObject);
                Assert.AreEqual(money + refund, Get<int>(wallet, "Balance")); Assert.AreEqual(pending, Get<int>(player, "PendingLevelUps"));
                Assert.AreEqual(1, Get<int>(Call(items, "GetOwnedAbility", ability), "CurrentLevel"));
                yield return null;
                object shop = Get<object>(_rounds, "Shop"); IList offers = Get<IList>(shop, "Offers");
                offers[0] = Activator.CreateInstance(TypeOf("RunShopOffer"), item, 1, 20);
                offers[0].GetType().GetProperty("Locked").SetValue(offers[0], true);
                yield return HoldCrateButton(card.Find("Banish").gameObject);
                object state = UnityEngine.Object.FindObjectOfType(TypeOf("RunState"));
                Assert.AreEqual(0, Get<int>(state, "RemainingBanishes"));
                Assert.IsTrue((bool)Call(state, "IsBanished", Get<string>(item, "Id")));
                Assert.AreEqual(money + refund * 2 + 10, Get<int>(wallet, "Balance"), "最后一箱因唯一候选被禁用而补偿十材料。");
                Assert.AreEqual(0, Get<int>(_rounds, "PendingCrates")); Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
                foreach (object offer in offers) if (offer != null) Assert.AreNotEqual(Get<string>(item, "Id"), Get<string>(Get<object>(offer, "Product"), "Id"));
                Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
                Assert.AreEqual(0, Get<int>(_rounds, "PendingUpgrades")); Assert.AreEqual(0, Get<int>(_rounds, "PendingCrates"));
            }
            finally { _rounds.GetType().GetField("config").SetValue(_rounds, original); UnityEngine.Object.Destroy(config); UnityEngine.Object.Destroy(catalog); }
        }

        /// <summary>通过真实指针按下与真实时间完成长按，普通 Submit 不能绕过确认。</summary>
        private IEnumerator HoldCrateButton(GameObject button)
        {
            int before = Get<int>(_rounds, "PendingCrates");
            ExecuteEvents.Execute(button, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(before, Get<int>(_rounds, "PendingCrates"));
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.AreEqual(before, Get<int>(_rounds, "PendingCrates"));
            Assert.That(button.transform.Find("HoldProgress").GetComponent<RectTransform>().anchorMax.x, Is.InRange(.1f, .99f));
            yield return new WaitForSecondsRealtime(.6f);
            ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerUpHandler);
        }

        /// <summary>过渡中失败立即取消回血吸收和奖励队列，不允许之后继续进入商店。</summary>
        [UnityTest]
        public IEnumerator Settlement_FailureCancelsDelayedRewards()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth"); RuntimeComponentTestUtility.SetField(health, "_currentHealth", 2f);
            SpawnFixture("Assets/Prefab/Pickup/CaptainPickup.prefab", new Vector3(8,5));
            Call(_rounds, "QueueCrate"); Call(_rounds, "Tick", 100f); yield return null;
            object director = UnityEngine.Object.FindObjectOfType(TypeOf("RunDirector")); Call(director, "EndRunAsDefeat");
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.AreEqual("Finished", Get<object>(_rounds, "Phase").ToString());
            Assert.AreEqual(2f, Get<float>(health, "CurrentHealth"));
            Assert.AreEqual(0, Get<IList>(Get<object>(_rounds, "Items"), "OwnedAbilities").Count);
            Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
        }

        /// <summary>在编辑器 PlayMode 中从正式 Prefab 通过生产对象池构造地面验收样本。</summary>
        private static GameObject SpawnFixture(string path, Vector3 position)
        {
            Type database = Type.GetType("UnityEditor.AssetDatabase, UnityEditor.CoreModule", true);
            GameObject prefab = (GameObject)database.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { path, typeof(GameObject) });
            Assert.IsNotNull(prefab, path);
            object pool = UnityEngine.Object.FindObjectOfType(TypeOf("PoolManager"));
            return (GameObject)Call(pool, "Spawn", prefab, position, Quaternion.identity);
        }

        /// <summary>满槽购买自动合并，独立实例合并和回收不会影响账号金币。</summary>
        [UnityTest]
        public IEnumerator Shop_FullSlotsAutoCombine_LocksAndRecyclingRemainConsistent()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null; yield return null;
            object wallet = Get<object>(_rounds, "Wallet"); Call(wallet, "Credit", 10000);
            object loadout = Get<object>(_rounds, "Loadout");
            IList owned = Get<IList>(loadout, "OwnedWeapons");
            object data = owned[0].GetType().GetField("weaponData").GetValue(owned[0]);
            while (owned.Count < 6) Assert.IsNotNull(Call(loadout, "BuyRoundWeapon", data, 1));
            Assert.AreNotEqual(Get<string>(owned[0], "InstanceId"), Get<string>(owned[1], "InstanceId"));
            object config = _rounds.GetType().GetField("config").GetValue(_rounds);
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            IList products = (IList)catalog.GetType().GetField("products").GetValue(catalog);
            object product = null;
            foreach (object entry in products)
            {
                object content = entry.GetType().GetField("content").GetValue(entry);
                if (content.GetType().GetField("weaponToGrant").GetValue(content) == data) { product = entry; break; }
            }
            object shop = Get<object>(_rounds, "Shop");
            IList offers = Get<IList>(shop, "Offers");
            offers[0] = Activator.CreateInstance(TypeOf("RunShopOffer"), product, 1, 12);
            Assert.IsTrue((bool)Call(shop, "Buy", 0));
            Assert.AreEqual(6, owned.Count);
            Assert.AreEqual(9988, Get<int>(wallet, "Balance"));
            Assert.IsFalse((bool)Call(shop, "Buy", 0));
            Assert.IsTrue((bool)Call(shop, "Combine", owned[1]));
            Assert.AreEqual(5, owned.Count);
            int balance = Get<int>(wallet, "Balance");
            Assert.IsTrue((bool)Call(shop, "Recycle", owned[0]));
            Assert.AreEqual(4, owned.Count);
            Assert.Greater(Get<int>(wallet, "Balance"), balance);
            if (offers[1] != null)
            {
                object locked = offers[1]; Call(shop, "ToggleLock", 1); Call(shop, "Refresh");
                Assert.AreSame(locked, offers[1]);
            }
            object state = UnityEngine.Object.FindObjectOfType(TypeOf("RunState"));
            Assert.AreEqual(0, Get<int>(state, "GoldCount"));
        }

        /// <summary>装备同步回调不能抢花预留余额、重复购买或提前开始下一回合。</summary>
        [UnityTest]
        public IEnumerator Shop_ReentrantInventoryCallbackCannotSplitTransaction()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null; yield return null;
            object shop = Get<object>(_rounds, "Shop"), wallet = Get<object>(_rounds, "Wallet");
            object loadout = Get<object>(_rounds, "Loadout");
            IList offers = Get<IList>(shop, "Offers"), owned = Get<IList>(loadout, "OwnedWeapons");
            Assert.IsTrue(Get<bool>(Get<object>(offers[0], "Product"), "IsWeapon"));
            int price = Get<int>(offers[0], "Price"), count = owned.Count;
            Call(wallet, "Credit", price);
            int calls = 0;
            Action listener = () =>
            {
                calls++;
                Assert.AreEqual(0, Get<int>(wallet, "Balance"));
                Assert.IsNull(offers[0]);
                Assert.IsFalse((bool)Call(wallet, "TrySpend", 1));
                Assert.IsFalse((bool)Call(shop, "Buy", 1));
                Assert.IsFalse((bool)Call(_rounds, "BeginNextRound"));
            };
            EventInfo changed = loadout.GetType().GetEvent("OwnedWeaponsChanged");
            Action brokenObserver = () => throw new InvalidOperationException("Session23 observer failure");
            changed.AddEventHandler(loadout, brokenObserver);
            changed.AddEventHandler(loadout, listener);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Session23 observer failure"));
            try { Assert.IsTrue((bool)Call(shop, "Buy", 0)); }
            finally
            {
                changed.RemoveEventHandler(loadout, brokenObserver);
                changed.RemoveEventHandler(loadout, listener);
            }
            Assert.AreEqual(1, calls); Assert.AreEqual(count + 1, owned.Count);
            Assert.AreEqual(price, Get<int>(wallet, "Spent"));
            Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
        }

        /// <summary>验证图标悬停、移入浮窗、导航聚焦和实例回收，不依赖私有方法直接打开详情。</summary>
        [UnityTest]
        public IEnumerator Inventory_HoverTransferAndFocusKeepInstanceActionsCorrect()
        {
            // 本用例显式派发鼠标和导航事件；真实输入模块不能在等待帧中混入额外悬停/选择。
            BaseInputModule input = EventSystem.current.currentInputModule;
            bool wasEnabled = input != null && input.enabled;
            if (input != null) input.enabled = false;
            try
            {
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null; yield return null;
                Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
                GameObject panel = Get<GameObject>(ui, "Panel"), tooltip = Get<GameObject>(ui, "Tooltip");
                object loadout = Get<object>(_rounds, "Loadout");
                IList owned = Get<IList>(loadout, "OwnedWeapons");
                object first = owned[0];
                object data = first.GetType().GetField("weaponData").GetValue(first);
                object second = Call(loadout, "BuyRoundWeapon", data, 1);
                GrantCatalogItems();
                Call(ui, "Refresh"); yield return null;
                GameObject icon = panel.transform.Find("WeaponsArea/WeaponSlot0").gameObject;
                var pointer = new PointerEventData(EventSystem.current);
                Assert.IsFalse(tooltip.activeSelf);
                ExecuteEvents.Execute(icon, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsTrue(tooltip.activeSelf);
                Assert.IsTrue(tooltip.transform.Find("Combine").GetComponent<Button>().interactable);
                ExecuteEvents.Execute(icon, pointer, ExecuteEvents.pointerExitHandler);
                ExecuteEvents.Execute(tooltip, pointer, ExecuteEvents.pointerEnterHandler);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.IsTrue(tooltip.activeSelf, "鼠标移入详情操作区后必须保持显示。");
                ExecuteEvents.Execute(tooltip, pointer, ExecuteEvents.pointerExitHandler);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.IsFalse(tooltip.activeSelf, "离开图标和详情后应关闭。");

                EventSystem.current.SetSelectedGameObject(icon);
                Assert.IsTrue(tooltip.activeSelf, "键盘/手柄聚焦应展示详情。");
                GameObject recycle = tooltip.transform.Find("Recycle").gameObject;
                EventSystem.current.SetSelectedGameObject(recycle);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.IsTrue(tooltip.activeSelf, "导航进入详情按钮后不能被图标失焦关闭。");
                ExecuteEvents.Execute(recycle, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                Assert.AreEqual(1, owned.Count);
                Assert.AreSame(second, owned[0], "回收必须作用于查看的实例，不得误删同类另一把。");
                Assert.IsFalse(tooltip.activeSelf);

                EventSystem.current.SetSelectedGameObject(null);
                GameObject itemIcon = panel.transform.Find("ItemsArea/Viewport/Content/ItemSlot0").gameObject;
                ExecuteEvents.Execute(itemIcon, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsTrue(tooltip.activeSelf);
                Assert.IsFalse(tooltip.transform.Find("Recycle").gameObject.activeSelf);
                Assert.IsFalse(tooltip.transform.Find("Combine").gameObject.activeSelf);
                Assert.IsNotEmpty(Get<string>(tooltip.transform.Find("Description").GetComponent("TextMeshProUGUI"), "text"));
                ExecuteEvents.Execute(itemIcon, pointer, ExecuteEvents.pointerExitHandler);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.IsFalse(tooltip.activeSelf);
                ExecuteEvents.Execute(panel.transform.Find("StatsBoard/Secondary").gameObject,
                    new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                Assert.AreEqual("经验获取", Get<string>(panel.transform.Find("StatsBoard/Stat0/Name").GetComponent("TextMeshProUGUI"), "text"));
            }
            finally { if (input != null) input.enabled = wasEnabled; }
        }

        /// <summary>持有大量道具时滚动区保持图标尺寸，导航到底部会自动露出目标图标。</summary>
        [UnityTest]
        public IEnumerator Inventory_OverflowScrollRevealsFocusedIcon()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds); yield return null; yield return null;
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            object items = Get<object>(_rounds, "Items");
            object catalog = _rounds.GetType().GetField("config").GetValue(_rounds);
            catalog = catalog.GetType().GetField("shopCatalog").GetValue(catalog);
            IList products = (IList)catalog.GetType().GetField("products").GetValue(catalog);
            ScriptableObject template = null;
            foreach (object product in products)
            {
                if (Get<bool>(product, "IsWeapon")) continue;
                object content = product.GetType().GetField("content").GetValue(product);
                template = (ScriptableObject)content.GetType().GetField("abilityToGrant").GetValue(content);
                break;
            }
            var copies = new System.Collections.Generic.List<ScriptableObject>();
            try
            {
                for (int i = 0; i < 31; i++)
                {
                    ScriptableObject copy = UnityEngine.Object.Instantiate(template); copies.Add(copy);
                    copy.GetType().GetField("abilityID").SetValue(copy, "round.ui.scroll." + i);
                    Call(items, "GrantOrUpgrade", copy);
                }
                Call(ui, "Refresh"); yield return null; Canvas.ForceUpdateCanvases();
                GameObject panel = Get<GameObject>(ui, "Panel");
                ScrollRect scroll = panel.transform.Find("ItemsArea").GetComponent<ScrollRect>();
                Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
                RectTransform last = (RectTransform)scroll.content.GetChild(30);
                EventSystem.current.SetSelectedGameObject(last.gameObject);
                Canvas.ForceUpdateCanvases();
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last);
                Assert.GreaterOrEqual(bounds.min.y, scroll.viewport.rect.yMin - 1);
                Assert.LessOrEqual(bounds.max.y, scroll.viewport.rect.yMax + 1);
                Assert.IsTrue(Get<GameObject>(ui, "Tooltip").activeSelf);
            }
            finally { foreach (ScriptableObject copy in copies) UnityEngine.Object.Destroy(copy); }
        }

        /// <summary>生命 HUD、红色扣血、升级治疗、分页说明和回合生命覆盖走正式场景链路。</summary>
        [UnityTest]
        public IEnumerator HealthFeedbackAndStatHelp_UseAuthoritativeValues()
        {
            Component player = (Component)Get<object>(_rounds, "Player");
            Component health = player.GetComponent(TypeOf("PlayerHealth"));
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Component hud = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("PlayerHealthHudUI"));
            Assert.IsNotNull(hud);
            RectTransform bar = Get<RectTransform>(hud, "BarRoot"); var frame = (RectTransform)bar.parent;
            Canvas.ForceUpdateCanvases(); Assert.That(bar.rect.width, Is.EqualTo(frame.rect.width / 6).Within(.1f));
            float before = Get<float>(health, "CurrentHealth"); Call(health, "TakeDamage", 5f);
            Assert.AreEqual(before - 5, Get<float>(health, "CurrentHealth"));
            StringAssert.StartsWith((before - 5).ToString("0.##") + " / ", Get<string>(hud, "CurrentText"));
            Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
            bool redPopup = false;
            foreach (Component popup in UnityEngine.Object.FindObjectsOfType(TypeOf("DamagePopup")))
            {
                Component label = popup.GetComponent(textType);
                if (Get<string>(label, "text") != "-5") continue;
                Color color = Get<Color>(label, "color"); Assert.Greater(color.r, .9f); Assert.Less(color.g, .3f); redPopup = true;
                // 同一个池对象切回敌人数字时不能保留负号或红色。
                Call(popup, "Initialize", 7f, false, Color.white, Color.yellow);
                Assert.AreEqual("7", Get<string>(label, "text")); Assert.AreEqual(Color.white, Get<Color>(label, "color"));
            }
            Assert.IsTrue(redPopup); Call(health, "TakeDamage", 5f); Assert.AreEqual(before - 5, Get<float>(health, "CurrentHealth"));
            Call(player, "AddExp", 16f); Assert.AreEqual(before - 4, Get<float>(health, "CurrentHealth"));
            object flow = UnityEngine.Object.FindObjectOfType(TypeOf("GameFlowManager")); Call(flow, "PauseGame"); yield return null;
            Component board = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("PlayerStatBoardUI"));
            Assert.AreEqual(16, Get<int>(board, "DisplayedStatCount"));
            Transform boardRoot = Get<RectTransform>(board, "BoardRoot"); var pointer = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(boardRoot.Find("Rows/Stat2").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Transform tooltip = board.transform.Find("StatTooltip"); Assert.IsTrue(tooltip.gameObject.activeSelf);
            StringAssert.Contains("武器生命窃取率", Get<string>(tooltip.Find("Description").GetComponent(textType), "text"));
            ExecuteEvents.Execute(boardRoot.Find("Secondary").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(6, Get<int>(board, "DisplayedStatCount")); Assert.IsFalse(tooltip.gameObject.activeSelf);
            ExecuteEvents.Execute(boardRoot.Find("Rows/Stat0").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.IsTrue(tooltip.gameObject.activeSelf); Call(flow, "ResumeGame"); Assert.IsFalse(tooltip.gameObject.activeSelf);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
            Transform panel = Get<GameObject>(ui, "Panel").transform;
            ExecuteEvents.Execute(panel.Find("StatsBoard/Stat2").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Transform shopTip = panel.Find("StatTooltip"); Assert.IsTrue(shopTip.gameObject.activeSelf);
            StringAssert.Contains("武器生命窃取率", Get<string>(shopTip.Find("Description").GetComponent(textType), "text"));
            Assert.IsTrue((bool)Call(health, "SetNextRoundHealth", 3f)); Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
            Assert.AreEqual(3, Get<float>(health, "CurrentHealth")); Assert.IsFalse(shopTip.gameObject.activeSelf);
            Assert.IsFalse((bool)Call(_rounds, "BeginNextRound")); Assert.AreEqual(3, Get<float>(health, "CurrentHealth"));
        }

        /// <summary>暂停图标须能被真实射线命中，详情只读；超过六种道具可滚动，恢复立即关闭。</summary>
        [UnityTest]
        public IEnumerator PauseInventory_HoverScrollAndResume_UseCurrentLoadout()
        {
            GrantCatalogItems();
            object items = Get<object>(_rounds, "Items"); IList owned = Get<IList>(items, "OwnedAbilities");
            var seed = (ScriptableObject)Get<object>(owned[0], "Data");
            var copies = new System.Collections.Generic.List<ScriptableObject>();
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            object flow = UnityEngine.Object.FindObjectOfType(TypeOf("GameFlowManager"));
            try
            {
                for (int i = 0; i < 25; i++)
                {
                    ScriptableObject copy = UnityEngine.Object.Instantiate(seed); copies.Add(copy);
                    copy.GetType().GetField("abilityID").SetValue(copy, "pause_test_" + i);
                    Assert.IsNotNull(Call(items, "GrantOrUpgrade", copy));
                }
                Call(flow, "PauseGame"); yield return null; Canvas.ForceUpdateCanvases(); yield return null;
                Transform inventory = ui.transform.Find("PauseItems");
                Assert.IsTrue(inventory.gameObject.activeInHierarchy);
                ScrollRect scroll = inventory.GetComponentInChildren<ScrollRect>();
                Assert.AreEqual(owned.Count, scroll.content.childCount);
                Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
                var weapon = (RectTransform)ui.transform.Find("PlayerLoadoutDisplay/WeaponSlot_1");
                var pointer = new PointerEventData(EventSystem.current);
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, weapon.TransformPoint(weapon.rect.center));
                var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Assert.IsTrue(hits.Exists(hit => hit.gameObject.transform == weapon || hit.gameObject.transform.IsChildOf(weapon)), "武器槽必须能被真实鼠标射线命中");
                ExecuteEvents.Execute(weapon.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Transform tooltip = ui.transform.Find("PauseInventoryTooltip");
                Assert.IsTrue(tooltip.gameObject.activeSelf); Assert.IsEmpty(tooltip.GetComponentsInChildren<Button>());
                Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                Component body = tooltip.Find("Description").GetComponent(textType);
                StringAssert.Contains("<sprite index=2>", Get<string>(body, "text"));
                Call(body, "ForceMeshUpdate", false, false);
                object info = Get<object>(body, "textInfo");
                // TMP 3.0.7 在 GenerateTextMesh 末尾把 spriteCount 覆盖为未维护的字段；逐字检查真实可见网格。
                Array characters = (Array)info.GetType().GetField("characterInfo").GetValue(info);
                int characterCount = (int)info.GetType().GetField("characterCount").GetValue(info);
                bool visibleSprite = false;
                for (int i = 0; i < characterCount; i++)
                {
                    object character = characters.GetValue(i);
                    if (character.GetType().GetField("elementType").GetValue(character).ToString() == "Sprite"
                        && (bool)character.GetType().GetField("isVisible").GetValue(character)) visibleSprite = true;
                }
                Assert.IsTrue(visibleSprite, "缩放公式必须包含实际可见的图片网格");
                Assert.IsFalse(Get<bool>(body, "isTextOverflowing"));
                ExecuteEvents.Execute(weapon.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                Assert.IsFalse(tooltip.gameObject.activeSelf);
                scroll.verticalNormalizedPosition = 0; yield return null;
                Transform last = scroll.content.GetChild(owned.Count - 1);
                Bounds lastBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last);
                Assert.GreaterOrEqual(lastBounds.min.y, scroll.viewport.rect.yMin - 1);
                Assert.LessOrEqual(lastBounds.max.y, scroll.viewport.rect.yMax + 1);
                ExecuteEvents.Execute(last.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsTrue(tooltip.gameObject.activeSelf);
                Call(flow, "ResumeGame");
                Assert.IsFalse(inventory.gameObject.activeSelf); Assert.IsFalse(tooltip.gameObject.activeSelf);
                ExecuteEvents.Execute(last.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsFalse(tooltip.gameObject.activeSelf, "恢复后旧事件不得重新打开详情");
                Call(flow, "PauseGame"); yield return null;
                Assert.AreEqual(owned.Count, scroll.content.childCount, "反复暂停不得重复创建槽位");
                Call(flow, "ResumeGame");
            }
            finally { foreach (ScriptableObject copy in copies) UnityEngine.Object.Destroy(copy); }
        }

        /// <summary>通过正式道具授予链补齐现有目录，用于验证图标库存和详情。</summary>
        private void GrantCatalogItems()
        {
            object config = _rounds.GetType().GetField("config").GetValue(_rounds);
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            foreach (object product in (IList)catalog.GetType().GetField("products").GetValue(catalog))
            {
                if (Get<bool>(product, "IsWeapon")) continue;
                object content = product.GetType().GetField("content").GetValue(product);
                Call(Get<object>(_rounds, "Items"), "GrantOrUpgrade", content.GetType().GetField("abilityToGrant").GetValue(content));
            }
        }

        /// <summary>为现有布局截图固定四档报价；从正式目录挑选内容，不修改共享资产或随机商店规则。</summary>
        private void SetQualityPreviewOffers()
        {
            object config = _rounds.GetType().GetField("config").GetValue(_rounds);
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            object weapon = null;
            var tierItems = new object[4];
            foreach (object product in (IList)catalog.GetType().GetField("products").GetValue(catalog))
            {
                object content = product.GetType().GetField("content").GetValue(product);
                if (Get<bool>(product, "IsWeapon"))
                {
                    object data = content.GetType().GetField("weaponToGrant").GetValue(content);
                    if ((string)data.GetType().GetField("weaponID").GetValue(data) == "12_coil_railgun") weapon = product;
                }
                else
                {
                    object data = content.GetType().GetField("abilityToGrant").GetValue(content);
                    int quality = (int)data.GetType().GetField("quality").GetValue(data);
                    if (quality >= 1 && quality <= 4 && data.GetType().GetField("mechanic").GetValue(data)?.GetType().Name == "StructureItemMechanicSO") tierItems[quality - 1] = product;
                }
            }
            Assert.NotNull(weapon); Assert.NotNull(tierItems[2]); Assert.NotNull(tierItems[3]);
            IList offers = Get<IList>(Get<object>(_rounds, "Shop"), "Offers");
            for (int i = 0; i < 4; i++)
                offers[i] = Activator.CreateInstance(TypeOf("RunShopOffer"), i < 2 ? weapon : tierItems[i], i + 1, 20 * (i + 1));
        }

        /// <summary>1080p 局间布局使用真实相机和正式 UI，可选输出截图。</summary>
        [UnityTest]
        public IEnumerator Layout_1920x1080() { yield return VerifyLayout(1920, 1080); }

        /// <summary>720p 复验属性选择与满六槽商店的文本和交互边界。</summary>
        [UnityTest]
        public IEnumerator Layout_1280x720() { yield return VerifyLayout(1280, 720); }

        /// <summary>配置目标尺寸后依次捕获战斗、升级、商店；无图形模式只断言布局。</summary>
        private IEnumerator VerifyLayout(int width, int height)
        {
            string[] args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-session23Screenshots");
            string directory = flag >= 0 && flag + 1 < args.Length ? args[flag + 1] : null;
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Canvas canvas = ui.GetComponent<Canvas>();
            Camera camera = Camera.main;
            var target = new RenderTexture(width, height, 24);
            int originalMask = camera.cullingMask;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) camera.cullingMask = 0;
            camera.targetTexture = target;
            canvas.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            // 正式 Canvas 是覆盖层；离屏相机用最高排序模拟覆盖关系。
            int originalOrder = canvas.sortingOrder, originalLayer = canvas.sortingLayerID;
            SortingLayer[] layers = SortingLayer.layers;
            canvas.sortingLayerID = layers[layers.Length - 1].id; canvas.sortingOrder = short.MaxValue;
            canvas.enabled = true;
            try
            {
                foreach (string page in new[] { "combat", "hurt", "pause", "pause-weapon-details", "pause-item-details", "pause-stat-help", "pause-secondary", "passed", "upgrades", "milestone-upgrades", "crate-reward", "crate-hold", "shop", "shop-set-help", "weapon-details", "item-details", "secondary-stats", "shop-stat-help" })
                {
                    if (page == "hurt")
                    {
                        Call(((Component)Get<object>(_rounds, "Player")).GetComponent(TypeOf("PlayerHealth")), "TakeDamage", 5f);
                    }
                    if (page == "pause-stat-help" || page == "pause-secondary")
                    {
                        Transform inventoryTip = ui.transform.Find("PauseInventoryTooltip"); inventoryTip.gameObject.SetActive(false);
                        Component board = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("PlayerStatBoardUI"));
                        if (page == "pause-secondary") Call(board, "SelectPage", true);
                        Transform row = Get<RectTransform>(board, "BoardRoot").Find(page == "pause-stat-help" ? "Rows/Stat10" : "Rows/Stat0");
                        ExecuteEvents.Execute(row.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    }
                    if (page == "pause")
                    {
                        object loadout = Get<object>(_rounds, "Loadout");
                        IList weapons = Get<IList>(loadout, "OwnedWeapons");
                        object data = FindWeaponData("01_copper_rapier");
                        while (weapons.Count < 6) Call(loadout, "BuyRoundWeapon", data, 1);
                        GrantCatalogItems();
                        object player = Get<object>(_rounds, "Player");
                        float armor = (float)Call(player, "GetFinalStat", Enum.Parse(TypeOf("PlayerStatType"), "Armor"));
                        SetCountStat(player, "Armor", 15 - Mathf.RoundToInt(armor));
                        Call(UnityEngine.Object.FindObjectOfType(TypeOf("GameFlowManager")), "PauseGame");
                    }
                    if (page == "pause-weapon-details")
                        ExecuteEvents.Execute(ui.transform.Find("PlayerLoadoutDisplay/WeaponSlot_2").gameObject,
                            new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    if (page == "pause-item-details")
                        ExecuteEvents.Execute(ui.transform.Find("PauseItems/Viewport/Content/ItemSlot0").gameObject,
                            new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    if (page == "passed")
                    {
                        Call(UnityEngine.Object.FindObjectOfType(TypeOf("GameFlowManager")), "ResumeGame");
                        Call(Get<object>(_rounds, "Player"), "AddExp", 100f);
                        Call(_rounds, "QueueCrate"); Call(_rounds, "QueueCrate");
                        Call(_rounds, "Tick", 100f); yield return null;
                    }
                    if (page == "upgrades") yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                    if (page == "crate-reward")
                    {
                        while (Get<object>(_rounds, "Phase").ToString() == "Upgrades")
                        { Call(_rounds, "Choose", 0); yield return null; }
                        SetCountStat(Get<object>(_rounds, "Player"), "Banish", 2);
                        // 用正式工程道具的长说明验收宝箱，避免随机抽到短描述掩盖溢出。
                        object fixtureConfig = _rounds.GetType().GetField("config").GetValue(_rounds);
                        object fixtureCatalog = fixtureConfig.GetType().GetField("shopCatalog").GetValue(fixtureConfig);
                        foreach (object product in (IList)fixtureCatalog.GetType().GetField("products").GetValue(fixtureCatalog))
                            if (Get<string>(product, "Id") == "upgrade.shell_mortar")
                                _rounds.GetType().GetProperty("CurrentCrate").SetValue(_rounds, Activator.CreateInstance(TypeOf("RunCrateReward"), product, 1, .5f));
                        Call(ui, "Refresh");
                    }
                    if (page == "crate-hold")
                    {
                        Transform button = Get<GameObject>(ui, "Panel").transform.Find("CrateReward/Recycle");
                        Component hold = button.GetComponent(TypeOf("HoldToConfirmButton"));
                        // 延长本次截图夹具的阈值，稳定捕捉中段进度；生产默认值仍为 0.8 秒。
                        RuntimeComponentTestUtility.SetField(hold, "holdSeconds", 4f);
                        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);
                        yield return new WaitForSecondsRealtime(1.5f);
                    }
                    if (page == "milestone-upgrades")
                    {
                        object player = Get<object>(_rounds, "Player");
                        RuntimeComponentTestUtility.SetField(player, "currentLevel", 13);
                        RuntimeComponentTestUtility.SetField(player, "_levelUpQueue", 3);
                        Call(Get<object>(_rounds, "Wallet"), "Credit", 100);
                        Call(_rounds, "RerollUpgrade");
                    }
                    if (page == "shop")
                    {
                        yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
                        while (Get<object>(_rounds, "Phase").ToString() == "Upgrades")
                        { Call(_rounds, "Choose", 0); yield return null; }
                        object loadout = Get<object>(_rounds, "Loadout");
                        IList owned = Get<IList>(loadout, "OwnedWeapons");
                        object data = FindWeaponData("01_copper_rapier");
                        while (owned.Count < 6) Call(loadout, "BuyRoundWeapon", data, 1);
                        GrantCatalogItems();
                        SetCountStat(Get<object>(_rounds, "Player"), "Banish", 2);
                        Call(Get<object>(_rounds, "Wallet"), "Credit", 1234);
                        SetQualityPreviewOffers();
                        Call(ui, "Refresh");
                    }
                    if (Get<object>(_rounds, "Phase").ToString() == "Shop")
                        Assert.AreEqual(1, ui.transform.Find("PassOverlay").GetComponent<Image>().color.a, "商店必须完全遮挡竞技场");
                    GameObject layoutPanel = Get<GameObject>(ui, "Panel");
                    var hover = new PointerEventData(EventSystem.current);
                    if (page == "shop-set-help")
                        ExecuteEvents.Execute(layoutPanel.transform.Find("Offer0/WeaponTags").gameObject, hover, ExecuteEvents.pointerEnterHandler);
                    if (page == "weapon-details")
                        ExecuteEvents.Execute(layoutPanel.transform.Find("WeaponsArea/WeaponSlot1").gameObject, hover, ExecuteEvents.pointerEnterHandler);
                    if (page == "item-details")
                        ExecuteEvents.Execute(layoutPanel.transform.Find("ItemsArea/Viewport/Content/ItemSlot0").gameObject, hover, ExecuteEvents.pointerEnterHandler);
                    if (page == "secondary-stats")
                    {
                        ExecuteEvents.Execute(Get<GameObject>(ui, "Tooltip").transform.Find("Close").gameObject,
                            new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                        ExecuteEvents.Execute(layoutPanel.transform.Find("StatsBoard/Secondary").gameObject,
                            new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                    }
                    if (page == "shop-stat-help")
                    {
                        RuntimeComponentTestUtility.Invoke(ui, "SelectStats", false);
                        ExecuteEvents.Execute(layoutPanel.transform.Find("StatsBoard/Stat10").gameObject,
                            new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    }
                    Canvas.ForceUpdateCanvases();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    if (page == "pause-weapon-details" || page == "weapon-details")
                    {
                        Transform tip = page == "pause-weapon-details" ? ui.transform.Find("PauseInventoryTooltip") : Get<GameObject>(ui, "Tooltip").transform;
                        Type tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                        Transform side = page == "pause-weapon-details" ? ui.transform.Find("PauseWeaponSide") : layoutPanel.transform.Find("WeaponSide");
                        Assert.IsTrue(side.gameObject.activeInHierarchy);
                        string body = Get<string>(side.Find("Sets/Description").GetComponent(tmp), "text");
                        StringAssert.Contains("刀刃 (5/6)", body); StringAssert.Contains("精准 (5/6)", body);
                        for (int tier = 2; tier <= 6; tier++) StringAssert.Contains("(" + tier + ")", body);
                        StringAssert.Contains("上一波", Get<string>(side.Find("WaveDamage/Description").GetComponent(tmp), "text"));
                        Bounds mainBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(ui.transform, tip);
                        Bounds sideBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(ui.transform, side);
                        Assert.Greater(sideBounds.min.x, mainBounds.max.x, "羁绊必须独立放在详情右侧");
                        Rect screen = ((RectTransform)ui.transform).rect;
                        Assert.LessOrEqual(sideBounds.max.x, screen.xMax); Assert.GreaterOrEqual(sideBounds.min.y, screen.yMin);
                        Assert.LessOrEqual(sideBounds.max.y, screen.yMax);
                        foreach (Component label in side.GetComponentsInChildren(tmp)) Assert.IsFalse(Get<bool>(label, "isTextOverflowing"));
                    }
                    Assert.AreEqual(width, camera.pixelWidth); Assert.AreEqual(height, camera.pixelHeight);
                    if (page == "passed" || page == "upgrades")
                    {
                        Image overlay = ui.transform.Find("PassOverlay").GetComponent<Image>();
                        Assert.That(overlay.color.a, Is.InRange(.5f, .9f));
                        Assert.IsFalse(overlay.canvasRenderer.cull);
                        Transform progress = ui.transform.Find("RoundProgress/Levels");
                        int visible = 0;
                        foreach (Transform icon in progress)
                        {
                            if (!icon.gameObject.activeSelf) continue;
                            visible++;
                            Graphic graphic = icon.GetComponent<Graphic>();
                            Assert.IsNotNull(icon.GetComponent<CanvasRenderer>());
                            Assert.Greater(((RectTransform)icon).rect.width, 10f);
                            Assert.IsFalse(graphic.canvasRenderer.cull);
                        }
                        Assert.AreEqual(Get<int>(_rounds, "PendingUpgrades"), visible);
                    }
                    Transform loadoutPanel = ui.transform.Find("PlayerLoadoutDisplay");
                    Assert.AreEqual(page.StartsWith("pause"), loadoutPanel.gameObject.activeInHierarchy, "装备仅在手动暂停中显示");
                    if (page.StartsWith("pause"))
                    {
                        Component attributes = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("PlayerStatBoardUI"));
                        RectTransform board = Get<RectTransform>(attributes, "BoardRoot");
                        var boardCorners = new Vector3[4]; var weaponCorners = new Vector3[4];
                        board.GetWorldCorners(boardCorners); ((RectTransform)loadoutPanel).GetWorldCorners(weaponCorners);
                        Assert.Greater(boardCorners[0].y, weaponCorners[1].y, "属性必须完整位于武器上方");
                        Assert.That(boardCorners[3].x, Is.EqualTo(weaponCorners[3].x).Within(.5f));
                        foreach (Transform slot in loadoutPanel)
                            if (slot.name.StartsWith("Ability")) Assert.IsFalse(slot.gameObject.activeSelf);
                        Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                        foreach (string region in new[] { "PauseItems", "PauseInventoryTooltip" })
                        {
                            Transform root = ui.transform.Find(region);
                            foreach (Component label in root.GetComponentsInChildren(textType))
                                Assert.IsFalse(Get<bool>(label, "isTextOverflowing"), page + "/" + region + "/" + label.name);
                        }
                        if (page.EndsWith("details")) Assert.IsTrue(ui.transform.Find("PauseInventoryTooltip").gameObject.activeSelf);
                        foreach (Component label in board.GetComponentsInChildren(textType))
                            Assert.IsFalse(Get<bool>(label, "isTextOverflowing"), "暂停属性溢出:" + label.name);
                    }
                    if (page.StartsWith("crate-"))
                    {
                        var crateBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(layoutPanel.transform, layoutPanel.transform.Find("CrateReward/Banish"));
                        var itemsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(layoutPanel.transform, layoutPanel.transform.Find("ItemsArea"));
                        Assert.Greater(crateBounds.min.y, itemsBounds.max.y + height * .04f, "宝箱操作不能覆盖道具栏或标题");
                    }
                    if (page == "passed" || page == "upgrades" || page == "crate-reward")
                    {
                        Transform crates = ui.transform.Find("RoundProgress/Crates");
                        int count = 0;
                        foreach (Transform icon in crates) if (icon.gameObject.activeSelf) { count++; Assert.IsNotNull(icon.GetComponent<Image>().sprite); }
                        Assert.AreEqual(Get<int>(_rounds, "PendingCrates"), count);
                        Assert.IsNull(ui.transform.Find("RoundProgress/CrateCount"));
                    }
                    if (page != "combat" && page != "hurt" && page != "passed" && !page.StartsWith("pause"))
                    {
                        GameObject panel = Get<GameObject>(ui, "Panel");
                        Assert.IsTrue(panel.activeInHierarchy);
                        Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                        foreach (Component label in panel.GetComponentsInChildren(textType))
                            Assert.IsFalse(Get<bool>(label, "isTextOverflowing"), page + "/" + label.name + " at " + width);
                        for (int card = 0; card < (page.StartsWith("crate-") ? 0 : 4); card++)
                        {
                            Image icon = panel.transform.Find("Offer" + card + "/Icon").GetComponent<Image>();
                            Assert.IsNotNull(icon.sprite);
                            Assert.Greater(icon.color.a, .99f, "商品和属性图标不能因底图透明而消失。");
                        }
                        foreach (Button button in panel.GetComponentsInChildren<Button>())
                        {
                            // 滚动内容允许超出屏幕；由视口裁切，不能把不可见的后续行当成布局溢出。
                            ScrollRect scroll = button.GetComponentInParent<ScrollRect>();
                            if (scroll != null && button.transform.IsChildOf(scroll.content))
                            {
                                Assert.IsNotNull(scroll.viewport.GetComponent<RectMask2D>());
                                continue;
                            }
                            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                            foreach (Vector3 corner in corners)
                            {
                                Vector3 screen = camera.WorldToScreenPoint(corner);
                                Assert.That(screen.x, Is.InRange(-1f, width + 1f));
                                Assert.That(screen.y, Is.InRange(-1f, height + 1f));
                            }
                        }
                    }
                    if (directory != null)
                    {
                        Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
                        System.IO.Directory.CreateDirectory(directory);
                        RenderTexture previous = RenderTexture.active;
                        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                        try
                        {
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, page + "-" + width + "x" + height + ".png"), pixels.EncodeToPNG());
                        }
                        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
                    }
                    if (page == "crate-hold")
                    {
                        Component hold = Get<GameObject>(ui, "Panel").transform.Find("CrateReward/Recycle").GetComponent(TypeOf("HoldToConfirmButton"));
                        Call(hold, "CancelHold");
                        RuntimeComponentTestUtility.SetField(hold, "holdSeconds", .8f);
                    }
                }
            }
            finally
            {
                camera.targetTexture = null; camera.cullingMask = originalMask;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.sortingOrder = originalOrder; canvas.sortingLayerID = originalLayer;
                target.Release(); UnityEngine.Object.Destroy(target);
            }
        }

        /// <summary>点击第二行武器后经过第一行和道具不改操作对象；显式点击另一把才切换。</summary>
        [UnityTest]
        public IEnumerator Feedback5_PinnedWeaponSurvivesCrossingOtherIcons()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            object loadout = Get<object>(_rounds, "Loadout"); IList owned = Get<IList>(loadout, "OwnedWeapons");
            object data = owned[0].GetType().GetField("weaponData").GetValue(owned[0]);
            while (owned.Count < 6) Call(loadout, "BuyRoundWeapon", data, 1);
            GrantCatalogItems();
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI")); Call(ui, "Refresh");
            Transform panel = Get<GameObject>(ui, "Panel").transform; GameObject tooltip = Get<GameObject>(ui, "Tooltip");
            object target = owned[4], upper = owned[1];
            GameObject bottom = panel.Find("WeaponsArea/WeaponSlot4").gameObject;
            GameObject top = panel.Find("WeaponsArea/WeaponSlot1").gameObject;
            var pointer = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(bottom, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(bottom, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(bottom, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(bottom, pointer, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(bottom, pointer, ExecuteEvents.pointerExitHandler);
            ExecuteEvents.Execute(top, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(panel.Find("ItemsArea/Viewport/Content/ItemSlot0").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.IsTrue(tooltip.activeSelf);
            Assert.AreSame(target, RuntimeComponentTestUtility.GetFieldValue<object>(ui, "_inspectedWeapon"));
            ExecuteEvents.Execute(tooltip.transform.Find("Combine").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(2, Get<int>(target, "CurrentLevel")); Assert.AreEqual(1, Get<int>(upper, "CurrentLevel"));
            Assert.IsFalse(tooltip.activeSelf);
            Call(ui, "Refresh");
            ExecuteEvents.Execute(panel.Find("WeaponsArea/WeaponSlot0").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            ExecuteEvents.Execute(panel.Find("WeaponsArea/WeaponSlot1").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreSame(owned[1], RuntimeComponentTestUtility.GetFieldValue<object>(ui, "_inspectedWeapon"));
            ExecuteEvents.Execute(tooltip.transform.Find("Close").gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.IsFalse(tooltip.activeSelf);
        }

        /// <summary>实际购买清空后零材料补货，保留正常刷新档位；锁定和禁用清空也遵守同一报价。</summary>
        [UnityTest]
        public IEnumerator Feedback5_EmptyShopRestocksFreeWithoutRaisingPrice()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            object shop = Get<object>(_rounds, "Shop"), wallet = Get<object>(_rounds, "Wallet");
            IList offers = Get<IList>(shop, "Offers"); Call(wallet, "Credit", 10000);
            int normalPrice = Get<int>(shop, "RefreshPrice"); Assert.Greater(normalPrice, 0);
            object item = null;
            object config = RuntimeComponentTestUtility.GetFieldValue<object>(_rounds, "config");
            object catalog = RuntimeComponentTestUtility.GetFieldValue<object>(config, "shopCatalog");
            foreach (object product in RuntimeComponentTestUtility.GetFieldValue<IList>(catalog, "products"))
                if (!Get<bool>(product, "IsWeapon")) { item = product; break; }
            // 首屏合法地可能全是武器；禁用夹具从目录取道具，不依赖商店随机结果。
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i] == null) continue;
                object product = Get<object>(offers[i], "Product"); if (!Get<bool>(product, "IsWeapon")) item = product;
                Assert.IsTrue((bool)Call(shop, "Buy", i));
            }
            Assert.AreEqual(0, Get<int>(shop, "RefreshPrice"));
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI")); Call(ui, "Refresh");
            Component refreshLabel = Get<GameObject>(ui, "Panel").transform.Find("Refresh/Label").GetComponent("TextMeshProUGUI");
            Assert.That(Get<string>(refreshLabel, "text"), Does.EndWith("0"));
            Call(wallet, "TrySpend", Get<int>(wallet, "Balance"));
            Assert.IsTrue((bool)Call(shop, "Refresh")); Assert.AreEqual(0, Get<int>(wallet, "Balance"));
            Assert.AreEqual(normalPrice, Get<int>(shop, "RefreshPrice"));
            Assert.IsFalse((bool)Call(shop, "Refresh"));
            for (int i = 0; i < offers.Count; i++) if (offers[i] != null) Call(shop, "ToggleLock", i);
            Call(wallet, "Credit", 100);
            Assert.IsFalse((bool)Call(shop, "Refresh")); Assert.AreEqual(100, Get<int>(wallet, "Balance"));
            Assert.IsNotNull(item); SetCountStat(Get<object>(_rounds, "Player"), "Banish", 1);
            for (int i = 0; i < offers.Count; i++) offers[i] = Activator.CreateInstance(TypeOf("RunShopOffer"), item, 1, 10);
            Assert.IsTrue((bool)Call(shop, "Banish", 0)); Assert.AreEqual(0, Get<int>(shop, "RefreshPrice"));
            Assert.IsTrue((bool)Call(shop, "Refresh")); Assert.AreEqual(100, Get<int>(wallet, "Balance"));
            Assert.AreEqual(normalPrice, Get<int>(shop, "RefreshPrice"));
            Assert.IsTrue((bool)Call(shop, "Refresh"));
            Assert.AreEqual(100 - normalPrice, Get<int>(wallet, "Balance"));
            Assert.Greater(Get<int>(shop, "RefreshPrice"), normalPrice);
        }

        /// <summary>远离中心结束回合后，下一波同一帧的真实武器攻击必须使用归中后的武器挂点。</summary>
        [UnityTest]
        public IEnumerator Feedback5_NextRoundFirstShotUsesCenteredTransform()
        {
            Component player = (Component)Get<object>(_rounds, "Player"); Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            Vector3 end = new Vector3(7, 3, 0); body.position = end; player.transform.position = end;
            Physics2D.SyncTransforms(); yield return new WaitForFixedUpdate(); yield return null;
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Assert.AreEqual(1, ui.transform.Find("PassOverlay").GetComponent<Image>().color.a);
            Assert.Less(player.transform.position.sqrMagnitude, .01f, "进入商店时应已复位");
            Assert.Less(body.position.sqrMagnitude, .01f); Assert.AreEqual(Vector2.zero, body.velocity);
            Assert.AreEqual(RigidbodyInterpolation2D.Interpolate, body.interpolation, "不能永久改变角色插值设置");
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Less(player.transform.position.sqrMagnitude, .01f, "暂停期间不应回放旧的插值位置");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType(TypeOf("ProjectileBase")).Length);
            Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
            Component weapon = (Component)Get<IList>(Get<object>(_rounds, "Loadout"), "OwnedWeapons")[0];
            // 新规则只在有效射程内有敌人时出手；首发归中回归必须显式提供可攻击目标。
            SpawnFixture("Assets/Prefab/Enemy/EnemyWeak_1.prefab", weapon.transform.position + Vector3.right * 1.5f);
            Physics2D.SyncTransforms();
            RuntimeComponentTestUtility.Invoke(weapon, "Update");
            UnityEngine.Object[] shots = UnityEngine.Object.FindObjectsOfType(TypeOf("ProjectileBase"));
            TestContext.Out.WriteLine("first-frame body=" + body.position + ", player=" + player.transform.position + ", weapon=" + weapon.transform.position + ", shots=" + shots.Length);
            Assert.Greater(shots.Length, 0, "必须实际发射，不能通过禁用全部攻击让测试假通过。");
            foreach (Component shot in shots)
                Assert.Less((shot.transform.position - weapon.transform.position).sqrMagnitude, .01f, "新回合首发必须位于归中后的武器挂点：" + shot.transform.position);
            Assert.Less(player.transform.position.sqrMagnitude, .01f);
            yield return new WaitForFixedUpdate(); yield return null;
            Assert.Less(player.transform.position.sqrMagnitude, .01f, "恢复物理后不能跳回旧位置");
        }

        /// <summary>真实镜头先跟到地图两侧，进入商店时即归中；下一波前半秒不得继续追赶旧位置。</summary>
        [UnityTest]
        public IEnumerator Feedback6_CameraSettlesUnderShopBeforeNextRound()
        {
            Component player = (Component)Get<object>(_rounds, "Player");
            Rigidbody2D body = player.GetComponent<Rigidbody2D>(); Camera camera = Camera.main;
            Vector3 home = camera.transform.position;
            Component framing = (Component)UnityEngine.Object.FindObjectOfType(Type.GetType("Cinemachine.CinemachineFramingTransposer, Cinemachine", true));
            float damping = RuntimeComponentTestUtility.GetFieldValue<float>(framing, "m_XDamping");
            Assert.Greater(damping, 0, "夹具必须启用正常平滑跟随");
            foreach (Vector3 edge in new[] { new Vector3(9, 6, 0), new Vector3(-9, -6, 0) })
            {
                body.position = edge; player.transform.position = edge; Physics2D.SyncTransforms();
                yield return new WaitForSeconds(.8f);
                Assert.Greater(Vector2.Distance(camera.transform.position, home), 2, "必须先让真实镜头跟到边缘，避免只测试角色瞬移。");
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
                for (int i = 0; i < 3; i++) yield return null;
                TestContext.Out.WriteLine("shop player=" + player.transform.position + ", camera=" + camera.transform.position + ", home=" + home);
                Assert.That(Vector3.Distance(camera.transform.position, home), Is.LessThan(.01f), "商店暂停期间镜头仍停在旧位置");
                Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
                float maxDrift = 0;
                for (int i = 0; i < 10; i++)
                {
                    yield return new WaitForSecondsRealtime(.05f);
                    maxDrift = Mathf.Max(maxDrift, Vector3.Distance(camera.transform.position, home));
                }
                TestContext.Out.WriteLine("next-wave camera max drift=" + maxDrift);
                Assert.Less(maxDrift, .01f, "恢复战斗后镜头仍在追赶复位位置");
                Assert.AreEqual(damping, RuntimeComponentTestUtility.GetFieldValue<float>(framing, "m_XDamping"), "不能通过永久移除正常阻尼掩盖问题");
            }
        }

        /// <summary>高速移动边界与安全生成点使用真实竞技场组件。</summary>
        [UnityTest]
        public IEnumerator Arena_ClampsHighSpeedAndSpawnsInsideSafeRegion()
        {
            Component player = (Component)Get<object>(_rounds, "Player");
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            Vector2 velocity = (Vector2)CallStatic("RoundArena", "ConstrainVelocity", body, new Vector2(100000, 100000), .4f);
            Vector2 next = body.position + velocity * Time.fixedDeltaTime;
            Assert.LessOrEqual(next.x, 11.6f + .001f);
            Assert.LessOrEqual(next.y, 7.6f + .001f);
            MethodInfo method = TypeOf("RoundArena").GetMethod("TrySpawnPosition");
            for (int i = 0; i < 20; i++)
            {
                object[] args = { Vector3.zero, Vector3.zero };
                Assert.IsTrue((bool)method.Invoke(null, args));
                Vector3 point = (Vector3)args[1];
                Assert.Less(Mathf.Abs(point.x), 12); Assert.Less(Mathf.Abs(point.y), 8);
                Assert.GreaterOrEqual(point.sqrMagnitude, 16);
            }
            yield return null;
        }

        /// <summary>真实经验连续升级：首个奖励为白品质；付费刷新跨奖励页累计，免费次数不抬高价格。</summary>
        [UnityTest]
        public IEnumerator OriginalCore_UpgradeProgressAndRerollCostsPersistPerWave()
        {
            object player = Get<object>(_rounds, "Player"), wallet = Get<object>(_rounds, "Wallet");
            // 默认角色被动自带一次免费重投，先经正式入口消耗后再验证付费序列。
            object state = UnityEngine.Object.FindObjectOfType(TypeOf("RunState"));
            while (Get<int>(state, "RemainingRerolls") > 0) Assert.IsTrue((bool)Call(state, "TryConsumeReroll"));
            Call(player, "AddExp", 41f); Call(wallet, "Credit", 100);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            Assert.AreEqual(2, Get<int>(_rounds, "UpgradeLevel")); Assert.AreEqual(1, Get<int>(_rounds, "UpgradeCount"));
            foreach (int tier in Get<IList>(_rounds, "ChoiceTiers")) Assert.AreEqual(1, tier);
            var previous = new System.Collections.Generic.HashSet<object>();
            foreach (object choice in Get<IList>(_rounds, "Choices")) previous.Add(Get<object>(RuntimeComponentTestUtility.GetFieldValue<object>(choice, "modifier"), "StatType"));
            int balance = Get<int>(wallet, "Balance");
            Assert.AreEqual(1, Get<int>(_rounds, "UpgradeRerollPrice")); Assert.IsTrue((bool)Call(_rounds, "RerollUpgrade"));
            Assert.AreEqual(balance - 1, Get<int>(wallet, "Balance"));
            foreach (object choice in Get<IList>(_rounds, "Choices")) Assert.IsFalse(previous.Contains(Get<object>(RuntimeComponentTestUtility.GetFieldValue<object>(choice, "modifier"), "StatType")));
            Assert.IsTrue((bool)Call(_rounds, "Choose", 0));
            Assert.AreEqual(3, Get<int>(_rounds, "UpgradeLevel")); Assert.AreEqual(2, Get<int>(_rounds, "UpgradeRerollPrice"));
            SetCountStat(player, "Reroll", 1); Assert.IsTrue((bool)Call(_rounds, "RerollUpgrade"));
            Assert.AreEqual(balance - 1, Get<int>(wallet, "Balance")); Assert.AreEqual(2, Get<int>(_rounds, "UpgradeRerollPrice"));
            Assert.IsTrue((bool)Call(_rounds, "RerollUpgrade")); Assert.AreEqual(balance - 3, Get<int>(wallet, "Balance"));
            Assert.AreEqual(3, Get<int>(_rounds, "UpgradeRerollPrice"));
            yield return null; // 奖励确认沿用每帧一次保护，下一页必须在下一帧领取。
            Assert.IsTrue((bool)Call(_rounds, "Choose", 0)); Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
            Assert.AreEqual(2, Get<int>(_rounds, "UpgradeRerollPrice"));
        }

        /// <summary>真实生命入口验证闪避也关闭受伤窗口，百分之十伤害保护约零点二六七秒且能阻止事件重入。</summary>
        [UnityTest]
        public IEnumerator OriginalCore_DodgeAndDamageCloseTheSameWindow()
        {
            object player = Get<object>(_rounds, "Player");
            Component health = ((Component)player).GetComponent("PlayerHealth");
            SetCountStat(player, "MaxHealth", 90); Call(health, "PrepareRound"); SetCountStat(player, "Dodge", 60);
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            try
            {
                int seed = 0;
                for (; seed < 1000; seed++) { UnityEngine.Random.InitState(seed); if (UnityEngine.Random.value < .6f) break; }
                Assert.Less(seed, 1000); UnityEngine.Random.InitState(seed);
                Call(health, "TakeDamage", 10f); Assert.AreEqual(100, Get<float>(health, "CurrentHealth"));
                Assert.That(RuntimeComponentTestUtility.GetFieldValue<float>(health, "_nextDamageAllowedTime") - Time.time, Is.EqualTo(.2f).Within(.001f));
                SetCountStat(player, "Dodge", 0); Call(health, "TakeDamage", 10f); Assert.AreEqual(100, Get<float>(health, "CurrentHealth"));
                RuntimeComponentTestUtility.SetField(health, "_nextDamageAllowedTime", Time.time - 1);
                int calls = 0; Action<float> reentrant = lost => { calls++; Call(health, "TakeDamage", 10f); };
                EventInfo damaged = health.GetType().GetEvent("Damaged"); damaged.AddEventHandler(health, reentrant);
                try { Call(health, "TakeDamage", 10f); }
                finally { damaged.RemoveEventHandler(health, reentrant); }
                Assert.AreEqual(1, calls); Assert.AreEqual(90, Get<float>(health, "CurrentHealth"));
                Assert.That(RuntimeComponentTestUtility.GetFieldValue<float>(health, "_nextDamageAllowedTime") - Time.time, Is.EqualTo(.26666667f).Within(.001f));
            }
            finally { UnityEngine.Random.state = saved; }
            yield return null;
        }

        /// <summary>首波宝箱按白品质筛选，锁定的唯一道具占用名额；不得用红道具绕过缺池补偿。</summary>
        [UnityTest]
        public IEnumerator OriginalCore_CrateReservesLockedCapAndNeverUpsamples()
        {
            ScriptableObject original = RuntimeComponentTestUtility.GetFieldValue<ScriptableObject>(_rounds, "config");
            ScriptableObject config = UnityEngine.Object.Instantiate(original);
            ScriptableObject catalog = UnityEngine.Object.Instantiate(RuntimeComponentTestUtility.GetFieldValue<ScriptableObject>(config, "shopCatalog"));
            IList products = RuntimeComponentTestUtility.GetFieldValue<IList>(catalog, "products"); object low = null, high = null;
            foreach (object product in products)
            {
                if (Get<bool>(product, "IsWeapon")) continue;
                object ability = RuntimeComponentTestUtility.GetFieldValue<object>(RuntimeComponentTestUtility.GetFieldValue<object>(product, "content"), "abilityToGrant");
                int quality = RuntimeComponentTestUtility.GetFieldValue<int>(ability, "quality");
                if (quality == 1 && low == null) low = product; if (quality == 4) high = product;
            }
            Assert.NotNull(low); Assert.NotNull(high);
            ScriptableObject content = UnityEngine.Object.Instantiate(RuntimeComponentTestUtility.GetFieldValue<ScriptableObject>(low, "content"));
            ScriptableObject item = UnityEngine.Object.Instantiate(RuntimeComponentTestUtility.GetFieldValue<ScriptableObject>(content, "abilityToGrant"));
            RuntimeComponentTestUtility.SetField(item, "stackPerCopy", true); RuntimeComponentTestUtility.SetField(item, "maxCopies", 1);
            RuntimeComponentTestUtility.SetField(content, "abilityToGrant", item);
            object unique = Activator.CreateInstance(TypeOf("RunShopProduct")); RuntimeComponentTestUtility.SetField(unique, "content", content);
            products.Clear(); products.Add(unique); products.Add(high);
            RuntimeComponentTestUtility.SetField(config, "shopCatalog", catalog); RuntimeComponentTestUtility.SetField(_rounds, "config", config);
            try
            {
                // 第一箱没有预留，必须从只有白/红两档的目录选中白色。
                Assert.IsTrue((bool)Call(_rounds, "QueueCrate")); Assert.IsTrue((bool)Call(_rounds, "QueueCrate"));
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                Assert.AreSame(unique, Get<object>(Get<object>(_rounds, "CurrentCrate"), "Product"));
                int refund = Get<int>(Get<object>(_rounds, "CurrentCrate"), "RecycleValue");
                object shop = Get<object>(_rounds, "Shop"), wallet = Get<object>(_rounds, "Wallet");
                IList offers = Get<IList>(shop, "Offers"); offers[0] = Activator.CreateInstance(TypeOf("RunShopOffer"), unique, 1, 14);
                offers[0].GetType().GetProperty("Locked").SetValue(offers[0], true);
                Assert.AreEqual(1, Call(shop, "LockedItemCount", Get<string>(unique, "Id")));
                Call(wallet, "Credit", 100); // 只验证预留不会封禁购买，资金不足另由交易用例覆盖。
                int balance = Get<int>(wallet, "Balance");
                Assert.IsTrue((bool)Call(_rounds, "ResolveCrate", Enum.Parse(TypeOf("CrateRewardAction"), "Recycle")));
                Assert.AreEqual(0, Get<int>(_rounds, "PendingCrates")); Assert.AreEqual("Shop", Get<object>(_rounds, "Phase").ToString());
                Assert.AreEqual(balance + refund + 10, Get<int>(wallet, "Balance"));
                Assert.IsTrue((bool)Call(shop, "Buy", 0), "宝箱预留过滤不能消耗已锁定道具的购买资格。");
            }
            finally
            {
                RuntimeComponentTestUtility.SetField(_rounds, "config", original);
                UnityEngine.Object.Destroy(config); UnityEngine.Object.Destroy(catalog); UnityEngine.Object.Destroy(content); UnityEngine.Object.Destroy(item);
            }
        }


        /// <summary>真实场景中买入、合成和回收会即时替换双标签加成，事件观察者读到最终属性。</summary>
        [UnityTest]
        public IEnumerator TagsEnemy_TradeUpdatesSetsAndCurrentArmorHelp()
        {
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
            object loadout = Get<object>(_rounds, "Loadout"), player = Get<object>(_rounds, "Player");
            object data = FindWeaponData("01_copper_rapier");
            Array sets = (Array)data.GetType().GetField("weaponSets").GetValue(data);
            object critStat = Enum.Parse(TypeOf("PlayerStatType"), "CritChance");
            float before = (float)Call(player, "GetFinalStat", critStat);
            object a = Call(loadout, "BuyRoundWeapon", data, 1), b = Call(loadout, "BuyRoundWeapon", data, 1);
            Assert.NotNull(a); Assert.NotNull(b); Assert.AreEqual(2, Call(loadout, "GetWeaponSetCount", sets.GetValue(0)));
            Assert.AreEqual(before + 3, Call(player, "GetFinalStat", critStat));
            object shop = Get<object>(_rounds, "Shop");
            Assert.IsTrue((bool)Call(shop, "Combine", a));
            Assert.AreEqual(2, Get<int>(a, "CurrentLevel")); Assert.AreEqual(1, Call(loadout, "GetWeaponSetCount", sets.GetValue(1)));
            Assert.AreEqual(before, Call(player, "GetFinalStat", critStat));
            Call(loadout, "BuyRoundWeapon", data, 1);
            Assert.AreEqual(before + 3, Call(player, "GetFinalStat", critStat));
            Assert.IsTrue((bool)Call(shop, "Recycle", a)); Assert.AreEqual(before, Call(player, "GetFinalStat", critStat));
            // 标签与伤害系数不同：远程采血针匹配细剑的精准，近战拳套没有共有标签。
            Assert.IsTrue((bool)CallStatic("WeaponShopPreference", "MatchesOwnedSet", FindWeaponData("07_medical_lancet"), loadout));
            Assert.IsFalse((bool)CallStatic("WeaponShopPreference", "MatchesOwnedSet", FindWeaponData("11_piston_gauntlet"), loadout));
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            SetCountStat(player, "Armor", 15);
            Transform panel = Get<GameObject>(ui, "Panel").transform;
            ExecuteEvents.Execute(panel.Find("StatsBoard/Stat10").gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
            Component description = panel.Find("StatTooltip/Description").GetComponent(textType);
            StringAssert.Contains("减少 50%", Get<string>(description, "text"));
            SetCountStat(player, "Armor", -15);
            StringAssert.Contains("增加 50%", Get<string>(description, "text"));
            yield return null;
        }

        /// <summary>跨真实回合从正式生成器取敌人，验证对象池中的生命及远程伤害采用本波数值。</summary>
        [UnityTest]
        public IEnumerator TagsEnemy_ActualSpawnerUsesWaveGrowthAndPoolResets()
        {
            Component waves = RuntimeComponentTestUtility.GetFieldValue<Component>(_rounds, "_waves");
            for (int wave = 1; wave <= 20; wave++)
            {
                if (wave == 1 || wave == 5 || wave == 10 || wave == 20)
                {
                    object definition = Get<object>(Get<object>(_rounds, "Current"), "Definition");
                    object spawn = definition.GetType().GetField("spawnConfig").GetValue(definition);
                    IList rules = (IList)spawn.GetType().GetField("rules").GetValue(spawn);
                    for (int index = 0; index < rules.Count; index++)
                    {
                        var before = new System.Collections.Generic.HashSet<int>();
                        foreach (Component enemy in UnityEngine.Object.FindObjectsOfType(TypeOf("EnemyBase"))) before.Add(enemy.GetInstanceID());
                        RuntimeComponentTestUtility.Invoke(waves, "SpawnFromRule", index, rules[index]);
                        Component found = null;
                        foreach (Component enemy in UnityEngine.Object.FindObjectsOfType(TypeOf("EnemyBase")))
                            if (!before.Contains(enemy.GetInstanceID())) { Assert.IsNull(found); found = enemy; }
                        Assert.NotNull(found, "正式生成器必须产生一个有效敌人");
                        float hp = index == 0 ? Mathf.Floor(6 + (wave - 1) * 1.5f) : 8 + wave - 1;
                        Assert.AreEqual(hp, Get<float>(found, "CurrentHealth"));
                        if (index > 0) Assert.That((float)Call(found, "ResolveOutgoingDamage", 2f), Is.EqualTo(2 + (wave - 1) * .08f).Within(.0001f));
                    }
                }
                if (wave == 20) break;
                Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
                yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
                Assert.IsTrue((bool)Call(_rounds, "BeginNextRound")); yield return null;
            }
        }

        /// <summary>真实波次边界固定实例账本；UI 回收报价与材料增量一致，商品正文不会触发羁绊。</summary>
        [UnityTest]
        public IEnumerator CombatDetails_WaveDamageSidePanelsAndRecycleQuote()
        {
            Component loadout = (Component)Get<object>(_rounds, "Loadout"); object data = FindWeaponData("01_copper_rapier");
            object a = Call(loadout, "BuyRoundWeapon", data, 1); object b = Call(loadout, "BuyRoundWeapon", data, 1);
            ((Behaviour)a).enabled = false; ((Behaviour)b).enabled = false;
            object hitA = RuntimeComponentTestUtility.Invoke(a, "CreateHitSnapshot");
            object hitB = RuntimeComponentTestUtility.Invoke(b, "CreateHitSnapshot");
            // 关闭随机暴击，给真实池化敌人指定生命；过量伤害按完整命中值记账。
            SetCountStat(Get<object>(_rounds, "Player"), "CritChance", -100);
            hitA = RuntimeComponentTestUtility.Invoke(a, "CreateHitSnapshot"); hitB = RuntimeComponentTestUtility.Invoke(b, "CreateHitSnapshot");
            GameObject enemy = SpawnFixture("Assets/Prefab/Enemy/EnemyWeak_1.prefab", new Vector3(100, 100));
            Call(hitA, "Apply", enemy.GetComponent("EnemyBase"), 101f, data);
            Call(hitB, "Apply", enemy.GetComponent("EnemyBase"), 999f, data);
            enemy = SpawnFixture("Assets/Prefab/Enemy/EnemyWeak_1.prefab", new Vector3(100, 100));
            Call(hitB, "Apply", enemy.GetComponent("EnemyBase"), 37f, data);
            Call(_rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_rounds);
            yield return RuntimeComponentTestUtility.ResolveRoundRewards(_rounds);
            Assert.AreEqual(101d, Get<double>(Get<object>(a, "WaveDamage"), "LastWaveDamage"));
            Assert.AreEqual(37d, Get<double>(Get<object>(b, "WaveDamage"), "LastWaveDamage"));
            object shop = Get<object>(_rounds, "Shop"); Assert.IsTrue((bool)Call(shop, "Combine", a));
            Assert.AreEqual(138d, Get<double>(Get<object>(a, "WaveDamage"), "LastWaveDamage"));
            yield return null;
            Component ui = (Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Transform panel = Get<GameObject>(ui, "Panel").transform;
            BaseInputModule input = EventSystem.current.currentInputModule; bool wasEnabled = input != null && input.enabled;
            if (input != null) input.enabled = false;
            try
            {
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(panel.Find("WeaponsArea/WeaponSlot1").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Type tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                StringAssert.Contains("138", Get<string>(panel.Find("WeaponSide/WaveDamage/Description").GetComponent(tmp), "text"));
                int quote = (int)Call(shop, "RecycleValue", a);
                StringAssert.Contains("+" + quote, Get<string>(Get<GameObject>(ui, "Tooltip").transform.Find("Recycle/Label").GetComponent(tmp), "text"));
                int balance = Get<int>(Get<object>(_rounds, "Wallet"), "Balance");
                ExecuteEvents.Execute(Get<GameObject>(ui, "Tooltip").transform.Find("Recycle").gameObject,
                    new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                Assert.AreEqual(balance + quote, Get<int>(Get<object>(_rounds, "Wallet"), "Balance"));
                Assert.AreEqual(0, Call(shop, "RecycleValue", a)); Assert.IsFalse(panel.Find("WeaponSide").gameObject.activeSelf);
                SetQualityPreviewOffers(); Call(ui, "Refresh");
                ExecuteEvents.Execute(panel.Find("Offer0").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsFalse(panel.Find("WeaponSide").gameObject.activeSelf);
                ExecuteEvents.Execute(panel.Find("Offer0/WeaponTags").gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsTrue(panel.Find("WeaponSide").gameObject.activeSelf); Assert.IsFalse(Get<GameObject>(ui, "Tooltip").activeSelf);
                Assert.IsFalse(panel.Find("WeaponSide/WaveDamage").gameObject.activeSelf);
                ExecuteEvents.Execute(panel.Find("Offer0/WeaponTags").gameObject, pointer, ExecuteEvents.pointerExitHandler);
                Assert.IsFalse(panel.Find("WeaponSide").gameObject.activeSelf);
            }
            finally { if (input != null) input.enabled = wasEnabled; }
            Assert.IsTrue((bool)Call(_rounds, "BeginNextRound"));
            object fresh = Call(loadout, "BuyRoundWeapon", data, 1);
            Assert.AreEqual(0d, Get<double>(Get<object>(fresh, "WaveDamage"), "LastWaveDamage"));
        }

        /// <summary>正式远程敌人的精灵/材质经过真实计时渐红，再由实际发射恢复；有图形时保存三个状态。</summary>
        [UnityTest]
        public IEnumerator CombatDetails_FormalRangedTelegraphFrames()
        {
            Component waves = RuntimeComponentTestUtility.GetFieldValue<Component>(_rounds, "_waves");
            ((Behaviour)waves).enabled = false;
            foreach (Behaviour weapon in Get<IList>(Get<object>(_rounds, "Loadout"), "OwnedWeapons")) weapon.enabled = false;
            Component player = (Component)Get<object>(_rounds, "Player");
            GameObject spawned = SpawnFixture("Assets/Prefab/Enemy/EnemyRanged_1.prefab", player.transform.position + Vector3.right * 5);
            Component enemy = spawned.GetComponent(TypeOf("RangedEnemyController"));
            RuntimeComponentTestUtility.Invoke(enemy, "BindWorldSimulation", RuntimeComponentTestUtility.GetFieldValue<Component>(waves, "enemySimulation"));
            RuntimeComponentTestUtility.Invoke(enemy, "ResetAttackCycle");
            SpriteRenderer sprite = enemy.GetComponent<SpriteRenderer>(); Color normal = sprite.color;
            var cameraObject = new GameObject("TelegraphCaptureCamera"); Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.4f;
            camera.transform.position = enemy.transform.position + Vector3.back * 10;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            try
            {
                CaptureCombatDetail(camera, "enemy-normal");
                float deadline = Time.realtimeSinceStartup + 2;
                while (Get<float>(enemy, "WarningProgress") < .75f && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.GreaterOrEqual(Get<float>(enemy, "WarningProgress"), .75f);
                Assert.Less(sprite.color.g, normal.g); Assert.AreEqual(normal.a, sprite.color.a);
                CaptureCombatDetail(camera, "enemy-warning");
                while (Get<float>(enemy, "WarningProgress") > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(normal, sprite.color);
                Assert.Greater(Get<float>(enemy, "AttackTimer"), 1f, "必须由成功发射重置攻击周期");
                CaptureCombatDetail(camera, "enemy-fired");
            }
            finally { UnityEngine.Object.Destroy(cameraObject); }
        }

        /// <summary>图形运行额外保存正式敌人的近景；无图形门禁继续验证同一运行状态而跳过像素读取。</summary>
        private static void CaptureCombatDetail(Camera camera, string name)
        {
            string[] args = Environment.GetCommandLineArgs(); int flag = Array.IndexOf(args, "-session23Screenshots");
            if (flag < 0 || flag + 1 >= args.Length) return;
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            string directory = args[flag + 1]; System.IO.Directory.CreateDirectory(directory);
            var target = new RenderTexture(512, 512, 24); var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
            }
        }

        /// <summary>从正式商店目录按稳定武器 ID 取测试对象，不依赖显示名或列表顺序。</summary>
        private object FindWeaponData(string id)
        {
            object config = RuntimeComponentTestUtility.GetFieldValue<object>(_rounds, "config");
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            foreach (object product in (IList)catalog.GetType().GetField("products").GetValue(catalog))
            {
                if (!Get<bool>(product, "IsWeapon")) continue;
                object data = RuntimeComponentTestUtility.GetFieldValue<object>(RuntimeComponentTestUtility.GetFieldValue<object>(product, "content"), "weaponToGrant");
                if (RuntimeComponentTestUtility.GetFieldValue<string>(data, "weaponID") == id) return data;
            }
            throw new InvalidOperationException("武器不在正式目录：" + id);
        }

        /// <summary>严格取得运行时类型，避免默认程序集依赖导致测试无法编译。</summary>
        private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        /// <summary>读取公开只读属性。</summary>
        private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        /// <summary>按参数数量查找实例方法并调用。</summary>
        private static object Call(object target, string name, params object[] args)
        {
            foreach (MethodInfo method in target.GetType().GetMethods())
                if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(target, args);
            throw new MissingMethodException(name);
        }
        /// <summary>调用指定运行时静态入口。</summary>
        private static object CallStatic(string type, string name, params object[] args) => TypeOf(type).GetMethod(name).Invoke(null, args);
    }
}
