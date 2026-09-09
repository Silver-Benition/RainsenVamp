using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证生成主世界的运行时隔离、真实磁吸入口、冻结恢复和对象池生命周期。</summary>
    public sealed class MainWorldPerformancePlayModeTests : PlayModeComponentTestBase
    {
        private const string PerformanceScenePath = "Assets/Tests/Performance/Generated/MainWorldPerformance.unity";
        private const string TestMagnetModifier = "session21-performance-playmode-magnet";
        private Scene _loadedPerformanceScene;
        private Component _loadedRunner;

        /// <summary>测试失败或断言提前退出时也停止 Runner、卸载整场景并恢复内存账号。</summary>
        [UnityTearDown]
        public IEnumerator UnloadPerformanceScene()
        {
            if (_loadedRunner != null)
            {
                Behaviour behaviour = _loadedRunner as Behaviour;
                if (behaviour != null) behaviour.enabled = false;
            }

            if (_loadedPerformanceScene.IsValid() && _loadedPerformanceScene.isLoaded)
            {
                // Single 模式加载生成场景后它可能是 PlayMode 中唯一场景；先放入一个
                // 空的活动场景，Unity 才允许卸载性能场景，避免 Runner 残留污染后续用例。
                Scene cleanupScene = SceneManager.CreateScene("Session21PerformanceCleanupScene");
                SceneManager.SetActiveScene(cleanupScene);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_loadedPerformanceScene);
                if (unload != null)
                {
                    while (!unload.isDone) yield return null;
                }
            }

            _loadedRunner = null;
            _loadedPerformanceScene = default(Scene);
            Time.timeScale = 1f;
            InstallInMemoryAccount();
            yield return null;
        }

        /// <summary>场景加载后副世界保持停用，主世界 Runner 能找到真实模拟器和波次链。</summary>
        [UnityTest]
        public IEnumerator GeneratedScene_IsolatedSubWorldAndRealMainChain()
        {
            yield return LoadPerformanceScene();
            Component runner = FindRuntimeComponent("MainWorldPerformanceRunner");
            Component coordinator = FindRuntimeComponent("WorldLineCoordinator");
            GameObject subWorld = FindInactiveObject("SubWorldRuntime");

            Assert.IsNotNull(runner);
            Assert.IsNotNull(RuntimeComponentTestUtility.GetProperty<ScriptableObject>(runner, "Profile"));
            Assert.IsNotNull(RuntimeComponentTestUtility.GetProperty<object>(runner, "MainSimulation"));
            Assert.IsNotNull(coordinator);
            Assert.IsNotNull(RuntimeComponentTestUtility.GetProperty<object>(coordinator, "MainWorldWaveManager"));
            Assert.IsNotNull(RuntimeComponentTestUtility.GetProperty<object>(coordinator, "SubWorldWaveManager"));
            Assert.IsNotNull(subWorld);
            Assert.IsFalse(subWorld.activeSelf);

            yield return null;
        }

        /// <summary>冻结仅影响敌对模拟，倒计时结束后恢复；事件和对象池均可安全回收。</summary>
        [UnityTest]
        public IEnumerator FreezeAndPickup_UseRealLifecycleAndMemoryAccount()
        {
            yield return LoadPerformanceScene();
            Component runner = FindRuntimeComponent("MainWorldPerformanceRunner");
            Component stats = FindRuntimeComponent("PlayerStats");
            Component freeze = FindRuntimeComponent("WorldFreezeController");
            Component pool = FindRuntimeComponent("PoolManager");
            Assert.IsNotNull(runner);
            Assert.IsNotNull(stats);
            Assert.IsNotNull(freeze);
            Assert.IsNotNull(pool);

            Type freezeType = RuntimeComponentTestUtility.RequireRuntimeType("WorldFreezeController");
            Assert.IsTrue((bool)RuntimeComponentTestUtility.Invoke(freeze, "TryFreeze", 0.15f));
            Assert.IsTrue((bool)freezeType.GetProperty(
                "IsHostileSimulationFrozen",
                BindingFlags.Static | BindingFlags.Public).GetValue(null, null));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsFalse((bool)freezeType.GetProperty(
                "IsHostileSimulationFrozen",
                BindingFlags.Static | BindingFlags.Public).GetValue(null, null));

            GameObject expPrefab = (GameObject)RuntimeComponentTestUtility.GetProperty<object>(runner, "ExpPickupPrefab");
            GameObject coinPrefab = (GameObject)RuntimeComponentTestUtility.GetProperty<object>(runner, "CoinPickupPrefab");
            Assert.IsNotNull(expPrefab);
            Assert.IsNotNull(coinPrefab);

            float magnet = RuntimeComponentTestUtility.GetProperty<float>(stats, "Magnet");
            Transform playerTransform = stats.transform;
            GameObject exp = (GameObject)RuntimeComponentTestUtility.Invoke(
                pool,
                "Spawn",
                expPrefab,
                playerTransform.position + Vector3.right * Mathf.Max(4f, magnet + 1f),
                Quaternion.identity);
            GameObject coin = (GameObject)RuntimeComponentTestUtility.Invoke(
                pool,
                "Spawn",
                coinPrefab,
                playerTransform.position + Vector3.up * Mathf.Max(4f, magnet + 1f),
                Quaternion.identity);
            Assert.IsNotNull(exp);
            Assert.IsNotNull(coin);
            Assert.IsTrue(exp.activeSelf);
            Assert.IsTrue(coin.activeSelf);

            Type modifierType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatModifier");
            Type statType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatType");
            Type modeType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatModifierMode");
            object magnetStat = Enum.Parse(statType, "Magnet");
            object flatMode = Enum.Parse(modeType, "Flat");
            object modifier = Activator.CreateInstance(modifierType, magnetStat, flatMode, 4f);
            Array modifiers = Array.CreateInstance(modifierType, 1);
            modifiers.SetValue(modifier, 0);
            RuntimeComponentTestUtility.Invoke(stats, "SetModifiers", TestMagnetModifier, modifiers);
            Physics2D.SyncTransforms();
            yield return new WaitForSecondsRealtime(0.5f);

            Component expMotion = exp.GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("MagneticPickupMotion"));
            Component coinMotion = coin.GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("MagneticPickupMotion"));
            Assert.IsNotNull(expMotion);
            Assert.IsNotNull(coinMotion);
            object expState = RuntimeComponentTestUtility.GetProperty<object>(expMotion, "State");
            object coinState = RuntimeComponentTestUtility.GetProperty<object>(coinMotion, "State");
            Assert.IsTrue(!string.Equals(expState.ToString(), "Idle", StringComparison.Ordinal) ||
                !string.Equals(coinState.ToString(), "Idle", StringComparison.Ordinal));

            if (exp.activeSelf) RuntimeComponentTestUtility.Invoke(pool, "Release", expPrefab, exp);
            if (coin.activeSelf) RuntimeComponentTestUtility.Invoke(pool, "Release", coinPrefab, coin);
            Assert.IsFalse(exp.activeSelf);
            Assert.IsFalse(coin.activeSelf);
            RuntimeComponentTestUtility.Invoke(stats, "RemoveModifiers", TestMagnetModifier);

            Type accountType = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
            Type storageType = RuntimeComponentTestUtility.RequireRuntimeType("InMemoryAccountProgressStorage");
            object storage = Activator.CreateInstance(storageType);
            MethodInfo setStorage = accountType.GetMethod(
                "SetStorageForTests",
                BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(setStorage);
            setStorage.Invoke(null, new[] { storage });
            object service = accountType.GetProperty(
                "Current",
                BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(service, "IsReadOnly"));
            Assert.IsTrue((bool)RuntimeComponentTestUtility.Invoke(service, "RecordRunResults", 3, 4));
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(service, "Gold"), Is.EqualTo(3));
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(service, "LifetimeKills"), Is.EqualTo(4));

            yield return null;
        }

        /// <summary>为每项用例在场景 Awake 前安装内存账号，避免生成场景访问正式存档。</summary>
        private IEnumerator LoadPerformanceScene()
        {
            InstallInMemoryAccount();
            Type editorSceneManagerType = Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
            Assert.IsNotNull(editorSceneManagerType, "Performance scene tests require the Unity Editor player loop.");
            MethodInfo loadMethod = editorSceneManagerType.GetMethod(
                "LoadSceneAsyncInPlayMode",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(LoadSceneParameters) },
                null);
            Assert.IsNotNull(loadMethod);
            LoadSceneParameters parameters = new LoadSceneParameters(LoadSceneMode.Single);
            AsyncOperation load = (AsyncOperation)loadMethod.Invoke(null, new object[] { PerformanceScenePath, parameters });
            Assert.IsNotNull(load);
            while (!load.isDone) yield return null;
            yield return null;
            _loadedPerformanceScene = SceneManager.GetActiveScene();
            _loadedRunner = FindRuntimeComponent("MainWorldPerformanceRunner");
        }

        /// <summary>通过受限测试入口安装内存后端，不让 PlayMode asmdef 依赖运行时程序集。</summary>
        private static void InstallInMemoryAccount()
        {
            Type accountType = RuntimeComponentTestUtility.RequireRuntimeType("AccountProgressService");
            Type storageType = RuntimeComponentTestUtility.RequireRuntimeType("InMemoryAccountProgressStorage");
            MethodInfo setStorage = accountType.GetMethod(
                "SetStorageForTests",
                BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(setStorage);
            setStorage.Invoke(null, new[] { Activator.CreateInstance(storageType) });
        }

        /// <summary>通过运行时程序集反射读取一个生产组件，避免为测试改造项目 asmdef。</summary>
        private static Component FindRuntimeComponent(string typeName)
        {
            Type type = RuntimeComponentTestUtility.RequireRuntimeType(typeName);
            return UnityEngine.Object.FindObjectOfType(type) as Component;
        }

        /// <summary>遍历场景根节点读取停用的 SubWorldRuntime。</summary>
        private static GameObject FindInactiveObject(string objectName)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index].name == objectName) return transforms[index].gameObject;
                }
            }

            return null;
        }
    }
}
