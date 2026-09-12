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

        /// <summary>三页签、列表滚动、进阶不可购买、退款和返回焦点使用真实控件。</summary>
        [UnityTest]
        public IEnumerator RealMenu_TabsScrollPurchaseRefundAndReturnFocus()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            GameObject.Find("ShopButton").GetComponent<Button>().onClick.Invoke();
            Component shop = Find("AccountShopUI");
            Assert.IsTrue(Get<bool>(shop, "IsVisible"));
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopBasicTab"));
            yield return CaptureIfRequested("shop-basic");
            ScrollRect scroll = Field<ScrollRect>(shop, "scroll");
            Assert.That(scroll.content.childCount, Is.EqualTo(21));
            EventSystem.current.SetSelectedGameObject(scroll.content.GetChild(20).gameObject);
            yield return null;
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(0.1f));
            GameObject.Find("ShopAdvancedTab").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(GameObject.Find("ShopBuy").GetComponent<Button>().interactable);
            Assert.IsFalse(GameObject.Find("ShopRefund").GetComponent<Button>().interactable);
            yield return CaptureIfRequested("shop-advanced");
            GameObject.Find("ShopExclusionTab").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("ShopBuy").GetComponent<Button>().onClick.Invoke();
            Assert.That(Get<int>(_account, "SealCapacity"), Is.EqualTo(2));
            yield return CaptureIfRequested("shop-exclusions");
            GameObject.Find("ShopRefund").GetComponent<Button>().onClick.Invoke();
            Assert.That(Get<int>(_account, "SealCapacity"), Is.EqualTo(1));
            GameObject.Find("ShopBasicTab").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("ShopBuy").GetComponent<Button>().onClick.Invoke();
            Assert.That((int)Call(_account, "GetUpgradeLevel", "account_maxhealth"), Is.EqualTo(1));
            GameObject.Find("ShopRefund").GetComponent<Button>().onClick.Invoke();
            Assert.That(Get<int>(_account, "Gold"), Is.EqualTo(10000));
            GameObject.Find("ShopBack").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(Get<bool>(shop, "IsVisible"));
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ShopButton"));
            GameObject.Find("CollectionButton").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("UpgradeTab").GetComponent<Button>().onClick.Invoke();
            Assert.IsNull(GameObject.Find("SealButton"));
            GameObject.Find("CollectionBackButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(GameObject.Find("StartButton").GetComponent<Button>().interactable);
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
                moveVector = direction == MoveDirection.Down ? Vector2.down : Vector2.up
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
            Assert.IsFalse(GameObject.Find("ShopBuy").GetComponent<Button>().interactable);
            Assert.That(Get<string>(Field<object>(Find("AccountShopUI"), "detailText"), "text"), Does.Contain("金币不足"));
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
            Assert.That(max, Is.EqualTo(110));
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
            Assert.That(Get<float>(Find("PlayerStats"), "MaxHealth"), Is.EqualTo(150));

        }

        /// <summary>真实升级页在 Seal 与 Banish 清空候选时继续队列；解封后出现一张可选卡并恢复游戏。</summary>
        [UnityTest]
        public IEnumerator SealAndBanish_EmptyQueueContinues_UnsealRestoresRealCard()
        {
            yield return SceneManager.LoadSceneAsync("MainLevel"); yield return null;
            object manager = Find("LevelUpManager");
            object state = Find("RunState");
            IList upgrades = Field<IList>(manager, "allAvailableUpgrades");
            Assert.That(upgrades.Count, Is.GreaterThan(1));
            string sealedId = (string)Call(upgrades[0], "GetStableId");
            Call(_account, "DiscoverUpgrade", sealedId);
            Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", sealedId, true));
            for (int i = 1; i < upgrades.Count; i++) Call(state, "BanishUpgrade", Call(upgrades[i], "GetStableId"));
            object stats = Find("PlayerStats");
            RuntimeComponentTestUtility.SetField(stats, "_levelUpQueue", 2);
            Call(stats, "CheckLevelUpQueue");
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Field<int>(stats, "_levelUpQueue"), Is.Zero);
            Assert.IsFalse(Field<GameObject>(manager, "levelUpPanel").activeSelf);
            Assert.IsTrue((bool)Call(_account, "TrySetUpgradeSealed", sealedId, false));
            Call(manager, "ShowLevelUpUI");
            yield return null;
            Assert.IsTrue(Field<GameObject>(manager, "levelUpPanel").activeSelf);
            Assert.That(Time.timeScale, Is.Zero);
            IList candidates = Field<IList>(manager, "_currentCandidates");
            Assert.That(candidates.Count, Is.EqualTo(1));
            Call(manager, "HandleCandidateSelected", candidates[0]);
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.IsFalse(Field<GameObject>(manager, "levelUpPanel").activeSelf);
            Assert.IsTrue((bool)Call(state, "IsBanished", Call(upgrades[1], "GetStableId")));
        }

        /// <summary>仅显式图形验证运行保存截图；无图形门禁不冒充视觉验收。</summary>
        private static IEnumerator CaptureIfRequested(string name)
        {
            bool requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-session22Capture") >= 0;
            string requestedPage = "shop-basic";
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("-session22CaptureOnly=")) requestedPage = argument.Substring("-session22CaptureOnly=".Length);
            // 每个进程只捕获一个页面，避免 Unity 批处理复用 Canvas 绘制批次造成静态控件漏绘。
            if (!requested || requestedPage != name) yield break;
            yield return null;
            Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var cameraObject = new GameObject("Session22CaptureCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 5;
            var render = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderMode mode = canvas.renderMode;
            Camera priorCamera = canvas.worldCamera;
            float distance = canvas.planeDistance;
            RenderTexture priorTarget = RenderTexture.active;
            try
            {
                camera.targetTexture = render;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                canvas.enabled = false;
                canvas.enabled = true;
                foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
                foreach (Component text in canvas.GetComponentsInChildren(textType, true))
                    RuntimeComponentTestUtility.Invoke(text, "ForceMeshUpdate", true, true);
                foreach (CanvasRenderer renderer in canvas.GetComponentsInChildren<CanvasRenderer>()) renderer.cull = false;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = render;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/session22-" + name + ".png");
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = priorCamera;
                canvas.planeDistance = distance;
                RenderTexture.active = priorTarget;
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(image);
                render.Release();
                UnityEngine.Object.Destroy(render);
            }

        }

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
