using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证阶段二掉落与 Defang 快照在真实对象池和物理循环中的状态重置。</summary>
    public sealed class PhaseTwoPoolLifecyclePlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>Defang 敌人回池再取出后必须恢复基础伤害、状态和 SpriteRenderer 原色。</summary>
        [UnityTest]
        public IEnumerator EnemyPool_Defang快照回收_完整恢复基础状态()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_PhaseTwoPool");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 2f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 5f);
            RuntimeComponentTestUtility.SetField(enemyData, "canBeDefanged", true);

            GameObject template = CreateTrackedGameObject("PlayModeTest_DefangEnemyTemplate", false);
            template.AddComponent<SpriteRenderer>().color = Color.white;
            template.AddComponent<Rigidbody2D>().gravityScale = 0f;
            template.AddComponent<BoxCollider2D>();
            Component templateEnemy = RuntimeComponentTestUtility.AddRuntimeComponent(template, "EnemyBase");
            RuntimeComponentTestUtility.SetField(templateEnemy, "enemyData", enemyData);

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject first = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component firstEnemy = first.GetComponent("EnemyBase");
            object defangedSnapshot = CreateEnemySnapshot(20f, 4f, 0f, 0f, true);
            RuntimeComponentTestUtility.Invoke(firstEnemy, "ApplySpawnSnapshot", defangedSnapshot);

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(firstEnemy, "IsDefanged"));
            Assert.That(RuntimeComponentTestUtility.GetProperty<float>(firstEnemy, "CurrentCollisionDamage"), Is.Zero);
            Assert.AreNotEqual(Color.white, first.GetComponent<SpriteRenderer>().color);

            RuntimeComponentTestUtility.Invoke(poolManager, "Release", template, first);
            Assert.AreEqual(Color.white, first.GetComponent<SpriteRenderer>().color);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(firstEnemy, "CurrentCollisionDamage"),
                Is.Zero);
            Assert.That(
                (float)RuntimeComponentTestUtility.Invoke(firstEnemy, "ResolveOutgoingDamage", 7f),
                Is.Zero);

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject second = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component secondEnemy = second.GetComponent("EnemyBase");

            Assert.AreSame(first, second);
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(secondEnemy, "IsDefanged"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(secondEnemy, "CurrentCollisionDamage"),
                Is.EqualTo(5f).Within(FloatTolerance));
            Assert.AreEqual(Color.white, second.GetComponent<SpriteRenderer>().color);
            yield return null;
        }

        /// <summary>Defang 敌人持续包围并在接触中停用时，玩家生命始终不得下降。</summary>
        [UnityTest]
        public IEnumerator DefangedEnemy_持续碰撞并停用_玩家不受伤()
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_DefangCollisionPlayer", false);
            player.tag = "Player";
            player.layer = RequireLayer("Player");
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
            player.AddComponent<BoxCollider2D>();
            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            player.SetActive(true);

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 0f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "canBeDefanged", true);

            GameObject enemyObject = CreateTrackedGameObject("PlayModeTest_DefangCollisionEnemy", false);
            enemyObject.layer = RequireLayer("Enemy");
            Rigidbody2D enemyBody = enemyObject.AddComponent<Rigidbody2D>();
            enemyBody.gravityScale = 0f;
            enemyBody.constraints = RigidbodyConstraints2D.FreezeAll;
            enemyObject.AddComponent<BoxCollider2D>();
            Component enemy = RuntimeComponentTestUtility.AddRuntimeComponent(enemyObject, "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemy, "enemyData", enemyData);
            enemyObject.SetActive(true);
            object snapshot = CreateEnemySnapshot(10f, 0f, 0f, 0f, true);
            RuntimeComponentTestUtility.Invoke(enemy, "ApplySpawnSnapshot", snapshot);

            float initialHealth = RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(initialHealth).Within(FloatTolerance));

            enemyObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(initialHealth).Within(FloatTolerance));
        }

        /// <summary>池化金币与玩家真实触发器接触后应按 Greed 结算，并把实例归还对象池。</summary>
        [UnityTest]
        public IEnumerator CoinPickup_碰到Greed玩家_按倍率入账并回池()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_CoinPool");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            ScriptableObject character = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("CharacterDataSO"));
            Type baseStatsType = RuntimeComponentTestUtility.RequireRuntimeType("CharacterBaseStats");
            object baseStats = Activator.CreateInstance(baseStatsType);
            baseStatsType.GetField("greed")?.SetValue(baseStats, 2f);
            RuntimeComponentTestUtility.SetField(character, "baseStats", baseStats);

            GameObject player = CreateTrackedGameObject("PlayModeTest_GreedPlayer", false);
            player.tag = "Player";
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            player.AddComponent<BoxCollider2D>();
            Component playerStats = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            RuntimeComponentTestUtility.Invoke(playerStats, "SetCharacterData", character);
            player.SetActive(true);

            GameObject coinTemplate = CreateTrackedGameObject("PlayModeTest_CoinTemplate", false);
            CircleCollider2D coinCollider = coinTemplate.AddComponent<CircleCollider2D>();
            coinCollider.isTrigger = true;
            RuntimeComponentTestUtility.AddRuntimeComponent(coinTemplate, "CoinPickup");

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject coin = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                coinTemplate,
                player.transform.position,
                Quaternion.identity));
            Component coinPickup = coin.GetComponent("CoinPickup");
            RuntimeComponentTestUtility.Invoke(coinPickup, "ConfigureValue", 3);

            yield return new WaitForFixedUpdate();
            yield return null;

            Component runState = player.GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("RunState"));
            Assert.IsNotNull(runState);
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(runState, "GoldCount"), Is.EqualTo(6));
            Assert.IsFalse(coin.activeSelf);
        }

        /// <summary>通过反射构造默认程序集中的只读 EnemySpawnSnapshot。</summary>
        private static object CreateEnemySnapshot(
            float health,
            float speed,
            float collisionDamage,
            float outgoingMultiplier,
            bool defanged)
        {
            Type snapshotType = RuntimeComponentTestUtility.RequireRuntimeType("EnemySpawnSnapshot");
            return Activator.CreateInstance(
                snapshotType,
                health,
                speed,
                collisionDamage,
                outgoingMultiplier,
                defanged);
        }
    }
}
