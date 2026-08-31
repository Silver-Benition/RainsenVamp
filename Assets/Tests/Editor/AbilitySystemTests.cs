using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证正式能力的累计属性、容量、机制状态与项目数据资产契约。</summary>
    public sealed class AbilitySystemTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;
        private const string AbilityIconDirectory = "Assets/Art/Sprites/Ability/Icons/";
        private const string RetaliationPulseSpritePath =
            "Assets/Art/Sprites/Ability/VFX/RetaliationPulseRing.png";

        /// <summary>升级应以累计快照替换稳定来源，不把 Lv.1 与 Lv.2 重复相加。</summary>
        [Test]
        public void GrantOrUpgrade_累计属性等级_替换旧来源且发布事件()
        {
            AbilityFixture fixture = CreateFixture("AutomationTest_AbilityModifiers");
            AbilityDataSO ability = CreateStatAbility(
                "ability_modifier_test",
                PlayerStatType.Might,
                PlayerStatModifierMode.AdditivePercent,
                0.1f,
                0.2f);
            int changeCount = 0;
            fixture.Manager.OwnedAbilitiesChanged += () => changeCount++;

            try
            {
                OwnedAbilityState levelOne = fixture.Manager.GrantOrUpgrade(ability);
                Assert.IsNotNull(levelOne);
                Assert.That(levelOne.CurrentLevel, Is.EqualTo(1));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1.1f).Within(FloatTolerance));

                OwnedAbilityState levelTwo = fixture.Manager.GrantOrUpgrade(ability);
                Assert.AreSame(levelOne, levelTwo);
                Assert.That(levelTwo.CurrentLevel, Is.EqualTo(2));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1.2f).Within(FloatTolerance));
                Assert.That(changeCount, Is.EqualTo(2));

                Assert.IsNull(fixture.Manager.GrantOrUpgrade(ability));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1.2f).Within(FloatTolerance));
                Assert.That(changeCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>逆境本能应跨越 40% 阈值时启停，并在激活状态升级后立即换用新数值。</summary>
        [Test]
        public void LowHealthBuff_生命阈值与升级_事件驱动启停并替换数值()
        {
            AbilityFixture fixture = CreateFixture("AutomationTest_LowHealthAbility");
            TestObjectUtility.SetPrivateFloat(fixture.Health, "invulnerabilityDuration", 0f);

            LowHealthBuffMechanicSO mechanic = ScriptableObject.CreateInstance<LowHealthBuffMechanicSO>();
            mechanic.healthThreshold = 0.4f;
            mechanic.levelConfigs = new List<LowHealthBuffLevelConfig>
            {
                new LowHealthBuffLevelConfig
                {
                    mightAdditivePercent = 0.2f,
                    moveSpeedAdditivePercent = 0.06f
                },
                new LowHealthBuffLevelConfig
                {
                    mightAdditivePercent = 0.3f,
                    moveSpeedAdditivePercent = 0.09f
                }
            };
            AbilityDataSO ability = CreateEmptyAbility("ability_low_health_test", 2);
            ability.mechanic = mechanic;

            try
            {
                Assert.IsNotNull(fixture.Manager.GrantOrUpgrade(ability));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1f).Within(FloatTolerance));
                Assert.That(fixture.Stats.FinalMoveSpeed, Is.EqualTo(3f).Within(FloatTolerance));

                fixture.Health.TakeDamage(61f);
                Assert.That(fixture.Health.NormalizedHealth, Is.EqualTo(0.39f).Within(FloatTolerance));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1.2f).Within(FloatTolerance));
                Assert.That(fixture.Stats.FinalMoveSpeed, Is.EqualTo(3.18f).Within(FloatTolerance));

                Assert.IsNotNull(fixture.Manager.GrantOrUpgrade(ability));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1.3f).Within(FloatTolerance));
                Assert.That(fixture.Stats.FinalMoveSpeed, Is.EqualTo(3.27f).Within(FloatTolerance));

                fixture.Health.Heal(20f);
                Assert.That(fixture.Health.NormalizedHealth, Is.EqualTo(0.59f).Within(FloatTolerance));
                Assert.That(fixture.Stats.Might, Is.EqualTo(1f).Within(FloatTolerance));
                Assert.That(fixture.Stats.FinalMoveSpeed, Is.EqualTo(3f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(mechanic);
            }
        }

        /// <summary>项目内必须存在六项合法能力、两项机制资产与完整等级曲线。</summary>
        [Test]
        public void ProjectAbilityAssets_六项正式能力_引用与等级配置完整()
        {
            string[] paths =
            {
                "Assets/Data/Abilities/StrengthTraining.asset",
                "Assets/Data/Abilities/SprintTraining.asset",
                "Assets/Data/Abilities/CooldownOptimization.asset",
                "Assets/Data/Abilities/MagneticCore.asset",
                "Assets/Data/Abilities/AdversityInstinct.asset",
                "Assets/Data/Abilities/RetaliationPulse.asset"
            };
            int[] expectedLevels = { 5, 5, 5, 5, 3, 3 };
            var stableIds = new HashSet<string>();
            var formalIcons = new HashSet<Sprite>();

            for (int index = 0; index < paths.Length; index++)
            {
                AbilityDataSO ability = AssetDatabase.LoadAssetAtPath<AbilityDataSO>(paths[index]);
                Assert.IsNotNull(ability, $"缺少正式能力资产：{paths[index]}");
                Assert.IsTrue(stableIds.Add(ability.GetStableId()), "能力稳定 ID 重复。 ");
                Assert.That(ability.MaxLevel, Is.EqualTo(expectedLevels[index]));
                Assert.IsNotNull(ability.icon, $"能力 {ability.name} 缺少正式图标。");
                Assert.IsTrue(formalIcons.Add(ability.icon), $"能力 {ability.name} 复用了其他能力图标。");
                AssertPixelSpriteImportContract(ability.icon, AbilityIconDirectory);

                string upgradePath = paths[index].Replace(".asset", "_Upgrade.asset");
                UpgradeDataSO upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDataSO>(upgradePath);
                Assert.IsNotNull(upgrade, $"缺少能力升级包装资产：{upgradePath}");
                Assert.AreSame(ability, upgrade.abilityToGrant, $"{upgrade.name} 未引用对应能力资产。");
                Assert.AreSame(ability.icon, upgrade.icon, $"{upgrade.name} 与能力正式图标不一致。");
            }

            AbilityDataSO adversity = AssetDatabase.LoadAssetAtPath<AbilityDataSO>(paths[4]);
            AbilityDataSO retaliation = AssetDatabase.LoadAssetAtPath<AbilityDataSO>(paths[5]);
            Assert.IsInstanceOf<LowHealthBuffMechanicSO>(adversity.mechanic);
            Assert.IsInstanceOf<RetaliationPulseMechanicSO>(retaliation.mechanic);
            RetaliationPulseMechanicSO pulse = (RetaliationPulseMechanicSO)retaliation.mechanic;
            Assert.IsNotNull(pulse.pulseVfxPrefab);
            Assert.IsNotNull(pulse.pulseVfxPrefab.GetComponent<AbilityPulseVfx>());
            SpriteRenderer pulseRenderer = pulse.pulseVfxPrefab.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(pulseRenderer);
            Assert.IsNotNull(pulseRenderer.sprite, "反击脉冲 Prefab 缺少正式环形 Sprite。");
            Assert.That(
                AssetDatabase.GetAssetPath(pulseRenderer.sprite),
                Is.EqualTo(RetaliationPulseSpritePath));
            AssertPixelSpriteImportContract(pulseRenderer.sprite, "Assets/Art/Sprites/Ability/VFX/");
        }

        /// <summary>
        /// 验证正式像素 Sprite 的目录、尺寸与导入设置，防止 Unity 重导入后出现模糊、压缩或物理形状。
        /// </summary>
        private static void AssertPixelSpriteImportContract(Sprite sprite, string expectedDirectory)
        {
            string spritePath = AssetDatabase.GetAssetPath(sprite);
            StringAssert.StartsWith(expectedDirectory, spritePath);
            Assert.That(sprite.rect.width, Is.EqualTo(48f).Within(FloatTolerance));
            Assert.That(sprite.rect.height, Is.EqualTo(48f).Within(FloatTolerance));

            TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            Assert.IsNotNull(importer, $"Sprite 缺少 TextureImporter：{spritePath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(48f).Within(FloatTolerance));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            string importerMetadata = File.ReadAllText(spritePath + ".meta");
            StringAssert.Contains(
                "spriteGenerateFallbackPhysicsShape: 0",
                importerMetadata,
                $"Sprite 必须禁用回退物理形状：{spritePath}");
        }

        /// <summary>创建并显式初始化玩家属性、生命和能力管理器夹具。</summary>
        private AbilityFixture CreateFixture(string objectName)
        {
            GameObject player = CreateTrackedGameObject(objectName);
            PlayerStats stats = player.AddComponent<PlayerStats>();
            TestObjectUtility.InvokeNonPublicMethod(stats, "Awake");
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
            AbilityManager manager = player.AddComponent<AbilityManager>();
            TestObjectUtility.InvokeNonPublicMethod(manager, "Awake");
            return new AbilityFixture(stats, health, manager);
        }

        /// <summary>创建包含指定累计修改值的两级属性能力。</summary>
        private static AbilityDataSO CreateStatAbility(
            string abilityId,
            PlayerStatType statType,
            PlayerStatModifierMode mode,
            float levelOneValue,
            float levelTwoValue)
        {
            AbilityDataSO ability = CreateEmptyAbility(abilityId, 2);
            ability.levelConfigs[0].statModifiers.Add(
                new PlayerStatModifier(statType, mode, levelOneValue));
            ability.levelConfigs[1].statModifiers.Add(
                new PlayerStatModifier(statType, mode, levelTwoValue));
            return ability;
        }

        /// <summary>创建指定等级数且不含基础修改器的能力。</summary>
        private static AbilityDataSO CreateEmptyAbility(string abilityId, int maxLevel)
        {
            AbilityDataSO ability = ScriptableObject.CreateInstance<AbilityDataSO>();
            ability.abilityID = abilityId;
            ability.levelConfigs = new List<AbilityLevelData>();
            for (int level = 0; level < maxLevel; level++)
            {
                ability.levelConfigs.Add(new AbilityLevelData());
            }
            return ability;
        }

        /// <summary>保存核心能力测试所需的三个权威运行时组件。</summary>
        private sealed class AbilityFixture
        {
            /// <summary>建立不可变组件引用。</summary>
            public AbilityFixture(PlayerStats stats, PlayerHealth health, AbilityManager manager)
            {
                Stats = stats;
                Health = health;
                Manager = manager;
            }

            public PlayerStats Stats { get; }
            public PlayerHealth Health { get; }
            public AbilityManager Manager { get; }
        }
    }
}
