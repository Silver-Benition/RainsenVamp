using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在正式场景验证实际交易、装备生成及 UI 图像；账号使用内存存储。</summary>
    public sealed class ContentExpansionPlayModeTests
    {
        private object _round, _items, _loadout, _shop;
        private IList _products;

        /// <summary>打开正式场景并等待首波依赖就绪。</summary>
        [UnitySetUp] public IEnumerator Setup()
        {
            Static("AccountProgressService", "SetStorageForTests", Activator.CreateInstance(TypeOf("InMemoryAccountProgressStorage")));
            yield return SceneManager.LoadSceneAsync("MainLevel");
            for (int i = 0; i < 6; i++) yield return null;
            _round = UnityEngine.Object.FindObjectOfType(TypeOf("RoundController"));
            _items = Get<object>(_round, "Items"); _loadout = Get<object>(_round, "Loadout"); _shop = Get<object>(_round, "Shop");
            _products = (IList)Field(Field(_round, "config"), "shopCatalog").GetType().GetField("products").GetValue(Field(Field(_round, "config"), "shopCatalog"));
            Component player = (Component)Get<object>(_round, "Player");
            object health = player.GetComponent(TypeOf("PlayerHealth"));
            health.GetType().GetField("_nextDamageAllowedTime", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(health, float.MaxValue);
            Call(Get<object>(_round, "Wallet"), "Credit", 100000);
            yield return EnterShop();
        }

        /// <summary>卸载场景，释放池和事件订阅，然后重置内存账号。</summary>
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            Scene empty = SceneManager.CreateScene("ExpansionEmpty"); SceneManager.SetActiveScene(empty);
            Scene main = SceneManager.GetSceneByName("MainLevel"); if (main.isLoaded) yield return SceneManager.UnloadSceneAsync(main);
            Static("AccountProgressService", "SetStorageForTests", Activator.CreateInstance(TypeOf("InMemoryAccountProgressStorage")));
        }

        /// <summary>所有已审阅道具均能通过实际商店购买；不限容量且暂停背包显示全部图像。</summary>
        [UnityTest] public IEnumerator ReviewedItems_BuyAllTwentyAndShowInventory()
        {
            int count = 0;
            foreach (object product in _products)
            {
                object data = Field(Field(product, "content"), "abilityToGrant");
                if (data == null || !(bool)Field(data, "stackPerCopy")) continue;
                SeedOffer(product, (int)Field(data, "quality"));
                Assert.IsTrue((bool)Call(_shop, "Buy", 0), (string)Call(data, "GetDisplayName"));
                Assert.NotNull(Call(_items, "GetOwnedAbility", data)); count++;
            }
            Assert.AreEqual(20, count); Assert.AreEqual(20, Get<int>(_items, "OwnedAbilityCount"));
            Component ui=(Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Call(ui,"Refresh");yield return null;Canvas.ForceUpdateCanvases();
            ScrollRect shopScroll=Get<GameObject>(ui,"Panel").transform.Find("ItemsArea").GetComponent<ScrollRect>();
            shopScroll.verticalNormalizedPosition=0;yield return null;
            Bounds last=RectTransformUtility.CalculateRelativeRectTransformBounds(shopScroll.viewport,shopScroll.content.GetChild(19));
            Assert.GreaterOrEqual(last.min.y,shopScroll.viewport.rect.yMin-1);
            Assert.LessOrEqual(last.max.y,shopScroll.viewport.rect.yMax+1);
            yield return Capture("items-shop");
            Assert.IsTrue((bool)Call(_round, "BeginNextRound")); yield return null;
            object flow = UnityEngine.Object.FindObjectOfType(TypeOf("GameFlowManager")); Call(flow, "PauseGame"); yield return null;
            Image[] images = UnityEngine.Object.FindObjectsOfType<Image>(); int itemIcons = 0;
            foreach (Image image in images)
                if (image.name == "Icon" && image.transform.parent.name.StartsWith("ItemSlot")) { Assert.NotNull(image.sprite); itemIcons++; }
            Assert.AreEqual(20, itemIcons);
            yield return Capture("items-inventory");
            ScrollRect inventory=ui.transform.Find("PauseItems").GetComponentInChildren<ScrollRect>();
            inventory.verticalNormalizedPosition=0;yield return null;
            yield return Capture("items-inventory-last-row");
            Call(flow, "ResumeGame");
            Call(_round,"Tick",15f);
            object stats=Get<object>(_round,"Player");object damage=Enum.Parse(TypeOf("PlayerStatType"),"DamagePercent");
            Assert.Greater((float)Call(stats,"GetFinalStat",damage),15f);
            UnityEngine.Object.Destroy((UnityEngine.Object)_items);yield return null;
            Assert.AreEqual(0f,(float)Call(stats,"GetFinalStat",damage),"真实销毁回调必须移除沙漏及道具基础属性来源");
        }

        /// <summary>逐把通过商店购买新武器，在战斗中生成对应池化攻击，再通过商店回收。</summary>
        [UnityTest] public IEnumerator TenWeapons_BuyAttackAndRecycleRealPrefabs()
        {
            int count = 0;
            foreach (object product in _products)
            {
                object data = Field(Field(product, "content"), "weaponToGrant");
                if (data == null || !((string)Field(data, "weaponID")).Contains("_")) continue;
                string id = (string)Field(data, "weaponID");
                if (id.Length < 3 || !char.IsDigit(id[0]) || id[2] != '_') continue;
                SeedOffer(product, 1); Assert.IsTrue((bool)Call(_shop, "Buy", 0), id);
                IList owned = Get<IList>(_loadout, "OwnedWeapons"); object weapon = owned[owned.Count - 1];
                Assert.AreSame(data, Field(weapon, "weaponData"));
                Assert.IsTrue((bool)Call(_round, "BeginNextRound")); yield return null;
                weapon.GetType().GetMethod("Attack", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(weapon, null);
                yield return new WaitForFixedUpdate();
                string runtime = Field(data, "runtimeType").ToString();
                string entity = runtime == "Melee" ? "MeleeSwingHitbox" : runtime == "Aura" ? "AuraDamageZone"
                    : runtime == "Orbiting" ? "OrbitingProjectile" : runtime == "Lobbed" ? "LobbedProjectile" : "ProjectileBase";
                bool found = false;
                foreach (UnityEngine.Object attack in UnityEngine.Object.FindObjectsOfType(TypeOf(entity)))
                {
                    FieldInfo field = attack.GetType().GetField("_weaponData", BindingFlags.Instance|BindingFlags.NonPublic)
                        ?? attack.GetType().GetField("weaponData", BindingFlags.Instance|BindingFlags.NonPublic);
                    if (field != null && ReferenceEquals(field.GetValue(attack), data)) found = true;
                }
                Assert.IsTrue(found, "未生成对应攻击实体：" + id);
                // 留出弹体离开角色遮挡区的时间；近战的短暂挥击则立即截图。
                if (runtime!="Melee")
                {
                    yield return new WaitForSeconds(.18f);
                    bool remains=false;
                    foreach (UnityEngine.Object attack in UnityEngine.Object.FindObjectsOfType(TypeOf(entity)))
                    {
                        FieldInfo field=attack.GetType().GetField("_weaponData",BindingFlags.Instance|BindingFlags.NonPublic)
                            ?? attack.GetType().GetField("weaponData",BindingFlags.Instance|BindingFlags.NonPublic);
                        if(field!=null && ReferenceEquals(field.GetValue(attack),data)) remains=true;
                    }
                    Assert.IsTrue(remains,"弹体尚在屏内，不应提前回收："+id);
                }
                yield return Capture(id);
                yield return EnterShop();
                Assert.IsTrue((bool)Call(_shop, "Recycle", weapon)); count++;
            }
            Assert.AreEqual(10, count);
        }

        /// <summary>以显式商品夹具固定报价，测试实际 Buy 路径，避免随机抽取造成偶发失败。</summary>
        private void SeedOffer(object product, int tier)
        {
            Array offers = (Array)_shop.GetType().GetField("_offers", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_shop);
            offers.SetValue(Activator.CreateInstance(TypeOf("RunShopOffer"), product, tier, (int)Field(product, "basePrice") + 2*Get<int>(_round,"RoundNumber")), 0);
        }

        /// <summary>推进当前波并等待既有结算流程，随后消费待选升级与宝箱。</summary>
        private IEnumerator EnterShop()
        {
            if (Get<object>(_round, "Phase").ToString() == "Shop") yield break;
            Call(_round, "Tick", 100f); yield return RuntimeComponentTestUtility.WaitForRoundSettlement(_round);
            for (int i=0; i<100 && Get<object>(_round,"Phase").ToString()!="Shop"; i++)
            {
                string phase = Get<object>(_round,"Phase").ToString();
                if (phase=="Upgrades") Call(_round,"Choose",0);
                if (phase=="Crates") Call(_round,"ResolveCrate",Enum.Parse(TypeOf("CrateRewardAction"),"Recycle"));
                yield return null;
            }
            Assert.AreEqual("Shop",Get<object>(_round,"Phase").ToString());
        }

        /// <summary>图形专项运行时捕获真实相机与 UI；无图形门禁跳过截图但保留行为断言。</summary>
        private IEnumerator Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("SESSION25_SCREENSHOTS");
            if (string.IsNullOrEmpty(directory)) yield break;
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            Directory.CreateDirectory(directory);
            Camera camera=Camera.main;Component ui=(Component)UnityEngine.Object.FindObjectOfType(TypeOf("RoundIntermissionUI"));
            Canvas canvas=ui!=null?ui.GetComponent<Canvas>():null;
            Assert.NotNull(canvas);
            var target=new RenderTexture(1280,720,24); target.Create();
            RenderTexture old=camera.targetTexture; RenderMode mode=canvas.renderMode; Camera oldCamera=canvas.worldCamera;
            int layer=canvas.sortingLayerID, order=canvas.sortingOrder; float distance=canvas.planeDistance;
            canvas.enabled=false;camera.targetTexture=target; canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            SortingLayer[] layers=SortingLayer.layers;canvas.sortingLayerID=layers[layers.Length-1].id;canvas.sortingOrder=short.MaxValue;canvas.enabled=true;
            yield return null; Canvas.ForceUpdateCanvases();
            if(name=="items-inventory-last-row")
            {
                ScrollRect inventory=ui.transform.Find("PauseItems").GetComponentInChildren<ScrollRect>();
                inventory.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
                Bounds bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(inventory.viewport,inventory.content.GetChild(19));
                Assert.GreaterOrEqual(bounds.min.y,inventory.viewport.rect.yMin-1);
                Assert.LessOrEqual(bounds.max.y,inventory.viewport.rect.yMax+1);
            }
            camera.Render();
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false); RenderTexture active=RenderTexture.active;
            try { RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),pixels.EncodeToPNG()); }
            finally { RenderTexture.active=active;camera.targetTexture=old;canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.sortingLayerID=layer;canvas.sortingOrder=order;canvas.planeDistance=distance;UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target); }
        }

        /// <summary>解析默认运行时程序集类型。</summary>
        private static Type TypeOf(string name)=>Type.GetType(name+", Assembly-CSharp",true);
        /// <summary>读取公开字段。</summary>
        private static object Field(object obj,string name)=>obj.GetType().GetField(name).GetValue(obj);
        /// <summary>读取只读属性。</summary>
        private static T Get<T>(object obj,string name)=>(T)obj.GetType().GetProperty(name).GetValue(obj);
        /// <summary>调用实例入口，按参数数量选择既有重载。</summary>
        private static object Call(object obj,string name,params object[] args)
        {foreach(var m in obj.GetType().GetMethods())if(m.Name==name&&m.GetParameters().Length==args.Length)return m.Invoke(obj,args);throw new MissingMethodException(name);}
        /// <summary>调用静态测试入口。</summary>
        private static object Static(string type,string name,params object[] args)=>TypeOf(type).GetMethod(name).Invoke(null,args);
    }
}
