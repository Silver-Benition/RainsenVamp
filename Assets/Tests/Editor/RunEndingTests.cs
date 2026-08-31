using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证 Session 19 的结果快照、实际扣血、稳定身份和未来拾取上报边界。</summary>
    public sealed class RunEndingTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>武器统计应按正式获得时间保序，并只累计真实扣除的生命值。</summary>
        [Test]
        public void RunTelemetry_武器首次获得时间与实际伤害_升级或重复登记不重置()
        {
            WeaponDataSO firstWeapon = CreateWeapon("weapon.telemetry.first", "首发武器", 3);
            WeaponDataSO secondWeapon = CreateWeapon("weapon.telemetry.second", "后加入武器", 2);
            var telemetry = new RunTelemetry();

            try
            {
                Assert.IsTrue(telemetry.RegisterWeapon(firstWeapon, 0f));
                Assert.IsTrue(telemetry.RegisterWeapon(secondWeapon, 10f));
                Assert.IsFalse(telemetry.RegisterWeapon(firstWeapon, 30f));

                telemetry.RecordWeaponDamage(firstWeapon, 100f, 12f);
                telemetry.RecordWeaponDamage(firstWeapon, 75f, 20f);
                telemetry.RecordWeaponDamage(secondWeapon, 50f, 20f);

                List<RunResultWeaponSnapshot> rows =
                    telemetry.CreateWeaponSnapshots(40f, null);

                Assert.That(rows.Count, Is.EqualTo(2));
                Assert.That(rows[0].WeaponId, Is.EqualTo(firstWeapon.weaponID));
                Assert.That(rows[0].FirstEffectTime, Is.EqualTo(0f).Within(FloatTolerance));
                Assert.That(rows[0].ActualTotalDamage, Is.EqualTo(175f).Within(FloatTolerance));
                Assert.That(rows[0].DamagePerSecond, Is.EqualTo(4.375f).Within(FloatTolerance));
                Assert.That(rows[1].WeaponId, Is.EqualTo(secondWeapon.weaponID));
                Assert.That(rows[1].FirstEffectTime, Is.EqualTo(10f).Within(FloatTolerance));
                Assert.That(rows[1].ActualTotalDamage, Is.EqualTo(50f).Within(FloatTolerance));
                Assert.That(rows[1].DamagePerSecond, Is.EqualTo(50f / 30f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(firstWeapon);
                Object.DestroyImmediate(secondWeapon);
            }
        }

        /// <summary>致死过量伤害必须按剩余生命封顶，并通过统一解析器写入武器统计。</summary>
        [Test]
        public void CombatDamageResolver_致死过量伤害_只记录目标实际扣血()
        {
            EnemyDataSO enemyData = ScriptableObject.CreateInstance<EnemyDataSO>();
            WeaponDataSO weaponData = CreateWeapon("weapon.telemetry.overkill", "过量测试武器", 1);
            GameObject enemyObject = CreateTrackedGameObject("AutomationTest_OverkillEnemy");
            enemyObject.SetActive(false);
            enemyObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            enemyObject.AddComponent<BoxCollider2D>();
            EnemyBase enemy = enemyObject.AddComponent<EnemyBase>();
            enemy.enemyData = enemyData;
            enemyObject.SetActive(true);
            enemy.ApplySpawnSnapshot(new EnemySpawnSnapshot(100f, 0f, 0f, 1f, false));
            var telemetry = new RunTelemetry();

            try
            {
                CombatDamageResult result = CombatDamageResolver.Apply(
                    enemy,
                    150f,
                    weaponData,
                    false,
                    telemetry);

                Assert.IsTrue(result.Accepted);
                Assert.IsTrue(result.TargetDefeated);
                Assert.That(result.AppliedDamage, Is.EqualTo(100f).Within(FloatTolerance));
                Assert.That(enemy.CurrentHealth, Is.EqualTo(0f).Within(FloatTolerance));

                List<RunResultWeaponSnapshot> rows = telemetry.CreateWeaponSnapshots(5f, null);
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].ActualTotalDamage, Is.EqualTo(100f).Within(FloatTolerance));

                CombatDamageResult lateResult = CombatDamageResolver.Apply(
                    enemy,
                    20f,
                    weaponData,
                    false,
                    telemetry);
                Assert.IsFalse(lateResult.Accepted);
                Assert.That(rows[0].ActualTotalDamage, Is.EqualTo(100f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(enemyData);
                Object.DestroyImmediate(weaponData);
            }
        }

        /// <summary>结果快照必须复制输入容器，冻结后外部列表变化不能影响结果页。</summary>
        [Test]
        public void RunResultSnapshot_冻结后复制集合_外部列表变化不影响结果()
        {
            WeaponDataSO weapon = CreateWeapon("weapon.snapshot", "快照武器", 1);
            var weaponRows = new List<RunResultWeaponSnapshot>
            {
                new RunResultWeaponSnapshot(
                    weapon.weaponID,
                    weapon.weaponNameKey,
                    weapon.weaponDisplayName,
                    null,
                    1,
                    weapon.MaxLevel,
                    12f,
                    0f,
                    12f)
            };
            var pickupRows = new List<RunResultPickupSnapshot>();

            try
            {
                var snapshot = new RunResultSnapshot(
                    RunOutcome.Victory,
                    false,
                    "map.double_world_trial",
                    "双世界试炼",
                    120f,
                    8,
                    3,
                    4,
                    new RunResultCharacterSnapshot("character.test", "character.test.name", "测试角色", null),
                    weaponRows,
                    new List<RunResultAbilitySnapshot>(),
                    new List<RunResultAbilitySnapshot>(),
                    pickupRows);

                weaponRows.Clear();
                pickupRows.Add(new RunResultPickupSnapshot(
                    "pickup.late",
                    "pickup.late.name",
                    "迟到拾取",
                    null,
                    0,
                    1));

                Assert.That(snapshot.Weapons.Count, Is.EqualTo(1));
                Assert.That(snapshot.InstantEffectPickups.Count, Is.Zero);
                Assert.That(snapshot.MapDisplayName, Is.EqualTo("双世界试炼"));
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        /// <summary>即时效果只在成功报告后累计，同一稳定 ID 聚合并在冻结后拒绝迟到报告。</summary>
        [Test]
        public void RunTelemetry_未来即时效果上报_稳定排序且冻结后拒绝()
        {
            MapInstantEffectPickupDataSO latePickup = CreatePickup(
                "pickup.telemetry.late",
                "后排序拾取",
                20);
            MapInstantEffectPickupDataSO earlyPickup = CreatePickup(
                "pickup.telemetry.early",
                "先排序拾取",
                5);
            var telemetry = new RunTelemetry();

            try
            {
                Assert.IsTrue(telemetry.ReportInstantEffectPickup(latePickup));
                Assert.IsTrue(telemetry.ReportInstantEffectPickup(earlyPickup));
                Assert.IsTrue(telemetry.ReportInstantEffectPickup(latePickup));

                List<RunResultPickupSnapshot> rows = telemetry.CreatePickupSnapshots();
                Assert.That(rows.Count, Is.EqualTo(2));
                Assert.That(rows[0].PickupId, Is.EqualTo(earlyPickup.pickupID));
                Assert.That(rows[0].Count, Is.EqualTo(1));
                Assert.That(rows[1].PickupId, Is.EqualTo(latePickup.pickupID));
                Assert.That(rows[1].Count, Is.EqualTo(2));

                telemetry.Freeze();
                Assert.IsFalse(telemetry.ReportInstantEffectPickup(earlyPickup));
                Assert.That(telemetry.CreatePickupSnapshots()[0].Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(latePickup);
                Object.DestroyImmediate(earlyPickup);
            }
        }

        /// <summary>首领配置必须保留固定基础属性、免 Defang 和 8/12 向阶段数值。</summary>
        [Test]
        public void BossDataSO_武装巨像配置_基础属性与阶段数值正确()
        {
            BossDataSO boss = ScriptableObject.CreateInstance<BossDataSO>();
            boss.maxHealth = 800f;
            boss.moveSpeed = 0.9f;
            boss.contactDamage = 18f;
            boss.canBeDefanged = false;
            boss.phaseTwoHealthRatio = 0.5f;
            boss.phaseOne.projectileCount = 8;
            boss.phaseOne.interval = 3f;
            boss.phaseOne.projectileDamage = 10f;
            boss.phaseOne.projectileSpeed = 4.5f;
            boss.phaseTwo.projectileCount = 12;
            boss.phaseTwo.interval = 2f;
            boss.phaseTwo.projectileDamage = 12f;
            boss.phaseTwo.projectileSpeed = 5.5f;

            try
            {
                EnemySpawnSnapshot spawn = boss.CreateSpawnSnapshot();
                Assert.That(spawn.MaxHealth, Is.EqualTo(800f).Within(FloatTolerance));
                Assert.That(spawn.MoveSpeed, Is.EqualTo(0.9f).Within(FloatTolerance));
                Assert.That(spawn.CollisionDamage, Is.EqualTo(18f).Within(FloatTolerance));
                Assert.IsFalse(spawn.IsDefanged);
                Assert.That(boss.GetPhase(false).GetSafeProjectileCount(), Is.EqualTo(8));
                Assert.That(boss.GetPhase(false).GetSafeInterval(), Is.EqualTo(3f).Within(FloatTolerance));
                Assert.That(boss.GetPhase(true).GetSafeProjectileCount(), Is.EqualTo(12));
                Assert.That(boss.GetPhase(true).GetSafeInterval(), Is.EqualTo(2f).Within(FloatTolerance));
                Assert.That(boss.GetPhase(true).projectileDamage, Is.EqualTo(12f).Within(FloatTolerance));
                Assert.That(boss.GetPhase(true).projectileSpeed, Is.EqualTo(5.5f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        /// <summary>能力结果分类必须来自显式字段，而不是 mechanic 是否为空。</summary>
        [Test]
        public void AbilityDataSO_结果页分类_显式支持Item与Ability()
        {
            AbilityDataSO item = ScriptableObject.CreateInstance<AbilityDataSO>();
            AbilityDataSO ability = ScriptableObject.CreateInstance<AbilityDataSO>();

            try
            {
                item.presentationCategory = AbilityPresentationCategory.Item;
                ability.presentationCategory = AbilityPresentationCategory.Ability;
                Assert.That(item.presentationCategory, Is.EqualTo(AbilityPresentationCategory.Item));
                Assert.That(ability.presentationCategory, Is.EqualTo(AbilityPresentationCategory.Ability));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>创建测试用武器资产，使用稳定 ID 和显式等级配置避免依赖项目内容资产。</summary>
        private static WeaponDataSO CreateWeapon(string id, string displayName, int maxLevel)
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            data.name = id;
            data.weaponID = id;
            data.weaponNameKey = id + ".name";
            data.weaponDisplayName = displayName;
            data.levelConfigs = new List<WeaponLevelData>();
            for (int index = 0; index < Mathf.Max(1, maxLevel); index++)
            {
                data.levelConfigs.Add(new WeaponLevelData());
            }

            return data;
        }

        /// <summary>创建测试用未来即时效果配置，验证稳定 ID、显示名和排序字段。</summary>
        private static MapInstantEffectPickupDataSO CreatePickup(
            string id,
            string displayName,
            int sortOrder)
        {
            var data = ScriptableObject.CreateInstance<MapInstantEffectPickupDataSO>();
            data.name = id;
            data.pickupID = id;
            data.nameKey = id + ".name";
            data.displayName = displayName;
            data.sortOrder = sortOrder;
            return data;
        }
    }
}
