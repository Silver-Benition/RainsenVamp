using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RainsenVampSur.Tests
{
    /// <summary>真实内容目录、羁绊替换、原版概率边界和低压逐波曲线的确定性验证。</summary>
    public sealed class WeaponSetsAndEnemyWaveTests
    {
        private readonly List<Object> _objects = new List<Object>();

        /// <summary>释放测试副本，不写入策划资产或账号。</summary>
        [TearDown] public void Cleanup()
        { for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]); _objects.Clear(); }

        /// <summary>正式二十二把武器都有有效标签，各作者 JSON 重建后仍引用相同资产。</summary>
        [Test] public void Catalog_AllWeaponsUseExplicitSetsAndAuthoringRoundTrips()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
            int count = 0, available = 0;
            foreach (RunShopProduct product in catalog.products)
            {
                if (!product.IsWeapon) continue;
                count++; WeaponDataSO data = product.content.weaponToGrant;
                if (!data.retiredFromPool) available++;
                Assert.That(data.weaponSets.Length, Is.InRange(1, 2), data.weaponID);
                var ids = new HashSet<string>();
                foreach (WeaponSetSO set in data.weaponSets)
                {
                    Assert.NotNull(set, data.weaponID); Assert.IsTrue(ids.Add(set.GetStableId()));
                    Assert.AreEqual(5, set.tiers.Length); Assert.IsEmpty(set.GetModifiers(1));
                    Assert.IsNotEmpty(set.GetModifiers(2)); Assert.AreEqual(set.GetModifiers(6), set.GetModifiers(99));
                }
            }
            Assert.AreEqual(22, count);
            Assert.AreEqual(21, available, "既有退池武器不得因添加标签重新入池。");
            var content = JsonUtility.FromJson<ContentExpansionSetup.Definitions>(System.IO.File.ReadAllText(ContentExpansionSetup.Root + "/definitions.json"));
            foreach (var weapon in content.weapons)
                CollectionAssert.AreEqual(WeaponSetAuthoring.Resolve(weapon.sets), AssetDatabase.LoadAssetAtPath<WeaponDataSO>(ContentExpansionSetup.Root + "/Weapons/" + weapon.id + ".asset").weaponSets);
            var engineering = JsonUtility.FromJson<EngineeringExpansionSetup.Definition>(System.IO.File.ReadAllText(EngineeringExpansionSetup.Root + "/definitions.json"));
            foreach (var weapon in engineering.weapons)
                CollectionAssert.AreEqual(WeaponSetAuthoring.Resolve(weapon.sets), AssetDatabase.LoadAssetAtPath<WeaponDataSO>(EngineeringExpansionSetup.Root + "/Weapons/" + weapon.id + ".asset").weaponSets);
        }

        /// <summary>同武器副本与多个标签同时累计；重复配置、重复通知和高品质不能重复增加属性。</summary>
        [Test] public void Bonuses_CountCopiesAndReplaceCurrentTierWithoutStacking()
        {
            var playerObject = new GameObject("SetTest"); _objects.Add(playerObject);
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character); character.useBrotatoStats = true;
            var player = playerObject.AddComponent<PlayerStats>(); player.SetCharacterData(character);
            WeaponDataSO data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/ContentExpansion/Weapons/01_copper_rapier.asset")); _objects.Add(data);
            WeaponSetSO blade = data.weaponSets[0], precise = data.weaponSets[1];
            data.weaponSets = new[] { blade, precise, precise };
            var weapons = new List<WeaponBase>(); var bonuses = new WeaponSetBonuses();
            float melee = player.GetFinalStat(PlayerStatType.MeleeDamage), crit = player.GetFinalStat(PlayerStatType.CritChance);
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject("Copy" + i); _objects.Add(go); go.SetActive(false);
                var weapon = go.AddComponent<WeaponBase>(); weapon.weaponData = data; weapons.Add(weapon);
                bonuses.Rebuild(weapons, player); bonuses.Rebuild(weapons, player);
                Assert.AreEqual(i + 1, bonuses.Count(precise));
                Assert.AreEqual(melee + i, player.GetFinalStat(PlayerStatType.MeleeDamage));
                Assert.AreEqual(crit + i * 3, player.GetFinalStat(PlayerStatType.CritChance));
                Assert.AreEqual(i, player.GetFinalStat(PlayerStatType.LifeSteal));
            }
            Assert.IsTrue(weapons[0].TryLevelUp()); bonuses.Rebuild(weapons, player);
            Assert.AreEqual(6, bonuses.Count(blade)); Assert.AreEqual(crit + 15, player.GetFinalStat(PlayerStatType.CritChance));
            weapons.RemoveAt(5); bonuses.Rebuild(weapons, player);
            Assert.AreEqual(crit + 12, player.GetFinalStat(PlayerStatType.CritChance));
            bonuses.Clear(); Assert.AreEqual(crit, player.GetFinalStat(PlayerStatType.CritChance)); Assert.AreEqual(0, bonuses.Count(blade));
        }

        /// <summary>原版阈值样本覆盖早期强化与稳定期，确保同标签是额外百分之十五。</summary>
        [TestCase(1, .249f, WeaponPreference.SameWeapon)] [TestCase(1, .25f, WeaponPreference.SharedSet)]
        [TestCase(1, .549f, WeaponPreference.SharedSet)] [TestCase(1, .551f, WeaponPreference.Any)]
        [TestCase(6, .199f, WeaponPreference.SameWeapon)] [TestCase(6, .20f, WeaponPreference.SharedSet)]
        [TestCase(6, .349f, WeaponPreference.SharedSet)] [TestCase(6, .35f, WeaponPreference.Any)]
        public void Preference_UsesCumulativeOriginalThresholds(int wave, float sample, WeaponPreference expected)
        { Assert.AreEqual(expected, WeaponShopPreference.Roll(wave, sample)); }

        /// <summary>实际资产按敌种成长；首五波明显低于原先，二十波仍保留成长差异。</summary>
        [TestCase(1, 6, 8)] [TestCase(5, 12, 12)] [TestCase(10, 19, 17)] [TestCase(20, 34, 27)]
        public void EnemyGrowth_AuthoredHealthAndIndependentAttackDamage(int wave, float weakHp, float rangedHp)
        {
            var weak = AssetDatabase.LoadAssetAtPath<EnemyDataSO>("Assets/Data/WeakEnemy_1.asset");
            var ranged = AssetDatabase.LoadAssetAtPath<EnemyDataSO>("Assets/Data/RangedEnemy_1.asset");
            EnemySpawnSnapshot a = EnemySpawnSnapshotFactory.CreateForRound(weak, null, 1, wave, 1, 1);
            EnemySpawnSnapshot b = EnemySpawnSnapshotFactory.CreateForRound(ranged, null, 1, wave, 1, 1);
            Assert.AreEqual(weakHp, a.MaxHealth); Assert.AreEqual(rangedHp, b.MaxHealth);
            Assert.That(a.CollisionDamage, Is.EqualTo(1 + (wave - 1) * .12f).Within(.0001f));
            Assert.That(b.ResolveOutgoingDamage(2), Is.EqualTo(2 + (wave - 1) * .08f).Within(.0001f));
            Assert.AreEqual(1.8f, a.MoveSpeed); Assert.AreEqual(1.5f, b.MoveSpeed);
            Assert.AreEqual(6, weak.maxHealth); Assert.AreEqual(8, ranged.maxHealth);
            var config = AssetDatabase.LoadAssetAtPath<RoundRunConfigSO>("Assets/Data/Rounds/Standard20.asset");
            Assert.AreEqual(1, config.rounds[wave - 1].enemyHealthMultiplier);
            Assert.AreEqual(1, config.rounds[wave - 1].enemyDamageMultiplier);
        }

        /// <summary>难度倍率在敌种成长后应用，旧非回合入口仍只取基础值；削弱敌人不漏出新增远程伤害。</summary>
        [Test] public void EnemyGrowth_DifficultyAndLegacyBoundaries()
        {
            var data = ScriptableObject.CreateInstance<EnemyDataSO>(); _objects.Add(data);
            data.maxHealth = 8; data.healthPerWave = 1; data.collisionDamage = 2;
            data.contactDamagePerWave = .5f; data.projectileDamagePerWave = .25f;
            EnemySpawnSnapshot snapshot = EnemySpawnSnapshotFactory.CreateForRound(data, null, 1, 5, 2, 3);
            Assert.AreEqual(24, snapshot.MaxHealth); Assert.AreEqual(12, snapshot.CollisionDamage);
            Assert.AreEqual(9, snapshot.ResolveOutgoingDamage(2));
            Assert.AreEqual(8, EnemySpawnSnapshotFactory.Create(data, null, 1).MaxHealth);
            var defanged = new EnemySpawnSnapshot(8, 2, 0, 3, true, 20);
            Assert.AreEqual(0, defanged.ResolveOutgoingDamage(10));
        }

        /// <summary>说明展示当前实际减伤、负护甲增伤和封顶闪避；重新查询会反映属性变化。</summary>
        [Test] public void StatHelp_ReportsCurrentClampedEffects()
        {
            var go = new GameObject("DescriptionTest"); _objects.Add(go);
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character); character.useBrotatoStats = true;
            var player = go.AddComponent<PlayerStats>(); player.SetCharacterData(character);
            player.SetModifiers("test", new[] { new PlayerStatModifier(PlayerStatType.Armor, PlayerStatModifierMode.Flat, 15.9f) });
            Assert.AreEqual("你受到的伤害减少 50%。", PlayerStatDescriptions.Get(PlayerStatType.Armor, player));
            player.SetModifiers("test", new[] { new PlayerStatModifier(PlayerStatType.Armor, PlayerStatModifierMode.Flat, -15.9f),
                new PlayerStatModifier(PlayerStatType.Dodge, PlayerStatModifierMode.Flat, 80) });
            Assert.AreEqual("你受到的伤害增加 50%。", PlayerStatDescriptions.Get(PlayerStatType.Armor, player));
            Assert.AreEqual("有 60% 概率避开攻击。", PlayerStatDescriptions.Get(PlayerStatType.Dodge, player));
            Assert.Less(PlayerStatDescriptions.Get(PlayerStatType.LifeSteal, player).Length, 35);
        }
    }
}
