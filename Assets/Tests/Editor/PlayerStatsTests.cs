using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证玩家经验需求列表、跨级消耗和列表范围外的备用增长规则。</summary>
    public sealed class PlayerStatsTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>逐级经验列表应按当前等级索引返回对应需求。</summary>
        [Test]
        public void GetExperienceRequiredForLevel_逐级配置_返回对应经验需求()
        {
            GameObject playerObject = CreateTrackedGameObject("AutomationTest_PlayerStats");
            PlayerStats stats = playerObject.AddComponent<PlayerStats>();
            TestObjectUtility.SetPrivateField(
                stats,
                "experienceRequirements",
                new List<float> { 10f, 20f, 35f });
            TestObjectUtility.SetPrivateField(stats, "experienceFallbackGrowth", 1.2f);
            TestObjectUtility.InvokeNonPublicMethod(stats, "Awake");

            Assert.That(stats.GetExperienceRequiredForLevel(1), Is.EqualTo(10f).Within(FloatTolerance));
            Assert.That(stats.GetExperienceRequiredForLevel(2), Is.EqualTo(20f).Within(FloatTolerance));
            Assert.That(stats.GetExperienceRequiredForLevel(3), Is.EqualTo(35f).Within(FloatTolerance));
        }

        /// <summary>跨越多级时应逐级扣除配置经验，并把当前等级需求切换到下一项。</summary>
        [Test]
        public void AddExp_跨越多级_按每级配置扣除并更新当前需求()
        {
            GameObject playerObject = CreateTrackedGameObject("AutomationTest_PlayerStats");
            PlayerStats stats = playerObject.AddComponent<PlayerStats>();
            TestObjectUtility.SetPrivateField(
                stats,
                "experienceRequirements",
                new List<float> { 10f, 15f, 25f });
            TestObjectUtility.InvokeNonPublicMethod(stats, "Awake");

            stats.AddExp(27f);

            Assert.That(stats.currentLevel, Is.EqualTo(3));
            Assert.That(stats.currentExp, Is.EqualTo(2f).Within(FloatTolerance));
            Assert.That(stats.expToNextLevel, Is.EqualTo(25f).Within(FloatTolerance));
        }

        /// <summary>列表结束后应从最后一项按备用倍率继续计算，避免高等级失去有效需求。</summary>
        [Test]
        public void GetExperienceRequiredForLevel_超过列表范围_按备用倍率增长()
        {
            GameObject playerObject = CreateTrackedGameObject("AutomationTest_PlayerStats");
            PlayerStats stats = playerObject.AddComponent<PlayerStats>();
            TestObjectUtility.SetPrivateField(
                stats,
                "experienceRequirements",
                new List<float> { 10f, 20f });
            TestObjectUtility.SetPrivateField(stats, "experienceFallbackGrowth", 1.5f);
            TestObjectUtility.InvokeNonPublicMethod(stats, "Awake");

            Assert.That(stats.GetExperienceRequiredForLevel(3), Is.EqualTo(30f).Within(FloatTolerance));
            Assert.That(stats.GetExperienceRequiredForLevel(4), Is.EqualTo(45f).Within(FloatTolerance));
        }
    }
}
