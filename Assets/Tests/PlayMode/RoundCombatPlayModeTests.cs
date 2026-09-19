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
                Call(_rounds, "Tick", 100f);
                yield return null; yield return null;
                Assert.AreEqual(wave, Get<int>(_rounds, "CompletedRounds"));
                if (wave == 20) break;
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
            Call(_rounds, "Tick", 100f);
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
                Call(_rounds, "Tick", 100f); yield return null;
                Transform panel = GameObject.Find("RoundIntermission").transform;
                Assert.IsNull(panel.Find("Exit"));
                bool mixed = false;
                for (int level = 2; level <= 21; level++)
                {
                    Assert.AreEqual(level, Get<int>(_rounds, "UpgradeLevel"));
                    IList tiers = Get<IList>(_rounds, "ChoiceTiers");
                    Assert.AreEqual(4, tiers.Count);
                    if (level % 10 == 0)
                    {
                        for (int reroll = 0; reroll < 4; reroll++)
                        {
                            foreach (int tier in tiers) { Assert.AreEqual(tiers[0], tier); Assert.GreaterOrEqual(tier, 3); }
                            Assert.IsTrue((bool)Call(_rounds, "RerollUpgrade"));
                            Assert.AreEqual(level, Get<int>(_rounds, "UpgradeLevel"));
                        }
                        foreach (int tier in tiers) { Assert.AreEqual(tiers[0], tier); Assert.GreaterOrEqual(tier, 3); }
                    }
                    else foreach (int tier in tiers) mixed |= tier != (int)tiers[0];
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.IsFalse(panel.Find("Offer" + i + "/Secondary").gameObject.activeSelf);
                        Assert.IsFalse(panel.Find("Offer" + i + "/Banish").gameObject.activeSelf);
                    }
                    object choice = Get<IList>(_rounds, "Choices")[3];
                    object modifier = choice.GetType().GetField("modifier").GetValue(choice);
                    float expected = Get<float>(modifier, "Value") * (int)tiers[3];
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
            Call(_rounds, "Tick", 100f); yield return null;
            object config = _rounds.GetType().GetField("config").GetValue(_rounds);
            object catalog = config.GetType().GetField("shopCatalog").GetValue(config);
            object item = null, weapon = null;
            foreach (object product in (IList)catalog.GetType().GetField("products").GetValue(catalog))
                if (Get<bool>(product, "IsWeapon")) weapon = product; else item = product;
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

        /// <summary>经属性重算增加测试次数，验证 RunState 的实际容量同步而非直接篡改剩余值。</summary>
        private static void SetCountStat(object player, string stat, int amount)
        {
            Type modifierType = TypeOf("PlayerStatModifier");
            Array modifiers = Array.CreateInstance(modifierType, 1);
            modifiers.SetValue(Activator.CreateInstance(modifierType, Enum.Parse(TypeOf("PlayerStatType"), stat),
                Enum.Parse(TypeOf("PlayerStatModifierMode"), "Flat"), (float)amount), 0);
            Call(player, "SetModifiers", "test.round." + stat, modifiers);
        }

        /// <summary>满槽购买自动合并，独立实例合并和回收不会影响账号金币。</summary>
        [UnityTest]
        public IEnumerator Shop_FullSlotsAutoCombine_LocksAndRecyclingRemainConsistent()
        {
            Call(_rounds, "Tick", 100f); yield return null; yield return null;
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
            Call(_rounds, "Tick", 100f); yield return null; yield return null;
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
            Call(_rounds, "Tick", 100f); yield return null; yield return null;
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
            Assert.AreEqual("成长", Get<string>(panel.transform.Find("StatsBoard/Stat0/Name").GetComponent("TextMeshProUGUI"), "text"));
        }

        /// <summary>持有大量道具时滚动区保持图标尺寸，导航到底部会自动露出目标图标。</summary>
        [UnityTest]
        public IEnumerator Inventory_OverflowScrollRevealsFocusedIcon()
        {
            Call(_rounds, "Tick", 100f); yield return null; yield return null;
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
                foreach (string page in new[] { "combat", "upgrades", "milestone-upgrades", "shop", "weapon-details", "item-details", "secondary-stats" })
                {
                    if (page == "upgrades")
                    {
                        Call(Get<object>(_rounds, "Player"), "AddExp", 100f);
                        Call(_rounds, "Tick", 100f);
                    }
                    if (page == "milestone-upgrades")
                    {
                        object player = Get<object>(_rounds, "Player");
                        RuntimeComponentTestUtility.SetField(player, "currentLevel", 12);
                        RuntimeComponentTestUtility.SetField(player, "_levelUpQueue", 3);
                        Call(Get<object>(_rounds, "Wallet"), "Credit", 100);
                        Call(_rounds, "RerollUpgrade");
                    }
                    if (page == "shop")
                    {
                        while (Get<object>(_rounds, "Phase").ToString() == "Upgrades")
                        { Call(_rounds, "Choose", 0); yield return null; }
                        object loadout = Get<object>(_rounds, "Loadout");
                        IList owned = Get<IList>(loadout, "OwnedWeapons");
                        object data = owned[0].GetType().GetField("weaponData").GetValue(owned[0]);
                        while (owned.Count < 6) Call(loadout, "BuyRoundWeapon", data, 1);
                        GrantCatalogItems();
                        SetCountStat(Get<object>(_rounds, "Player"), "Banish", 2);
                        Call(Get<object>(_rounds, "Wallet"), "Credit", 1234);
                        Call(ui, "Refresh");
                    }
                    GameObject layoutPanel = Get<GameObject>(ui, "Panel");
                    var hover = new PointerEventData(EventSystem.current);
                    if (page == "weapon-details")
                        ExecuteEvents.Execute(layoutPanel.transform.Find("WeaponsArea/WeaponSlot0").gameObject, hover, ExecuteEvents.pointerEnterHandler);
                    if (page == "item-details")
                        ExecuteEvents.Execute(layoutPanel.transform.Find("ItemsArea/Viewport/Content/ItemSlot0").gameObject, hover, ExecuteEvents.pointerEnterHandler);
                    if (page == "secondary-stats")
                    {
                        ExecuteEvents.Execute(Get<GameObject>(ui, "Tooltip").transform.Find("Close").gameObject,
                            new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                        ExecuteEvents.Execute(layoutPanel.transform.Find("StatsBoard/Secondary").gameObject,
                            new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                    }
                    Canvas.ForceUpdateCanvases();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Assert.AreEqual(width, camera.pixelWidth); Assert.AreEqual(height, camera.pixelHeight);
                    if (page != "combat")
                    {
                        GameObject panel = Get<GameObject>(ui, "Panel");
                        Assert.IsTrue(panel.activeInHierarchy);
                        Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                        foreach (Component label in panel.GetComponentsInChildren(textType))
                            Assert.IsFalse(Get<bool>(label, "isTextOverflowing"), page + "/" + label.name + " at " + width);
                        for (int card = 0; card < 4; card++)
                        {
                            Image icon = panel.transform.Find("Offer" + card + "/Icon").GetComponent<Image>();
                            Assert.IsNotNull(icon.sprite);
                            Assert.Greater(icon.color.a, .99f, "商品和属性图标不能因底图透明而消失。");
                        }
                        foreach (Button button in panel.GetComponentsInChildren<Button>())
                        {
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
                }
            }
            finally
            {
                camera.targetTexture = null; camera.cullingMask = originalMask;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.sortingOrder = originalOrder; canvas.sortingLayerID = originalLayer;
                target.Release(); UnityEngine.Object.Destroy(target);
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
