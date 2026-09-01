using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证 PoolManager 与 EnemyBase 的真实启用、禁用和刚体状态重置。</summary>
    public sealed class PoolLifecyclePlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>敌人回收再取出时应复用实例，并清除上一生命周期的线速度与角速度。</summary>
        [UnityTest]
        public IEnumerator EnemyPool_回收再取出_复用实例且动量归零()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_PoolManager");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            GameObject template = CreateTrackedGameObject("PlayModeTest_EnemyTemplate", false);
            Rigidbody2D templateBody = template.AddComponent<Rigidbody2D>();
            templateBody.gravityScale = 0f;
            template.AddComponent<BoxCollider2D>();
            RuntimeComponentTestUtility.AddRuntimeComponent(template, "EnemyBase");

#if UNITY_EDITOR
            LogAssert.Expect(
                LogType.Warning,
                new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject firstInstance = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Rigidbody2D firstBody = firstInstance.GetComponent<Rigidbody2D>();
            firstBody.velocity = new Vector2(4f, -2f);
            firstBody.angularVelocity = 35f;

            RuntimeComponentTestUtility.Invoke(poolManager, "Release", template, firstInstance);

            Assert.IsFalse(firstInstance.activeSelf);
            Assert.That(firstBody.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.That(firstBody.angularVelocity, Is.EqualTo(0f).Within(FloatTolerance));

#if UNITY_EDITOR
            LogAssert.Expect(
                LogType.Warning,
                new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject secondInstance = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                new Vector3(2f, 3f, 0f),
                Quaternion.identity));
            Rigidbody2D secondBody = secondInstance.GetComponent<Rigidbody2D>();

            Assert.AreSame(firstInstance, secondInstance);
            Assert.IsTrue(secondInstance.activeSelf);
            Assert.That(secondInstance.transform.position, Is.EqualTo(new Vector3(2f, 3f, 0f)));
            Assert.That(secondBody.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.That(secondBody.angularVelocity, Is.EqualTo(0f).Within(FloatTolerance));

            yield return null;
        }

        /// <summary>敌人进入真实死亡出口时，击杀统计应只增加一次。</summary>
        [UnityTest]
        public IEnumerator EnemyDeath_进入死亡出口_击杀数增加一次()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_KillCounterPoolManager");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");
            GameObject statsObject = CreateTrackedGameObject("PlayModeTest_RunStats");
            Component runStats = RuntimeComponentTestUtility.AddRuntimeComponent(statsObject, "RunStatsUI");

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);

            GameObject template = CreateTrackedGameObject("PlayModeTest_KillCounterEnemyTemplate", false);
            Rigidbody2D body = template.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            template.AddComponent<BoxCollider2D>();
            Component templateEnemy = RuntimeComponentTestUtility.AddRuntimeComponent(template, "EnemyBase");
            RuntimeComponentTestUtility.SetField(templateEnemy, "enemyData", enemyData);
            template.SetActive(true);

#if UNITY_EDITOR
            LogAssert.Expect(
                LogType.Warning,
                new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject enemyInstance = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component enemy = enemyInstance.GetComponent("EnemyBase");

            RuntimeComponentTestUtility.Invoke(enemy, "TakeDamage", 10f);
            RuntimeComponentTestUtility.Invoke(enemy, "TakeDamage", 10f);
            yield return null;

            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(runStats, "KillCount"), Is.EqualTo(1));
        }

        /// <summary>真实对象池敌人致死后，伤害收据仍必须报告 TargetDefeated=true。</summary>
        [UnityTest]
        public IEnumerator EnemyPool_致死伤害回池_返回结果保留死亡状态()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_LethalDamagePoolManager");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);

            GameObject template = CreateTrackedGameObject("PlayModeTest_LethalDamageEnemyTemplate", false);
            Rigidbody2D body = template.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            template.AddComponent<BoxCollider2D>();
            Component templateEnemy = RuntimeComponentTestUtility.AddRuntimeComponent(template, "EnemyBase");
            RuntimeComponentTestUtility.SetField(templateEnemy, "enemyData", enemyData);

#if UNITY_EDITOR
            LogAssert.Expect(
                LogType.Warning,
                new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject enemyInstance = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component enemy = enemyInstance.GetComponent("EnemyBase");
            object telemetry = Activator.CreateInstance(
                RuntimeComponentTestUtility.RequireRuntimeType("RunTelemetry"));

            object result = RuntimeComponentTestUtility.InvokeStatic(
                RuntimeComponentTestUtility.RequireRuntimeType("CombatDamageResolver"),
                "Apply",
                enemy,
                10f,
                null,
                false,
                telemetry);

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(result, "Accepted"));
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(result, "TargetDefeated"),
                "敌人在 ApplyCombatDamage 内回池后，返回结果仍必须保留本次死亡状态。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(result, "AppliedDamage"),
                Is.EqualTo(10f).Within(FloatTolerance));
            Assert.IsFalse(enemyInstance.activeSelf, "致死敌人必须从对象池回收并停用。");

            yield return null;
        }
    }
}
