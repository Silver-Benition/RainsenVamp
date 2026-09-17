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
