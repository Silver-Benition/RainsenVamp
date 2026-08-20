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
    }
}
