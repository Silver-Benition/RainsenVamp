using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证伤害目标过滤只接受正确阵营、显式受击标记，并兼容刚体根节点结构。</summary>
    public sealed class DamageTargetFilterTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /// <summary>每项测试后销毁临时组件，避免编辑器对象泄漏到下一项测试。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = _createdObjects.Count - 1; index >= 0; index--)
            {
                if (_createdObjects[index] != null)
                {
                    Object.DestroyImmediate(_createdObjects[index]);
                }
            }

            _createdObjects.Clear();
        }

        /// <summary>验证空碰撞体会安全失败并返回空接口。</summary>
        [Test]
        public void TryGetEnemyDamageable_空碰撞体_安全失败()
        {
            bool found = DamageTargetFilter.TryGetEnemyDamageable(null, out IDamageable damageable);

            Assert.IsFalse(found);
            Assert.IsNull(damageable);
        }

        /// <summary>验证错误 Layer 即使拥有 IDamageable，也不会被当作敌人命中。</summary>
        [Test]
        public void TryGetEnemyDamageable_非敌人Layer_拒绝目标()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_WrongLayer");
            target.layer = RequireLayer("Default");
            TestDamageableComponent damageableComponent = target.AddComponent<TestDamageableComponent>();
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();

            bool found = DamageTargetFilter.TryGetEnemyDamageable(collider, out IDamageable damageable);

            Assert.IsFalse(found);
            Assert.IsNull(damageable);
            Assert.IsNotNull(damageableComponent);
        }

        /// <summary>验证 Enemy Layer 碰撞体上的直接 IDamageable 可以被找到。</summary>
        [Test]
        public void TryGetEnemyDamageable_敌人碰撞体直接实现接口_返回目标()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_DirectEnemy");
            target.layer = RequireLayer("Enemy");
            TestDamageableComponent expected = target.AddComponent<TestDamageableComponent>();
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();

            bool found = DamageTargetFilter.TryGetEnemyDamageable(collider, out IDamageable actual);

            Assert.IsTrue(found);
            Assert.AreSame(expected, actual);
        }

        /// <summary>验证子节点 Collider 能通过 attachedRigidbody 找到 Enemy 根节点接口。</summary>
        [Test]
        public void TryGetEnemyDamageable_接口位于刚体根节点_返回根节点目标()
        {
            GameObject root = CreateTrackedGameObject("AutomationTest_EnemyBodyRoot");
            root.layer = RequireLayer("Enemy");
            Rigidbody2D rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            TestDamageableComponent expected = root.AddComponent<TestDamageableComponent>();

            GameObject colliderObject = CreateTrackedGameObject("AutomationTest_EnemyColliderChild");
            colliderObject.layer = RequireLayer("Default");
            colliderObject.transform.SetParent(root.transform, false);
            BoxCollider2D collider = colliderObject.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();

            bool found = DamageTargetFilter.TryGetEnemyDamageable(collider, out IDamageable actual);

            Assert.AreSame(rigidbody, collider.attachedRigidbody);
            Assert.IsTrue(found);
            Assert.AreSame(expected, actual);
        }

        /// <summary>验证带 PlayerHurtbox 的 Player Layer 碰撞体可以被敌方伤害入口找到。</summary>
        [Test]
        public void TryGetPlayerDamageable_玩家Layer且带受击标记_返回目标()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_DirectPlayer");
            target.layer = RequireLayer("Player");
            TestDamageableComponent expected = target.AddComponent<TestDamageableComponent>();
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();
            target.AddComponent<PlayerHurtbox>();

            bool found = DamageTargetFilter.TryGetPlayerDamageable(collider, out IDamageable actual);

            Assert.IsTrue(found);
            Assert.AreSame(expected, actual);
        }

        /// <summary>验证 Player Layer 的非 Trigger Collider 没有显式标记时会被拒绝。</summary>
        [Test]
        public void TryGetPlayerDamageable_玩家Layer但无受击标记_拒绝目标()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_UnmarkedPlayerCollider");
            target.layer = RequireLayer("Player");
            target.AddComponent<TestDamageableComponent>();
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();

            bool found = DamageTargetFilter.TryGetPlayerDamageable(collider, out IDamageable damageable);

            Assert.IsFalse(found);
            Assert.IsNull(damageable);
        }

        /// <summary>验证 Player Layer 的辅助 Trigger 没有显式标记时会被拒绝。</summary>
        [Test]
        public void TryGetPlayerDamageable_玩家Layer无标记Trigger_拒绝目标()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_UnmarkedPlayerTrigger");
            target.layer = RequireLayer("Player");
            target.AddComponent<TestDamageableComponent>();
            CircleCollider2D collider = target.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;

            bool found = DamageTargetFilter.TryGetPlayerDamageable(collider, out IDamageable damageable);

            Assert.IsFalse(found);
            Assert.IsNull(damageable);
        }

        /// <summary>验证带标记的子级 Trigger 可以沿 attachedRigidbody 找到玩家根节点受击接口。</summary>
        [Test]
        public void TryGetPlayerDamageable_标记子级Trigger_解析刚体根节点目标()
        {
            GameObject root = CreateTrackedGameObject("AutomationTest_PlayerBodyRoot");
            root.layer = RequireLayer("Player");
            Rigidbody2D rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            TestDamageableComponent expected = root.AddComponent<TestDamageableComponent>();

            GameObject colliderObject = CreateTrackedGameObject("AutomationTest_PlayerHurtboxChild");
            colliderObject.layer = RequireLayer("Player");
            colliderObject.transform.SetParent(root.transform, false);
            CircleCollider2D collider = colliderObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            colliderObject.AddComponent<PlayerHurtbox>();
            Physics2D.SyncTransforms();

            bool found = DamageTargetFilter.TryGetPlayerDamageable(collider, out IDamageable actual);

            Assert.AreSame(rigidbody, collider.attachedRigidbody);
            Assert.IsTrue(found);
            Assert.AreSame(expected, actual);
        }

        /// <summary>验证 Layer 正确但没有 IDamageable 时安全失败。</summary>
        [Test]
        public void TryGetEnemyDamageable_缺少受击接口_安全失败()
        {
            GameObject target = CreateTrackedGameObject("AutomationTest_NoDamageable");
            target.layer = RequireLayer("Enemy");
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();

            bool found = DamageTargetFilter.TryGetEnemyDamageable(collider, out IDamageable damageable);

            Assert.IsFalse(found);
            Assert.IsNull(damageable);
        }

        /// <summary>验证物理查询公开掩码与项目 Enemy Layer 保持一致。</summary>
        [Test]
        public void EnemyLayerMask_项目已配置EnemyLayer_返回对应位掩码()
        {
            int enemyLayer = RequireLayer("Enemy");

            Assert.That(DamageTargetFilter.EnemyLayerMask, Is.EqualTo(1 << enemyLayer));
        }

        /// <summary>创建并登记 EditMode 临时对象。</summary>
        private GameObject CreateTrackedGameObject(string name)
        {
            GameObject createdObject = new GameObject(name);
            _createdObjects.Add(createdObject);
            return createdObject;
        }

        /// <summary>读取必需 Layer；项目配置被删除时给出直接、可定位的测试失败。</summary>
        private static int RequireLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            Assert.That(layer, Is.GreaterThanOrEqualTo(0), $"项目缺少必需 Layer：{layerName}");
            return layer;
        }
    }

    /// <summary>供过滤器测试使用的最小受击组件，不包含任何生产逻辑。</summary>
    internal sealed class TestDamageableComponent : MonoBehaviour, IDamageable
    {
        /// <summary>测试替身无需处理实际伤害。</summary>
        public void TakeDamage(float damage)
        {
        }

        /// <summary>测试替身无需处理实际伤害或暴击表现。</summary>
        public void TakeDamage(float damage, bool isCritical)
        {
        }
    }
}
