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
    /// <summary>通过真实场景与反射边界验证商店和局内启动链；全部账号隔离。</summary>
    public sealed class AccountShopPlayModeTests
    {
        private object _account;

        /// <summary>使用内存账号，绝不访问真实用户存档。</summary>
        [SetUp]
        public void Setup()
        {
            Type service = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
            object storage = Activator.CreateInstance(RuntimeComponentTestUtility.RequireRuntimeType("InMemoryAccountProgressStorage"));
            RuntimeComponentTestUtility.InvokeStatic(service, "SetStorageForTests", storage);
            _account = service.GetProperty("Current").GetValue(null);
            RuntimeComponentTestUtility.Invoke(_account, "RecordRunResults", 10000, 100);
            Time.timeScale = 1;
        }

        /// <summary>卸载正式场景后恢复隔离账号，避免场景系统跨测试运行。</summary>
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            Scene empty = SceneManager.CreateScene("Session22Empty" + Guid.NewGuid().ToString("N"));
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(empty);
            if (previous.IsValid() && previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
            RuntimeComponentTestUtility.InvokeStatic(RuntimeComponentTestUtility.RequireRuntimeType("CharacterSelectionSession"), "Clear");
            Type service = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
            RuntimeComponentTestUtility.InvokeStatic(service, "SetStorageForTests",
                Activator.CreateInstance(RuntimeComponentTestUtility.RequireRuntimeType("InMemoryAccountProgressStorage")));
        }

        /// <summary>核心15卡与次要资源7卡、三列滚动、买退与免费排除均走新两阶段事件链。</summary>
        [UnityTest]
        public IEnumerator RealMenu_TabsScrollPurchaseRefundAndReturnFocus()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Submit(GameObject.Find("ShopButton"));
            Component shop = Find("AccountShopUI");
            ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
            Assert.That(Get<int>(shop, "ActiveEntryCount"), Is.EqualTo(15));
            Assert.That(scroll.content.GetComponent<GridLayoutGroup>().constraintCount, Is.EqualTo(3));
            Assert.IsNotNull(scroll.verticalScrollbar);
            Assert.IsFalse(Field<Button>(shop, "buyButton").gameObject.activeSelf);
            MoveFocus(EventSystem.current, MoveDirection.Right);
            for (int i = 0; i < 4; i++) MoveFocus(EventSystem.current, MoveDirection.Down);
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopCard_12"));
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_range"));
            AssertFocusedCardInside(scroll);
            Submit(GameObject.Find("ShopAdvancedTab"));
            Assert.That(Get<int>(shop, "ActiveEntryCount"), Is.EqualTo(7));
            EventSystem.current.SetSelectedGameObject(scroll.content.GetChild(6).gameObject);
            Submit(); yield return null;
            Assert.That(State(shop), Is.EqualTo("Locked"));
            Assert.That(Get<int>(_account, "SealCapacity"), Is.EqualTo(1));
            Submit(); yield return null;
            Assert.That(Get<int>(_account, "SealCapacity"), Is.EqualTo(2));
            MoveFocus(EventSystem.current, MoveDirection.Right); Submit(); yield return null;
            Assert.That(Get<int>(_account, "SealCapacity"), Is.EqualTo(1));
            Assert.That(State(shop), Is.EqualTo("Locked"));
            Cancel(); yield return null;
            Submit(GameObject.Find("ShopAdvancedTab"));
            Assert.That(Get<int>(shop, "ActiveEntryCount"), Is.EqualTo(7));
            Assert.IsFalse(Field<Button>(shop, "buyButton").gameObject.activeSelf);
            Submit(scroll.content.GetChild(0).gameObject); yield return null;
            Assert.IsTrue(Field<Button>(shop, "buyButton").interactable);
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_growth"));
            Cancel(); yield return null;
            object content = Field<object>(shop, "contentCatalog");
            IList upgrades = (IList)Get<object>(content, "Upgrades");
            string id = (string)Call(upgrades[0], "GetStableId");
            Call(_account, "DiscoverUpgrade", id);
            Submit(GameObject.Find("ShopExclusionTab"));
            Assert.That(Get<int>(shop, "ActiveEntryCount"), Is.EqualTo(1));
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo(id));
            Submit(scroll.content.GetChild(0).gameObject); yield return null;
            Assert.IsFalse(Field<Button>(shop, "refundButton").gameObject.activeSelf);
            Submit(); yield return null;
            Assert.IsTrue((bool)Call(_account, "IsUpgradeSealed", id));
            Submit(); yield return null;
            Assert.IsFalse((bool)Call(_account, "IsUpgradeSealed", id));
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
            Cancel(); yield return null; Cancel(); yield return null;
            Assert.IsFalse(Get<bool>(shop, "IsVisible"));
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopButton"));
            Submit(GameObject.Find("CollectionButton"));
            Submit(GameObject.Find("UpgradeTab"));
            Assert.IsNull(GameObject.Find("SealButton"));
            Submit(GameObject.Find("CollectionBackButton")); yield return null;
            Assert.IsTrue(GameObject.Find("StartButton").GetComponent<Button>().interactable);
        }

        /// <summary>悬停只预览，确认只锁定；锁定后的导航可滚动但不偷偷改变交易目标。</summary>
        [UnityTest]
        public IEnumerator PointerAndNavigation_LockDoesNotBuy_CancelUnwindsOneLayer()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Submit(GameObject.Find("ShopButton"));
            Component shop = Find("AccountShopUI");
            ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
            GameObject first = scroll.content.GetChild(0).gameObject;
            GameObject second = scroll.content.GetChild(1).gameObject;
            Hover(second);
            Assert.That(State(shop), Is.EqualTo("Browsing"));
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_recovery"));
            Assert.IsFalse(Field<Button>(shop, "buyButton").gameObject.activeSelf);
            Click(first);
            Assert.That(State(shop), Is.EqualTo("Locked"));
            Submit(); // 同帧对已转移焦点的按钮再次发事件也不得购买。
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
            Hover(second);
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_maxhealth"));
            Click(first); yield return null;
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
            Click(second); yield return null;
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_recovery"));
            Submit(); yield return null;
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(9900));
            Assert.That(State(shop), Is.EqualTo("Locked"));
            MoveFocus(EventSystem.current, MoveDirection.Up);
            for (int i = 0; i < 4; i++) MoveFocus(EventSystem.current, MoveDirection.Down);
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopCard_13"));
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_recovery"));
            AssertFocusedCardInside(scroll);
            Submit(); yield return null;
            Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_dodge"));
            Cancel(); Cancel(); // 同帧去重：第二个 Cancel 不能关闭页面。
            Assert.That(State(shop), Is.EqualTo("Browsing"));
            Assert.IsTrue(Get<bool>(shop, "IsVisible"));
            yield return null;
            Cancel(); yield return null;
            Assert.IsFalse(Get<bool>(shop, "IsVisible"));
            Submit(Field<Button>(shop, "buyButton").gameObject);
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(9900));
        }

        /// <summary>满级价格、买退锁定和切页清理保持一致，隐藏按钮不能交易。</summary>
        [UnityTest]
        public IEnumerator MaxLevelAndTabChange_RefreshCardsAndPreventHiddenPurchase()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Submit(GameObject.Find("ShopButton"));
            Component shop = Find("AccountShopUI");
            ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
            Submit(scroll.content.GetChild(0).gameObject); yield return null;
            for (int i = 0; i < 3; i++) { Submit(Field<Button>(shop, "buyButton").gameObject); yield return null; }
            Assert.That(State(shop), Is.EqualTo("Locked"));
            Assert.IsFalse(Field<Button>(shop, "buyButton").interactable);
            Assert.That(Text(shop, "statusText"), Does.Contain("已满级"));
            Component entry = scroll.content.GetChild(0).GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("AccountShopEntryUI"));
            Assert.That(Get<int>(entry, "VisibleLevelCount"), Is.EqualTo(3));
            Assert.That(Get<string>(Field<object>(entry, "label"), "text"), Is.EqualTo("已满级"));
            Submit(Field<Button>(shop, "refundButton").gameObject); yield return null;
            Assert.That((int)Call(_account, "GetUpgradeLevel", "account_maxhealth"), Is.EqualTo(2));
            Assert.That(Get<string>(Field<object>(entry, "label"), "text"), Is.EqualTo("300"));
            Assert.That(State(shop), Is.EqualTo("Locked"));
            int gold = Get<int>(_account, "Gold");
            Submit(GameObject.Find("ShopAdvancedTab"));
            Assert.That(State(shop), Is.EqualTo("Browsing"));
            Submit(Field<Button>(shop, "buyButton").gameObject);
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(gold));
        }

        /// <summary>从默认 Start 经真实方向与提交事件进入商店，返回后验证焦点及上下循环。</summary>
        [UnityTest]
        public IEnumerator DefaultFocus_MoveAndSubmit_OpensShopAndReturnsToNavigationRing()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            EventSystem events = EventSystem.current;
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("StartButton"));
            MoveFocus(events, MoveDirection.Down);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("CollectionButton"));
            MoveFocus(events, MoveDirection.Down);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("ShopButton"));
            ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
            yield return null;
            Assert.IsTrue(Get<bool>(Find("AccountShopUI"), "IsVisible"));
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("ShopBasicTab"));
            // 返回按钮同样经过 Button.OnSubmit，不绕过按钮调用业务方法或 onClick。
            events.SetSelectedGameObject(GameObject.Find("ShopBack"));
            ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
            yield return null;
            Assert.IsFalse(Get<bool>(Find("AccountShopUI"), "IsVisible"));
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("ShopButton"));
            MoveFocus(events, MoveDirection.Down);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("QuitButton"));
            MoveFocus(events, MoveDirection.Down);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("StartButton"));
            MoveFocus(events, MoveDirection.Up);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("QuitButton"));
            MoveFocus(events, MoveDirection.Up);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("ShopButton"));
        }

        /// <summary>发送与键盘/手柄导航共用的 Move 事件，检查实际序列化导航而非直接选择目标。</summary>
        private static void MoveFocus(EventSystem events, MoveDirection direction)
        {
            var movement = new AxisEventData(events)
            {
                moveDir = direction,
                moveVector = direction == MoveDirection.Down ? Vector2.down : direction == MoveDirection.Up ? Vector2.up : direction == MoveDirection.Left ? Vector2.left : Vector2.right
            };
            ExecuteEvents.Execute(events.currentSelectedGameObject, movement, ExecuteEvents.moveHandler);
        }

        /// <summary>金币不足时按钮禁用且详情说明原因，玩家无需点击禁用按钮才能知道结果。</summary>
        [UnityTest]
        public IEnumerator InsufficientGold_ShowsReasonOnDisabledPurchase()
        {
            Type service = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
            RuntimeComponentTestUtility.InvokeStatic(service, "SetStorageForTests",
                Activator.CreateInstance(RuntimeComponentTestUtility.RequireRuntimeType("InMemoryAccountProgressStorage")));
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            GameObject.Find("ShopButton").GetComponent<Button>().onClick.Invoke();
            Component shop = Find("AccountShopUI");
            Assert.IsFalse(Field<Button>(shop, "buyButton").gameObject.activeSelf);
            Assert.That(Text(shop, "statusText"), Does.Contain("金币不足"));
            Submit(Field<ScrollRect>(shop, "scroll").content.GetChild(0).gameObject);
            Assert.IsFalse(Field<Button>(shop, "buyButton").interactable);
        }

        /// <summary>购买后直接进入 MainLevel，验证生命与四种资源；重算不补次数，重开只获得一份成长。</summary>
        [UnityTest]
        public IEnumerator PurchasedStats_DirectSceneAndRestart_InitializeHealthAndCountsOnce()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            object catalog = Field<object>(Find("AccountShopUI"), "catalog");
            foreach (string id in new[] { "account_maxhealth", "account_revival", "account_reroll", "account_skip", "account_banish" })
                Assert.IsTrue((bool)Call(_account, "TryPurchaseUpgrade", catalog, id));
            yield return SceneManager.LoadSceneAsync("MainLevel"); yield return null;
            Component stats = Find("PlayerStats");
            Component health = stats.GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("PlayerHealth"));
            object state = Find("RunState");
            float max = Get<float>(stats, "MaxHealth");
            Assert.That(max, Is.EqualTo(13));
            Assert.That(Get<float>(health, "CurrentHealth"), Is.EqualTo(max));
            string[] names = { "Revivals", "Rerolls", "Skips", "Banishes" };
            int[] before = new int[4];
            for (int i = 0; i < names.Length; i++) before[i] = Get<int>(state, "Remaining" + names[i]);
            foreach (string resource in new[] { "Revival", "Reroll", "Skip", "Banish" }) Assert.IsTrue((bool)Call(state, "TryConsume" + resource));
            object character = Get<object>(stats, "CharacterData");
            Call(stats, "SetCharacterData", character);
            for (int i = 0; i < names.Length; i++) Assert.That(Get<int>(state, "Remaining" + names[i]), Is.EqualTo(before[i] - 1), names[i]);
            Assert.That(Get<float>(stats, "MaxHealth"), Is.EqualTo(max));
            yield return SceneManager.LoadSceneAsync("MainLevel"); yield return null;
            Assert.That(Get<float>(Find("PlayerStats"), "MaxHealth"), Is.EqualTo(max));
            Assert.That(Get<int>(Find("RunState"), "RemainingRevivals"), Is.EqualTo(1));
            // 使用真实选角卡和确认按钮进入另一个角色，验证账号成长共享且不沿用旧角色基础值。
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            object content = Field<object>(Find("AccountShopUI"), "contentCatalog");
            IList characters = (IList)Get<object>(content, "Characters");
            object warrior = characters[1];
            Assert.IsTrue((bool)Call(_account, "IsCharacterUnlocked", RuntimeComponentTestUtility.GetFieldValue<string>(warrior, "characterID")));
            GameObject.Find("StartButton").GetComponent<Button>().onClick.Invoke();
            ExecuteEvents.Execute(GameObject.Find("CharacterSlot_02"), new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            GameObject.Find("CharacterConfirmButton").GetComponent<Button>().onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 15;
            while (SceneManager.GetActiveScene().name != "MainLevel" && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainLevel"));
            Assert.That(Get<float>(Find("PlayerStats"), "MaxHealth"), Is.EqualTo(17));

        }

        /// <summary>封印过滤局内商店；属性放逐耗尽时回合升级队列可继续，解封恢复合法商品。</summary>
        [UnityTest]
        public IEnumerator SealAndBanish_ShopExclusionsDoNotDiscardStatQueue()
        {
            yield return SceneManager.LoadSceneAsync("MainLevel"); yield return null; yield return null;
            object rounds = Find("RoundController");
            object state = Find("RunState");
            object stats = Find("PlayerStats");
            object config = Field<object>(rounds, "config");
            object catalog = Field<object>(config, "shopCatalog");
            IList products = Field<IList>(catalog, "products");
            string sealedId = Get<string>(products[0], "Id");
            Call(_account, "DiscoverUpgrade", sealedId);
            Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", sealedId, true));
            foreach (object option in Field<IList>(catalog, "stats"))
                Call(state, "BanishUpgrade", Field<string>(option, "id"));
            RuntimeComponentTestUtility.SetField(stats, "_levelUpQueue", 2);
            RuntimeComponentTestUtility.SetField(stats, "currentLevel", 3);
            Call(stats, "CheckLevelUpQueue");
            Assert.AreEqual(2, Get<int>(stats, "PendingLevelUps"), "战斗中不能弹出或消费属性选择。");
            Call(rounds, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(rounds);
            yield return null; yield return null;
            Assert.AreEqual(2, Get<int>(stats, "PendingLevelUps"), "本局放逐集合不再移除属性升级选项。");
            Assert.AreEqual("Upgrades", Get<object>(rounds, "Phase").ToString());
            for (int i = 0; i < 2; i++)
            { Assert.IsTrue((bool)Call(rounds, "Choose", 0)); yield return null; }
            Assert.AreEqual(0, Get<int>(stats, "PendingLevelUps"));
            Assert.AreEqual("Shop", Get<object>(rounds, "Phase").ToString());
            object shop = Get<object>(rounds, "Shop");
            foreach (object offer in Get<IList>(shop, "Offers"))
                if (offer != null) Assert.AreNotEqual(sealedId, Get<string>(Get<object>(offer, "Product"), "Id"));
            Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", sealedId, false));
            // 用测试局部目录收窄候选以确定性验证解封，不依赖反复随机刷新。
            ScriptableObject clonedCatalog = UnityEngine.Object.Instantiate((ScriptableObject)catalog);
            IList cloneProducts = Field<IList>(clonedCatalog, "products");
            object first = cloneProducts[0]; cloneProducts.Clear(); cloneProducts.Add(first);
            Type serviceType = RuntimeComponentTestUtility.RequireRuntimeType("RunShopService");
            object isolated = Activator.CreateInstance(serviceType, clonedCatalog, Get<object>(rounds, "Wallet"),
                Get<object>(rounds, "Loadout"), Get<object>(rounds, "Items"), stats, new Func<bool>(() => true));
            Call(isolated, "Enter", 1);
            Assert.AreEqual(sealedId, Get<string>(Get<object>(Get<IList>(isolated, "Offers")[0], "Product"), "Id"));
            UnityEngine.Object.Destroy(clonedCatalog);
        }

        /// <summary>已占用槽位和只读账号提前禁用按钮并显示原因，迟到确认不能绕过限制。</summary>
        [UnityTest]
        public IEnumerator UnavailableActions_ShowReadonlyAndOccupiedSlotReasons()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Component shop = Find("AccountShopUI");
            Assert.IsTrue((bool)Call(_account, "TryPurchaseUpgrade", Field<object>(shop, "catalog"), "account_seal_slots"));
            foreach (string id in new[] { "occupied_a", "occupied_b" })
            {
                Call(_account, "DiscoverUpgrade", id);
                Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", id, true));
            }
            Submit(GameObject.Find("ShopButton"));
            Submit(GameObject.Find("ShopAdvancedTab"));
            Submit(Field<ScrollRect>(shop, "scroll").content.GetChild(6).gameObject); yield return null;
            Assert.IsFalse(Field<Button>(shop, "refundButton").interactable);
            Assert.That(Text(shop, "statusText"), Does.Contain("先解除排除"));
            int gold = Get<int>(_account, "Gold");
            Submit(Field<Button>(shop, "refundButton").gameObject);
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(gold));
            Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", "occupied_a", false));
            Assert.IsTrue(Field<Button>(shop, "refundButton").interactable);
            // 服务的只读存储载入行为另由 EditMode 覆盖；此处仅注入状态验证真实页面的按钮与解释。
            RuntimeComponentTestUtility.SetField(_account, "_isReadOnly", true);
            Call(shop, "Refresh");
            Assert.IsFalse(Field<Button>(shop, "buyButton").interactable);
            Assert.IsFalse(Field<Button>(shop, "refundButton").interactable);
            Assert.That(Text(shop, "statusText"), Does.Contain("账号只读"));
            Submit(Field<Button>(shop, "buyButton").gameObject);
            Submit(Field<Button>(shop, "refundButton").gameObject);
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(gold));
        }

        /// <summary>在全高清参考尺寸验证三列、方形图标和详情文本，并按显式参数输出截图。</summary>
        [UnityTest]
        public IEnumerator Layout_1920x1080() { yield return VerifyLayout(1920, 1080); }

        /// <summary>在720p验证缩放后的实际布局，防止小分辨率下卡片或操作文字溢出。</summary>
        [UnityTest]
        public IEnumerator Layout_1280x720() { yield return VerifyLayout(1280, 720); }

        /// <summary>1080p检查启用/停用、零级/已购四种锁定详情布局。</summary>
        [UnityTest]
        public IEnumerator PreferencesLayout_1920x1080() { yield return VerifyLayout(1920, 1080, false, true); }

        /// <summary>720p复验勾选与退款可见状态，截图只在显式图形参数下生成。</summary>
        [UnityTest]
        public IEnumerator PreferencesLayout_1280x720() { yield return VerifyLayout(1280, 720, false, true); }

        /// <summary>真实Toggle确认免费且保持锁定，隐藏退款后焦点有效；四种主动资源不显示开关。</summary>
        [UnityTest]
        public IEnumerator EnabledPreference_RealToggleRefundNavigationAndCancel()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Submit(GameObject.Find("ShopButton"));
            Component shop = Find("AccountShopUI");
            Toggle toggle = Field<Toggle>(shop, "enabledToggle");
            ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
            Assert.IsFalse(toggle.gameObject.activeSelf);
            Assert.That(Text(shop, "statusText"), Is.Empty);
            Submit(scroll.content.GetChild(0).gameObject);
            Submit(toggle.gameObject); // 锁定同帧的迟到确认不能更改偏好。
            Assert.IsTrue(toggle.isOn);
            yield return null;
            MoveFocus(EventSystem.current, MoveDirection.Left);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(toggle.gameObject));
            Submit(); yield return null;
            Assert.IsFalse(toggle.isOn);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(toggle.gameObject));
            Assert.IsFalse((bool)Call(_account, "IsAccountUpgradeEnabled", "account_maxhealth"));
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
            Assert.That(State(shop), Is.EqualTo("Locked"));
            Assert.IsFalse(Field<Button>(shop, "refundButton").gameObject.activeSelf);
            MoveFocus(EventSystem.current, MoveDirection.Right); Submit(); yield return null;
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(9900));
            Assert.IsFalse(toggle.isOn);
            Assert.IsTrue(Field<Button>(shop, "refundButton").gameObject.activeSelf);
            Assert.That(Text(shop, "statusText"), Is.Empty);
            MoveFocus(EventSystem.current, MoveDirection.Right); Submit(); yield return null;
            Assert.IsFalse(Field<Button>(shop, "refundButton").gameObject.activeSelf);
            Assert.IsTrue(EventSystem.current.currentSelectedGameObject.activeInHierarchy);
            Assert.IsFalse(toggle.isOn);
            Cancel(); yield return null;
            Assert.IsFalse(toggle.gameObject.activeSelf);
            Submit(GameObject.Find("ShopAdvancedTab"));
            foreach (int index in new[] { 3, 4, 5, 6 })
            {
                Submit(scroll.content.GetChild(index).gameObject); yield return null;
                Assert.IsFalse(toggle.gameObject.activeSelf);
                if (index == 6) Assert.That(Text(shop, "detailName"), Is.EqualTo("封印"));
            }
            Submit(scroll.content.GetChild(2).gameObject); yield return null;
            Assert.IsTrue(toggle.gameObject.activeSelf, "复活应可停用");
            Submit(toggle.gameObject); yield return null;
            Assert.IsFalse(toggle.isOn);
            Cancel(); yield return null; Cancel(); yield return null;
            Assert.IsFalse(Get<bool>(shop, "IsVisible"));
        }

        /// <summary>真实存储在载入后受到未来版本保护而拒绝保存，勾选必须回滚并保留失败提示。</summary>
        [UnityTest]
        public IEnumerator EnabledPreference_SaveFailureRestoresCheckbox()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ShopPreferenceFailure-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                object storage = Activator.CreateInstance(RuntimeComponentTestUtility.RequireRuntimeType("JsonAccountProgressStorage"), directory);
                Type service = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
                RuntimeComponentTestUtility.InvokeStatic(service, "SetStorageForTests", storage);
                _account = service.GetProperty("Current").GetValue(null);
                Call(_account, "RecordRunResults", 10000, 0);
                yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
                Submit(GameObject.Find("ShopButton"));
                Component shop = Find("AccountShopUI");
                Submit(Field<ScrollRect>(shop, "scroll").content.GetChild(0).gameObject); yield return null;
                // 模拟加载后文件被新版客户端写入；旧客户端保存保护应拒绝覆盖。
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "account-progress.json"), "{\"saveVersion\":999}");
                Toggle toggle = Field<Toggle>(shop, "enabledToggle");
                Submit(toggle.gameObject); yield return null;
                Assert.IsTrue(toggle.isOn);
                Assert.IsTrue((bool)Call(_account, "IsAccountUpgradeEnabled", "account_maxhealth"));
                Assert.That(Text(shop, "statusText"), Does.Contain("保存失败"));
                Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
                Assert.That(State(shop), Is.EqualTo("Locked"));
                Assert.That(System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "account-progress.json")), Is.EqualTo("{\"saveVersion\":999}"));
            }
            finally { System.IO.Directory.Delete(directory, true); }
        }

        /// <summary>测试专用相机在页面激活前固定画布尺寸；每页重新加载场景，截图不修改生产资产。</summary>
        private IEnumerator VerifyLayout(int width, int height, bool scrollOnly = false, bool preferencesOnly = false)
        {
            string[] args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-session22ShopScreenshots");
            string directory = flag >= 0 && flag + 1 < args.Length ? args[flag + 1] : null;
            foreach (string page in preferencesOnly ? new[] { "preference0-on", "preference0-off", "preference1-on", "preference1-off" } : scrollOnly ? new[] { "scroll" } : new[] { "basic", "locked", "advanced", "exclusion", "slots" })
            {
                if (preferencesOnly) Setup();
                yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
                Component shop = Find("AccountShopUI");
                Canvas canvas = shop.GetComponent<Canvas>();
                var cameraObject = new GameObject("Session22LayoutCamera", typeof(Camera));
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.orthographic = true;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100;
                var target = new RenderTexture(width, height, 24);
                camera.targetTexture = target;
                canvas.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                canvas.enabled = true;
                Submit(GameObject.Find("ShopButton"));
                ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
                if (preferencesOnly)
                {
                    Submit(scroll.content.GetChild(0).gameObject); yield return null;
                    bool purchased = page.Contains("preference1");
                    if (purchased) { Submit(Field<Button>(shop, "buyButton").gameObject); yield return null; }
                    if (page.EndsWith("off")) { Submit(Field<Toggle>(shop, "enabledToggle").gameObject); yield return null; }
                    Assert.That(Field<Toggle>(shop, "enabledToggle").isOn, Is.EqualTo(page.EndsWith("on")));
                    Assert.That(Field<Toggle>(shop, "enabledToggle").graphic.canvasRenderer.GetAlpha(), Is.EqualTo(page.EndsWith("on") ? 1 : 0).Within(0.01f));
                    Assert.That(Field<Button>(shop, "refundButton").gameObject.activeSelf, Is.EqualTo(purchased));
                    Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(purchased ? 9900 : 10000));
                    Assert.That(Text(shop, "statusText"), Is.Empty);
                }
                if (page == "scroll")
                {
                    MoveFocus(EventSystem.current, MoveDirection.Right);
                    for (int state = 0; state < 2; state++)
                    {
                        if (state == 1)
                        {
                            Submit(); yield return null;
                            MoveFocus(EventSystem.current, MoveDirection.Up);
                        }
                        string lockedId = Get<string>(shop, "SelectedId");
                        AssertFocusedCardInside(scroll);
                        for (int direction = 0; direction < 2; direction++)
                        {
                            for (int step = 0; step < 4; step++)
                            {
                                MoveFocus(EventSystem.current, direction == 0 ? MoveDirection.Down : MoveDirection.Up);
                                yield return null;
                                AssertFocusedCardInside(scroll);
                                if (state == 1)
                                {
                                    Assert.That(State(shop), Is.EqualTo("Locked"));
                                    Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo(lockedId));
                                }
                            }
                            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo(direction == 0 ? "ShopCard_12" : "ShopCard_0"));
                        }
                    }
                    Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
                    // 补图停在从底部返回的第9张卡，直接呈现原来被裁切半张卡的位置。
                    for (int step = 0; step < 4; step++) MoveFocus(EventSystem.current, MoveDirection.Down);
                    for (int step = 0; step < 1; step++) MoveFocus(EventSystem.current, MoveDirection.Up);
                    AssertFocusedCardInside(scroll);
                    Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopCard_9"));
                    Assert.That(Get<string>(shop, "SelectedId"), Is.EqualTo("account_maxhealth"));
                }
                if (page == "basic")
                {
                    // 本轮全部描述均改为具体效果，逐项验证长中文不会挤出详情区域。
                    foreach (Transform card in scroll.content)
                    {
                        Hover(card.gameObject); yield return null;
                        Assert.IsFalse(Get<bool>(Field<object>(shop, "detailText"), "isTextOverflowing"), card.name + " description at " + width);
                        ExecuteEvents.Execute(card.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
                    }
                    Hover(scroll.content.GetChild(0).gameObject);
                }
                if (page == "locked")
                {
                    Submit(scroll.content.GetChild(0).gameObject); yield return null;
                    Submit(Field<Button>(shop, "buyButton").gameObject);
                }
                if (page == "advanced") Submit(GameObject.Find("ShopAdvancedTab"));
                if (page == "exclusion")
                {
                    IList upgrades = (IList)Get<object>(Field<object>(shop, "contentCatalog"), "Upgrades");
                    for (int i = 0; i < Math.Min(6, upgrades.Count); i++) Call(_account, "DiscoverUpgrade", Call(upgrades[i], "GetStableId"));
                    Submit(GameObject.Find("ShopExclusionTab"));
                    Submit(scroll.content.GetChild(0).gameObject);
                }
                if (page == "slots")
                { Submit(GameObject.Find("ShopAdvancedTab")); Submit(scroll.content.GetChild(6).gameObject); }
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < 4; i++) yield return null;
                try
                {
                    Assert.That(camera.pixelWidth, Is.EqualTo(width));
                    Assert.That(camera.pixelHeight, Is.EqualTo(height));
                    Assert.That(scroll.content.GetComponent<GridLayoutGroup>().constraintCount, Is.EqualTo(3));
                    RectTransform first = (RectTransform)scroll.content.GetChild(0);
                    RectTransform iconFrame = (RectTransform)first.Find("IconSlot/IconFrame");
                    Assert.That(iconFrame.rect.width, Is.EqualTo(iconFrame.rect.height).Within(0.1f));
                    Assert.That(first.rect.width * 3 + 48, Is.LessThanOrEqualTo(scroll.viewport.rect.width + 0.1f));
                    foreach (string field in new[] { "detailName", "detailText", "statusText", "inputHints", "goldText", "buyLabel", "refundLabel" })
                        Assert.IsFalse(Get<bool>(Field<object>(shop, field), "isTextOverflowing"), page + "/" + field + " at " + width);
                    if (directory != null)
                    {
                        Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null), "截图必须使用图形模式。");
                        System.IO.Directory.CreateDirectory(directory);
                        RenderTexture previous = RenderTexture.active;
                        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                        try
                        {
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            pixels.Apply();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, page + "-" + width + "x" + height + ".png"), pixels.EncodeToPNG());
                        }
                        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
                    }
                }
                finally
                {
                    canvas.worldCamera = null;
                    camera.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                    UnityEngine.Object.Destroy(cameraObject);
                }
            }
        }

        /// <summary>1080p 浏览和锁定时经真实 Move 往返，逐次验证焦点卡四角完整可见。</summary>
        [UnityTest]
        public IEnumerator ScrollRoundTrip_1920x1080() { yield return VerifyLayout(1920, 1080, true); }

        /// <summary>720p 复验同一往返路径，锁定身份和余额不随滚动改变。</summary>
        [UnityTest]
        public IEnumerator ScrollRoundTrip_1280x720() { yield return VerifyLayout(1280, 720, true); }

        /// <summary>把焦点卡实际世界四角转换到视口局部空间，独立于生产代码的 pivot 算法。</summary>
        private static void AssertFocusedCardInside(ScrollRect scroll)
        {
            Canvas.ForceUpdateCanvases();
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            Assert.That(selected.name, Does.StartWith("ShopCard_"));
            var corners = new Vector3[4];
            ((RectTransform)selected.transform).GetWorldCorners(corners);
            Rect viewport = scroll.viewport.rect;
            foreach (Vector3 corner in corners)
            {
                Vector3 local = scroll.viewport.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(viewport.xMin - 0.5f, viewport.xMax + 0.5f), selected.name + " horizontal corner");
                Assert.That(local.y, Is.InRange(viewport.yMin - 0.5f, viewport.yMax + 0.5f), selected.name + " vertical corner");
            }
        }

        /// <summary>通过真实 Button.OnSubmit 确认当前焦点或指定控件。</summary>
        private static void Submit(GameObject target = null) { ExecuteEvents.Execute(target != null ? target : EventSystem.current.currentSelectedGameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); }
        /// <summary>通过选中控件的 ICancelHandler 返回，覆盖真实 EventSystem 路径。</summary>
        private static void Cancel() { ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler); }
        /// <summary>模拟鼠标悬停。</summary>
        private static void Hover(GameObject target) { ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler); }
        /// <summary>鼠标按下产生 OnSelect 后才触发点击，验证 OnSelect 不提前确认。</summary>
        private static void Click(GameObject target)
        {
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        }
        /// <summary>读取当前状态名称。</summary>
        private static string State(object shop) { return Get<object>(shop, "State").ToString(); }
        /// <summary>通过运行时 TMP 属性读取展示结果。</summary>
        private static string Text(object shop, string field) { return Get<string>(Field<object>(shop, field), "text"); }

        /// <summary>定位真实场景组件。</summary>
        private static Component Find(string type) { return UnityEngine.Object.FindObjectOfType(RuntimeComponentTestUtility.RequireRuntimeType(type)) as Component; }
        /// <summary>读取跨程序集属性。</summary>
        private static T Get<T>(object target, string name) { return RuntimeComponentTestUtility.GetProperty<T>(target, name); }
        /// <summary>读取跨程序集配置或测试诊断状态。</summary>
        private static T Field<T>(object target, string name) { return RuntimeComponentTestUtility.GetFieldValue<T>(target, name); }
        /// <summary>调用真实生产入口。</summary>
        private static object Call(object target, string name, params object[] args) { return RuntimeComponentTestUtility.Invoke(target, name, args); }
    }
}
