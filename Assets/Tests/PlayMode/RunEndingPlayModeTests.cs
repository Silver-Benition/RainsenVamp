using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>通过真实 MainLevel、对象池和 Player Loop 验证首领遭遇及世界切换锁。</summary>
    public sealed class RunEndingPlayModeTests : PlayModeComponentTestBase
    {
        private const string MainSceneName = "MainLevel";
        private const float FloatTolerance = 0.0001f;

        /// <summary>120 秒遭遇配置可在当前活动世界生成首领，并锁定世界切换但保留正常运行。</summary>
        [UnityTest]
        public IEnumerator MainLevel_RunDirector_生成武装巨像并锁定世界切换()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Component coordinator = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("WorldLineCoordinator")) as Component;
            Component resultsUi = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunResultsUI")) as Component;
            Assert.IsNotNull(director, "MainLevel 缺少 RunDirector。");
            Assert.IsNotNull(coordinator, "MainLevel 缺少 WorldLineCoordinator。");
            Assert.IsNotNull(resultsUi, "MainLevel 缺少 RunResultsUI。");
            Assert.IsNotNull(
                RuntimeComponentTestUtility.GetProperty<object>(director, "Telemetry"),
                "RunDirector 未建立本局统计容器。");
            Assert.IsFalse(
                RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "IsWorldSwitchLocked"));

            Assert.IsTrue(
                (bool)RuntimeComponentTestUtility.Invoke(director, "DebugTriggerBossEncounter"),
                "Boss 遭遇未能从当前活动世界对象池生成。");
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(director, "IsBossSpawned"));
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "IsWorldSwitchLocked"));

            bool worldBeforeAttempt = RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "MainWorldIsActive");
            RuntimeComponentTestUtility.Invoke(coordinator, "SwitchWorldLine");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "MainWorldIsActive"),
                Is.EqualTo(worldBeforeAttempt),
                "Boss 生成后仍可切换世界。");

            Component simulation = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                "ActiveWorldSimulation") as Component;
            Assert.IsNotNull(simulation);
            Component boss = simulation.GetComponentInChildren(
                RuntimeComponentTestUtility.RequireRuntimeType("BossEnemyController"),
                true);
            Assert.IsNotNull(boss, "活动世界中找不到已生成的武装巨像。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentHealth"),
                Is.EqualTo(800f).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentMoveSpeed"),
                Is.EqualTo(0.9f).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(boss, "CurrentCollisionDamage"),
                Is.EqualTo(18f).Within(FloatTolerance));
            Assert.IsFalse(
                RuntimeComponentTestUtility.GetProperty<bool>(boss, "IsDefanged"),
                "Boss 不应从普通敌人 Defang 逻辑继承免疫错误。");

            object phaseDamage = RuntimeComponentTestUtility.Invoke(
                boss,
                "ApplyCombatDamage",
                400f,
                false);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(phaseDamage, "Accepted"));
            yield return null;
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(boss, "IsPhaseTwoActive"),
                "Boss 生命值降至 50% 后未进入第二阶段。");
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(FloatTolerance), "仅进入二阶段不应冻结游戏。");
        }

        /// <summary>真实 Boss 致死命中必须先写入实际伤害，再只冻结一次胜利结果。</summary>
        [UnityTest]
        public IEnumerator MainLevel_Boss被武器致死_实际伤害进入胜利快照且只结算一次()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Component coordinator = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("WorldLineCoordinator")) as Component;
            Assert.IsNotNull(director);
            Assert.IsNotNull(coordinator);
            Assert.IsTrue((bool)RuntimeComponentTestUtility.Invoke(director, "DebugTriggerBossEncounter"));

            Component simulation = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                "ActiveWorldSimulation") as Component;
            Component boss = simulation.GetComponentInChildren(
                RuntimeComponentTestUtility.RequireRuntimeType("BossEnemyController"),
                true);
            Assert.IsNotNull(boss);

            yield return new WaitForSeconds(0.05f);
            ScriptableObject finisher = CreateWeaponData("weapon.test.boss_finisher", 1);
            object result = RuntimeComponentTestUtility.InvokeStatic(
                RuntimeComponentTestUtility.RequireRuntimeType("CombatDamageResolver"),
                "Apply",
                boss,
                900f,
                finisher,
                false,
                null);

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(result, "Accepted"));
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(result, "TargetDefeated"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(result, "AppliedDamage"),
                Is.EqualTo(800f).Within(FloatTolerance));
            Assert.IsFalse(boss.gameObject.activeSelf, "Boss 致死后必须从对象池回收。");
            Assert.IsNull(
                RuntimeComponentTestUtility.GetFieldValue<object>(boss, "enemyData"),
                "Boss 死亡出口不应拥有普通敌人掉落数据。");

            object finalSnapshot = RuntimeComponentTestUtility.GetProperty<object>(director, "FinalSnapshot");
            Assert.IsNotNull(finalSnapshot);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<object>(finalSnapshot, "Outcome").ToString(),
                Is.EqualTo("Victory"));

            object weaponRow = FindWeaponSnapshot(
                RuntimeComponentTestUtility.GetProperty<object>(finalSnapshot, "Weapons"),
                "weapon.test.boss_finisher");
            Assert.IsNotNull(weaponRow, "致死武器没有进入胜利结果的武器表。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(weaponRow, "ActualTotalDamage"),
                Is.EqualTo(800f).Within(FloatTolerance),
                "Boss 在实际扣血记账前冻结会丢失最后一击；该值必须是 800 而不是 0。");
            float damagePerSecond = RuntimeComponentTestUtility.GetProperty<float>(
                weaponRow,
                "DamagePerSecond");
            Assert.IsFalse(float.IsNaN(damagePerSecond));
            Assert.IsFalse(float.IsInfinity(damagePerSecond));
            Assert.That(damagePerSecond, Is.GreaterThan(0f));

            Component runState = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunState")) as Component;
            Assert.IsNotNull(runState);
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(runState, "KillCount"), Is.EqualTo(1));
            Component gameFlow = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("GameFlowManager")) as Component;
            Assert.IsNotNull(gameFlow);
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetFieldValue<bool>(gameFlow, "_runProgressCommitted"),
                "胜利结果建立后必须完成一次局内进度提交。");

            RuntimeComponentTestUtility.Invoke(director, "NotifyBossDefeated", boss);
            Assert.AreSame(
                finalSnapshot,
                RuntimeComponentTestUtility.GetProperty<object>(director, "FinalSnapshot"),
                "重复 Boss 胜利通知不得重新构造结果或重复提交。");
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(runState, "KillCount"), Is.EqualTo(1));
            Assert.IsTrue(RuntimeComponentTestUtility.GetFieldValue<bool>(gameFlow, "_runProgressCommitted"));
        }

        /// <summary>正式 WeaponAdded 事件必须区分起始武器与运行时新增武器，升级不重置获得时间。</summary>
        [UnityTest]
        public IEnumerator MainLevel_WeaponAdded事件_起始为零秒且升级不重置获得时间()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Component levelUpManager = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("LevelUpManager")) as Component;
            Assert.IsNotNull(director);
            Assert.IsNotNull(levelUpManager);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(
                levelUpManager,
                "IsInitialWeaponsReady"));

            object telemetry = RuntimeComponentTestUtility.GetProperty<object>(director, "Telemetry");
            object initialOwnedWeapons = RuntimeComponentTestUtility.GetProperty<object>(
                levelUpManager,
                "OwnedWeapons");
            object initialRowsObject = RuntimeComponentTestUtility.Invoke(
                telemetry,
                "CreateWeaponSnapshots",
                1f,
                initialOwnedWeapons);
            System.Collections.IList initialRows = initialRowsObject as System.Collections.IList;
            bool hasZeroSecondInitialWeapon = false;
            for (int index = 0; index < initialRows.Count; index++)
            {
                if (RuntimeComponentTestUtility.GetProperty<float>(
                        initialRows[index],
                        "FirstEffectTime") <= FloatTolerance)
                {
                    hasZeroSecondInitialWeapon = true;
                    break;
                }
            }
            Assert.IsTrue(hasZeroSecondInitialWeapon, "起始武器未按初始化完成事件登记为 0 秒。");

            ScriptableObject runtimeWeapon = CreateWeaponData("weapon.test.runtime_event", 2);
            Component createdWeapon = RuntimeComponentTestUtility.Invoke(
                levelUpManager,
                "DebugEnsureWeaponLevel",
                runtimeWeapon,
                1) as Component;
            Assert.IsNotNull(createdWeapon, "DebugEnsureWeaponLevel 未通过正式新武器授予路径创建武器。");

            object ownedWeapons = RuntimeComponentTestUtility.GetProperty<object>(
                levelUpManager,
                "OwnedWeapons");
            object firstRowsObject = RuntimeComponentTestUtility.Invoke(
                telemetry,
                "CreateWeaponSnapshots",
                1f,
                ownedWeapons);
            object firstRow = FindWeaponSnapshot(firstRowsObject, "weapon.test.runtime_event");
            Assert.IsNotNull(firstRow, "WeaponAdded 事件没有登记运行时武器。");
            float firstAcquisitionTime = RuntimeComponentTestUtility.GetProperty<float>(
                firstRow,
                "FirstEffectTime");
            Assert.That(
                firstAcquisitionTime,
                Is.GreaterThan(0f),
                "运行时新武器即使发生在首帧，也不得被时间窗口误判为起始武器。");

            RuntimeComponentTestUtility.Invoke(
                levelUpManager,
                "DebugEnsureWeaponLevel",
                runtimeWeapon,
                2);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(createdWeapon, "CurrentLevel"),
                Is.EqualTo(2));

            object secondRowsObject = RuntimeComponentTestUtility.Invoke(
                telemetry,
                "CreateWeaponSnapshots",
                1f,
                ownedWeapons);
            object secondRow = FindWeaponSnapshot(secondRowsObject, "weapon.test.runtime_event");
            Assert.IsNotNull(secondRow);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(secondRow, "FirstEffectTime"),
                Is.EqualTo(firstAcquisitionTime).Within(FloatTolerance),
                "升级已有武器不得重新登记获得时间。");
        }

        /// <summary>五类武器均通过真实触发回调把稳定来源送入统计，并在停用时清空池化来源。</summary>
        [UnityTest]
        public IEnumerator MainLevel_FiveWeaponHitCallbacks_稳定武器ID贯通且回池清源()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Assert.IsNotNull(director);
            object telemetry = RuntimeComponentTestUtility.GetProperty<object>(director, "Telemetry");
            int enemyLayer = RequireLayer("Enemy");
            Type bounceModeType = RuntimeComponentTestUtility.RequireRuntimeType("BounceMode");
            object directionalBounce = Enum.Parse(bounceModeType, "Directional");

            string[] weaponIds =
            {
                "weapon.test.source.projectile",
                "weapon.test.source.aura",
                "weapon.test.source.orbiting",
                "weapon.test.source.lobbed",
                "weapon.test.source.melee"
            };
            string[] runtimeTypes =
            {
                "ProjectileBase",
                "AuraDamageZone",
                "OrbitingProjectile",
                "LobbedProjectile",
                "MeleeSwingHitbox"
            };
            string[] sourceFields =
            {
                "weaponData",
                "weaponData",
                "_weaponData",
                "_weaponData",
                "_weaponData"
            };

            for (int index = 0; index < weaponIds.Length; index++)
            {
                ScriptableObject weapon = CreateWeaponData(weaponIds[index], 1);
                ScriptableObject enemyData = TrackObject(
                    RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
                RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 100f);
                RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 0f);
                RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 0f);

                GameObject enemyObject = CreateTrackedGameObject(
                    $"PlayModeTest_SourceEnemy_{index}",
                    false);
                enemyObject.layer = enemyLayer;
                Rigidbody2D enemyBody = enemyObject.AddComponent<Rigidbody2D>();
                enemyBody.bodyType = RigidbodyType2D.Kinematic;
                enemyBody.gravityScale = 0f;
                BoxCollider2D enemyCollider = enemyObject.AddComponent<BoxCollider2D>();
                enemyCollider.size = Vector2.one;
                Component enemy = RuntimeComponentTestUtility.AddRuntimeComponent(enemyObject, "EnemyBase");
                RuntimeComponentTestUtility.SetField(enemy, "enemyData", enemyData);
                enemyObject.SetActive(true);

                GameObject attackObject = CreateTrackedGameObject(
                    $"PlayModeTest_SourceAttack_{index}",
                    false);
                Component attack;
                switch (index)
                {
                    case 0:
                        attackObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                        attackObject.AddComponent<BoxCollider2D>().isTrigger = true;
                        attack = RuntimeComponentTestUtility.AddRuntimeComponent(attackObject, runtimeTypes[index]);
                        attackObject.SetActive(true);
                        RuntimeComponentTestUtility.Invoke(
                            attack,
                            "Initialize",
                            weapon,
                            Vector3.right,
                            10f,
                            0f,
                            1,
                            10f,
                            0,
                            directionalBounce,
                            1f);
                        RuntimeComponentTestUtility.Invoke(attack, "OnTriggerEnter2D", enemyCollider);
                        break;
                    case 1:
                        attackObject.AddComponent<CircleCollider2D>().isTrigger = true;
                        attack = RuntimeComponentTestUtility.AddRuntimeComponent(attackObject, runtimeTypes[index]);
                        attackObject.SetActive(true);
                        RuntimeComponentTestUtility.Invoke(
                            attack,
                            "Initialize",
                            weapon,
                            attackObject.transform,
                            0.5f,
                            10f,
                            10f,
                            2f);
                        RuntimeComponentTestUtility.Invoke(attack, "OnTriggerEnter2D", enemyCollider);
                        RuntimeComponentTestUtility.Invoke(attack, "TickDamage");
                        break;
                    case 2:
                        attackObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                        attackObject.AddComponent<CircleCollider2D>().isTrigger = true;
                        attack = RuntimeComponentTestUtility.AddRuntimeComponent(attackObject, runtimeTypes[index]);
                        attackObject.SetActive(true);
                        RuntimeComponentTestUtility.Invoke(
                            attack,
                            "Initialize",
                            weapon,
                            attackObject.transform,
                            10f,
                            1f);
                        RuntimeComponentTestUtility.Invoke(attack, "OnTriggerEnter2D", enemyCollider);
                        break;
                    case 3:
                        attackObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                        attackObject.AddComponent<BoxCollider2D>().isTrigger = true;
                        attackObject.AddComponent<SpriteRenderer>();
                        attack = RuntimeComponentTestUtility.AddRuntimeComponent(attackObject, runtimeTypes[index]);
                        attackObject.SetActive(true);
                        RuntimeComponentTestUtility.Invoke(
                            attack,
                            "Initialize",
                            weapon,
                            attackObject.transform.position,
                            Vector3.zero,
                            Vector3.right,
                            10f,
                            0f,
                            10f,
                            1,
                            3f,
                            0f,
                            1f);
                        RuntimeComponentTestUtility.Invoke(attack, "OnTriggerEnter2D", enemyCollider);
                        break;
                    default:
                        attackObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                        attackObject.AddComponent<CapsuleCollider2D>().isTrigger = true;
                        attack = RuntimeComponentTestUtility.AddRuntimeComponent(attackObject, runtimeTypes[index]);
                        attackObject.SetActive(true);
                        RuntimeComponentTestUtility.Invoke(
                            attack,
                            "Initialize",
                            weapon,
                            attackObject.transform,
                            true,
                            10f,
                            2f,
                            90f,
                            1f,
                            0f);
                        RuntimeComponentTestUtility.Invoke(attack, "OnTriggerEnter2D", enemyCollider);
                        break;
                }

                attackObject.SetActive(false);
                Assert.IsNull(
                    RuntimeComponentTestUtility.GetFieldValue<object>(attack, sourceFields[index]),
                    $"{runtimeTypes[index]} 停用时未清除池化武器来源。");
            }

            object rowsObject = RuntimeComponentTestUtility.Invoke(
                telemetry,
                "CreateWeaponSnapshots",
                1f,
                null);
            for (int index = 0; index < weaponIds.Length; index++)
            {
                object row = FindWeaponSnapshot(rowsObject, weaponIds[index]);
                Assert.IsNotNull(row, $"{runtimeTypes[index]} 的真实命中没有写入稳定 weaponID。");
                Assert.That(
                    RuntimeComponentTestUtility.GetProperty<float>(row, "ActualTotalDamage"),
                    Is.EqualTo(10f).Within(FloatTolerance));
            }
        }

        /// <summary>创建可被统计系统识别的运行时武器测试资产，并配置所需等级数量。</summary>
        private ScriptableObject CreateWeaponData(string weaponId, int levelCount)
        {
            ScriptableObject weapon = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("WeaponDataSO"));
            RuntimeComponentTestUtility.SetField(weapon, "weaponID", weaponId);
            RuntimeComponentTestUtility.SetField(weapon, "weaponNameKey", weaponId + ".name");
            RuntimeComponentTestUtility.SetField(weapon, "weaponDisplayName", weaponId);

            Type levelDataType = RuntimeComponentTestUtility.RequireRuntimeType("WeaponLevelData");
            Type levelListType = typeof(List<>).MakeGenericType(levelDataType);
            IList levelConfigs = (IList)Activator.CreateInstance(levelListType);
            for (int index = 0; index < Mathf.Max(1, levelCount); index++)
            {
                levelConfigs.Add(Activator.CreateInstance(levelDataType));
            }

            RuntimeComponentTestUtility.SetField(weapon, "levelConfigs", levelConfigs);
            return weapon;
        }

        /// <summary>按稳定 WeaponId 从反射返回的结果行集合中取得唯一武器行。</summary>
        private static object FindWeaponSnapshot(object rowsObject, string weaponId)
        {
            System.Collections.IList rows = rowsObject as System.Collections.IList;
            if (rows == null)
            {
                return null;
            }

            for (int index = 0; index < rows.Count; index++)
            {
                object row = rows[index];
                if (row != null && string.Equals(
                        RuntimeComponentTestUtility.GetProperty<string>(row, "WeaponId"),
                        weaponId,
                        StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }
    }
}
