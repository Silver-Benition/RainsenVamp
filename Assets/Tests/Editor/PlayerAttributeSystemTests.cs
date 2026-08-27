using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证角色数据、稳定来源修改器、属性消费与武器运行时快照。</summary>
    public sealed class PlayerAttributeSystemTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>Flat、加算百分比与独立乘区应使用固定顺序，且不修改角色共享资产。</summary>
        [Test]
        public void SetModifiers_三类修改方式_按固定顺序生成最终值()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                character.baseStats.moveSpeed = 3f;
                PlayerStats stats = CreatePlayerStats(character);
                var modifiers = new List<PlayerStatModifier>
                {
                    new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.Flat, 0.5f),
                    new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.AdditivePercent, 0.2f),
                    new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.AdditivePercent, 0.1f),
                    new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.Multiplicative, 0.8f)
                };

                Assert.IsTrue(stats.SetModifiers("test.movement_combo", modifiers));

                Assert.That(stats.FinalMoveSpeed, Is.EqualTo(3.64f).Within(FloatTolerance));
                Assert.That(character.baseStats.moveSpeed, Is.EqualTo(3f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>同一 sourceId 的能力升级必须替换旧值，移除来源后恢复角色基础值。</summary>
        [Test]
        public void SetModifiers_同一稳定来源升级_替换旧加成且可完整移除()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                PlayerStats stats = CreatePlayerStats(character);
                int changedCount = 0;
                stats.StatsChanged += () => changedCount++;

                stats.SetModifiers(
                    "ability.spinach",
                    new[]
                    {
                        new PlayerStatModifier(
                            PlayerStatType.Might,
                            PlayerStatModifierMode.AdditivePercent,
                            0.1f)
                    });
                Assert.That(stats.Might, Is.EqualTo(1.1f).Within(FloatTolerance));

                stats.SetModifiers(
                    "ability.spinach",
                    new[]
                    {
                        new PlayerStatModifier(
                            PlayerStatType.Might,
                            PlayerStatModifierMode.AdditivePercent,
                            0.25f)
                    });
                Assert.That(stats.Might, Is.EqualTo(1.25f).Within(FloatTolerance));

                Assert.IsTrue(stats.RemoveModifiers("ability.spinach"));
                Assert.That(stats.Might, Is.EqualTo(1f).Within(FloatTolerance));
                Assert.That(changedCount, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>Growth 应只在经验进入玩家时乘算一次，并继续沿用逐级经验队列。</summary>
        [Test]
        public void AddExp_角色成长倍率_经验只乘算一次()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                character.baseStats.growth = 1.5f;
                PlayerStats stats = CreatePlayerStats(character);

                stats.AddExp(4f);

                Assert.That(stats.currentExp, Is.EqualTo(6f).Within(FloatTolerance));
                Assert.That(stats.currentLevel, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>最大生命与护甲应由 PlayerStats 驱动；增加生命上限不能隐式治疗。</summary>
        [Test]
        public void PlayerHealth_角色生命与护甲_同步上限并执行最低一点伤害()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                character.baseStats.maxHealth = 120f;
                character.baseStats.armor = 20f;

                GameObject player = CreateTrackedGameObject("AutomationTest_AttributeHealth");
                player.SetActive(false);
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(character);
                PlayerHealth health = player.AddComponent<PlayerHealth>();
                TestObjectUtility.SetFloat(health, "invulnerabilityDuration", 0f);
                TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
                TestObjectUtility.InvokeNonPublicMethod(health, "OnEnable");

                health.TakeDamage(10f);
                Assert.That(health.CurrentHealth, Is.EqualTo(119f).Within(FloatTolerance));

                stats.SetModifiers(
                    "ability.health_up",
                    new[]
                    {
                        new PlayerStatModifier(
                            PlayerStatType.MaxHealth,
                            PlayerStatModifierMode.Flat,
                            30f)
                    });

                Assert.That(health.MaxHealth, Is.EqualTo(150f).Within(FloatTolerance));
                Assert.That(health.CurrentHealth, Is.EqualTo(119f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>磁吸 Collider 只在属性变化时刷新，并使用角色配置的世界单位半径。</summary>
        [Test]
        public void PlayerMagnet_磁吸属性变化_同步圆形触发器半径()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                character.baseStats.magnet = 4f;
                GameObject player = CreateTrackedGameObject("AutomationTest_AttributeMagnet");
                player.SetActive(false);
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(character);

                GameObject magnetObject = new GameObject("MagnetRadius");
                magnetObject.transform.SetParent(player.transform, false);
                CircleCollider2D trigger = magnetObject.AddComponent<CircleCollider2D>();
                trigger.isTrigger = true;
                PlayerMagnet magnet = magnetObject.AddComponent<PlayerMagnet>();
                TestObjectUtility.InvokeNonPublicMethod(magnet, "Awake");
                TestObjectUtility.InvokeNonPublicMethod(magnet, "OnEnable");

                Assert.That(trigger.radius, Is.EqualTo(4f).Within(FloatTolerance));

                stats.SetModifiers(
                    "ability.magnet_up",
                    new[]
                    {
                        new PlayerStatModifier(
                            PlayerStatType.Magnet,
                            PlayerStatModifierMode.Flat,
                            2f)
                    });

                Assert.That(trigger.radius, Is.EqualTo(6f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>WeaponBase 应把角色六项战斗属性转换为一次攻击使用的确定性数值。</summary>
        [Test]
        public void WeaponBase_角色战斗属性_生成正确运行时快照()
        {
            CharacterDataSO character = CreateCharacterData();
            WeaponDataSO weaponData = ScriptableObject.CreateInstance<WeaponDataSO>();
            try
            {
                character.baseStats.might = 1.5f;
                character.baseStats.area = 2f;
                character.baseStats.projectileSpeed = 1.25f;
                character.baseStats.duration = 1.4f;
                character.baseStats.amount = 2f;
                character.baseStats.cooldown = 0.75f;

                GameObject player = CreateTrackedGameObject("AutomationTest_AttributeWeapon");
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(character);
                GameObject weaponObject = new GameObject("Weapon");
                weaponObject.transform.SetParent(player.transform, false);
                WeaponBase weapon = weaponObject.AddComponent<WeaponBase>();
                TestObjectUtility.InvokeNonPublicMethod(weapon, "Awake");

                weaponData.levelConfigs = new List<WeaponLevelData>
                {
                    new WeaponLevelData
                    {
                        damage = 10f,
                        cooldown = 2f,
                        projectileCount = 1,
                        projectileSpeed = 4f,
                        lifeTime = 3f,
                        auraRadius = 2f
                    }
                };
                weapon.weaponData = weaponData;

                Assert.That(
                    TestObjectUtility.InvokeNonPublicMethod<float>(weapon, "GetCurrentDamage"),
                    Is.EqualTo(15f).Within(FloatTolerance));
                Assert.That(
                    TestObjectUtility.InvokeNonPublicMethod<float>(weapon, "GetCurrentCooldown"),
                    Is.EqualTo(1.5f).Within(FloatTolerance));
                Assert.That(
                    TestObjectUtility.InvokeNonPublicMethod<float>(weapon, "GetCurrentProjectileSpeed"),
                    Is.EqualTo(5f).Within(FloatTolerance));
                Assert.That(
                    TestObjectUtility.InvokeNonPublicMethod<int>(weapon, "GetCurrentProjectileCount"),
                    Is.EqualTo(3));
                Assert.That(InvokeProtectedFloat(weapon, "GetModifiedDuration", 3f),
                    Is.EqualTo(4.2f).Within(FloatTolerance));
                Assert.That(InvokeProtectedFloat(weapon, "GetModifiedArea", 2f),
                    Is.EqualTo(4f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(weaponData);
            }
        }

        /// <summary>创建使用中性《吸血鬼幸存者》式基准值的临时角色资产。</summary>
        private static CharacterDataSO CreateCharacterData()
        {
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            character.characterID = "character_test";
            character.baseStats = new CharacterBaseStats();
            return character;
        }

        /// <summary>创建绑定指定角色数据的真实 PlayerStats 组件。</summary>
        private PlayerStats CreatePlayerStats(CharacterDataSO character)
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_PlayerAttributes");
            PlayerStats stats = player.AddComponent<PlayerStats>();
            stats.SetCharacterData(character);
            return stats;
        }

        /// <summary>调用带一个浮点参数的受保护武器计算方法，并展开反射异常。</summary>
        private static float InvokeProtectedFloat(WeaponBase weapon, string methodName, float argument)
        {
            MethodInfo method = typeof(WeaponBase).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"WeaponBase 缺少方法：{methodName}");
            return (float)method.Invoke(weapon, new object[] { argument });
        }
    }
}
