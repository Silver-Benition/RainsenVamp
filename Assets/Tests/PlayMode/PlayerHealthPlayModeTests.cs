using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在真实游戏时间中验证 PlayerHealth 的自动生命周期和无敌窗口。</summary>
    public sealed class PlayerHealthPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>同一无敌窗口内拒绝重复伤害，真实时间到期后允许下一次伤害。</summary>
        [UnityTest]
        public IEnumerator Invulnerability_真实时间到期后_允许再次受伤()
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_PlayerHealth", false);
            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(
                player,
                "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "maxHealth", 100f);
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0.12f);

            player.SetActive(true);

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance));

            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 10f);
            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 10f);

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(90f).Within(FloatTolerance));

            float waitStartedAt = Time.time;
            yield return new WaitForSeconds(0.16f);
            Assert.That(Time.time - waitStartedAt, Is.GreaterThanOrEqualTo(0.12f));

            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 10f);

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(80f).Within(FloatTolerance));
        }
    }
}
