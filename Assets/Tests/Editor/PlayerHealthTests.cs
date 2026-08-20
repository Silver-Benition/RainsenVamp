using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证玩家生命、伤害事件、无敌帧和死亡边界。</summary>
    public sealed class PlayerHealthTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>验证 Awake 会以配置的最大生命初始化全部公开状态。</summary>
        [Test]
        public void Awake_使用配置的最大生命初始化状态()
        {
            PlayerHealth health = CreatePlayerHealth(125f, 0.5f);

            Assert.That(health.MaxHealth, Is.EqualTo(125f).Within(FloatTolerance));
            Assert.That(health.CurrentHealth, Is.EqualTo(125f).Within(FloatTolerance));
            Assert.That(health.NormalizedHealth, Is.EqualTo(1f).Within(FloatTolerance));
            Assert.IsFalse(health.IsDead);
        }

        /// <summary>验证一次有效伤害会更新生命并按实际扣血量发布两个结果事件。</summary>
        [Test]
        public void TakeDamage_有效伤害_更新生命并发布事件()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0f);
            int damagedCount = 0;
            int healthChangedCount = 0;
            float reportedDamage = 0f;
            float reportedCurrentHealth = 0f;
            float reportedMaxHealth = 0f;

            health.Damaged += damage =>
            {
                damagedCount++;
                reportedDamage = damage;
            };
            health.HealthChanged += (currentHealth, maxHealth) =>
            {
                healthChangedCount++;
                reportedCurrentHealth = currentHealth;
                reportedMaxHealth = maxHealth;
            };

            health.TakeDamage(25f);

            Assert.That(health.CurrentHealth, Is.EqualTo(75f).Within(FloatTolerance));
            Assert.That(health.NormalizedHealth, Is.EqualTo(0.75f).Within(FloatTolerance));
            Assert.That(damagedCount, Is.EqualTo(1));
            Assert.That(healthChangedCount, Is.EqualTo(1));
            Assert.That(reportedDamage, Is.EqualTo(25f).Within(FloatTolerance));
            Assert.That(reportedCurrentHealth, Is.EqualTo(75f).Within(FloatTolerance));
            Assert.That(reportedMaxHealth, Is.EqualTo(100f).Within(FloatTolerance));
        }

        /// <summary>验证零伤害和负伤害不会修改生命，也不会错误触发表现事件。</summary>
        [Test]
        public void TakeDamage_非正数伤害_保持状态且不发布事件()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0f);
            int damagedCount = 0;
            int healthChangedCount = 0;
            int diedCount = 0;
            health.Damaged += _ => damagedCount++;
            health.HealthChanged += (_, __) => healthChangedCount++;
            health.Died += () => diedCount++;

            health.TakeDamage(0f);
            health.TakeDamage(-10f);

            Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(FloatTolerance));
            Assert.That(damagedCount, Is.Zero);
            Assert.That(healthChangedCount, Is.Zero);
            Assert.That(diedCount, Is.Zero);
        }

        /// <summary>验证同一无敌窗口内的后续伤害请求会被全局伤害门槛拒绝。</summary>
        [Test]
        public void TakeDamage_仍在无敌窗口_拒绝重复伤害()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0.5f);
            int damagedCount = 0;
            health.Damaged += _ => damagedCount++;

            health.TakeDamage(10f);
            health.TakeDamage(10f);

            Assert.That(health.CurrentHealth, Is.EqualTo(90f).Within(FloatTolerance));
            Assert.That(damagedCount, Is.EqualTo(1));
        }

        /// <summary>验证无敌窗口到期后，下一次伤害可以正常生效。</summary>
        [Test]
        public void TakeDamage_无敌窗口结束_允许再次受伤()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0.05f);

            health.TakeDamage(10f);
            // EditMode 不推进 Time.time；直接把门槛移到当前时间之前，确定性模拟窗口到期。
            TestObjectUtility.SetPrivateFloat(health, "_nextDamageAllowedTime", Time.time - 1f);
            health.TakeDamage(10f);

            Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(FloatTolerance));
        }

        /// <summary>验证关闭无敌帧后，同一帧内的多次有效伤害都能独立结算。</summary>
        [Test]
        public void TakeDamage_无敌时间为零_连续伤害全部生效()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0f);

            health.TakeDamage(10f);
            health.TakeDamage(10f);
            health.TakeDamage(10f);

            Assert.That(health.CurrentHealth, Is.EqualTo(70f).Within(FloatTolerance));
        }

        /// <summary>验证致死伤害会钳制到零，死亡后所有伤害和死亡事件都不会重复处理。</summary>
        [Test]
        public void TakeDamage_致死后再次请求_死亡事件只触发一次()
        {
            PlayerHealth health = CreatePlayerHealth(100f, 0f);
            int damagedCount = 0;
            int healthChangedCount = 0;
            int diedCount = 0;
            float reportedDamage = 0f;
            health.Damaged += damage =>
            {
                damagedCount++;
                reportedDamage = damage;
            };
            health.HealthChanged += (_, __) => healthChangedCount++;
            health.Died += () => diedCount++;

            health.TakeDamage(150f);
            health.TakeDamage(10f);

            Assert.That(health.CurrentHealth, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.That(health.NormalizedHealth, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsTrue(health.IsDead);
            Assert.That(reportedDamage, Is.EqualTo(100f).Within(FloatTolerance));
            Assert.That(damagedCount, Is.EqualTo(1));
            Assert.That(healthChangedCount, Is.EqualTo(1));
            Assert.That(diedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// 在禁用宿主上配置序列化字段，再激活对象触发真实 Awake，避免测试绕过生产生命周期。
        /// </summary>
        private PlayerHealth CreatePlayerHealth(float maxHealth, float invulnerabilityDuration)
        {
            GameObject playerObject = CreateTrackedGameObject("AutomationTest_PlayerHealth");
            PlayerHealth health = playerObject.AddComponent<PlayerHealth>();
            TestObjectUtility.SetFloat(health, "maxHealth", maxHealth);
            TestObjectUtility.SetFloat(health, "invulnerabilityDuration", invulnerabilityDuration);
            TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
            return health;
        }
    }
}
