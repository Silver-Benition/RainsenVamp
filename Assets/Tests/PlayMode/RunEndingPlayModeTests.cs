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

        /// <summary>真实 MainLevel Boss 弹幕测试所需的活动世界、非活动世界和正式 Prefab 上下文。</summary>
        private sealed class BossEncounterFixture
        {
            public Component director;
            public Component coordinator;
            public Component boss;
            public Component activeSimulation;
            public Component inactiveSimulation;
            public GameObject projectilePrefab;
        }

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

        /// <summary>真实 Boss 第一阶段必须在 3 秒边界后发射恰好 8 枚正式弹体并保持正确属性与世界归属。</summary>
        [UnityTest]
        public IEnumerator MainLevel_Boss弹幕第一阶段_三秒边界与正式弹体属性()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            BossEncounterFixture fixture = PrepareBossEncounter();
            AssertNoActiveProjectiles(fixture, "第一阶段发射前");

            yield return new WaitForSeconds(2.75f);
            AssertNoActiveProjectiles(fixture, "第一阶段 3 秒边界前");

            yield return WaitUntil(
                () => GetActiveProjectiles(fixture.activeSimulation).Count >= 8,
                0.75f,
                "第一阶段跨过 3 秒边界后未观察到 8 枚 Boss 弹体。");

            List<Component> projectiles = GetActiveProjectiles(fixture.activeSimulation);
            AssertBossBarrageProjectiles(fixture, projectiles, 8, 10f, 4.5f, "第一阶段");
        }

        /// <summary>真实 Boss 第二阶段必须在生命降至 50% 后重置 2 秒边界并发射恰好 12 枚正式弹体。</summary>
        [UnityTest]
        public IEnumerator MainLevel_Boss弹幕第二阶段_两秒边界与正式弹体属性()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            BossEncounterFixture fixture = PrepareBossEncounter();
            AssertNoActiveProjectiles(fixture, "第二阶段切换前");

            object phaseDamage = RuntimeComponentTestUtility.Invoke(
                fixture.boss,
                "ApplyCombatDamage",
                400f,
                false);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(phaseDamage, "Accepted"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(fixture.boss, "CurrentHealth"),
                Is.EqualTo(400f).Within(FloatTolerance));

            // Boss 在下一次 Update 中依据真实生命值切入第二阶段并将弹幕计时器重置为 2 秒。
            yield return null;
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(fixture.boss, "IsPhaseTwoActive"),
                "Boss 降至 50% 后未进入第二阶段。");

            yield return new WaitForSeconds(1.75f);
            AssertNoActiveProjectiles(fixture, "第二阶段 2 秒边界前");

            yield return WaitUntil(
                () => GetActiveProjectiles(fixture.activeSimulation).Count >= 12,
                0.75f,
                "第二阶段跨过 2 秒边界后未观察到 12 枚 Boss 弹体。");

            List<Component> projectiles = GetActiveProjectiles(fixture.activeSimulation);
            AssertBossBarrageProjectiles(fixture, projectiles, 12, 12f, 5.5f, "第二阶段");
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
            Component levelUpManager = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("LevelUpManager")) as Component;
            Assert.IsNotNull(director);
            Assert.IsNotNull(coordinator);
            Assert.IsNotNull(levelUpManager);
            Assert.IsTrue((bool)RuntimeComponentTestUtility.Invoke(director, "DebugTriggerBossEncounter"));

            Component simulation = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                "ActiveWorldSimulation") as Component;
            Component boss = simulation.GetComponentInChildren(
                RuntimeComponentTestUtility.RequireRuntimeType("BossEnemyController"),
                true);
            Assert.IsNotNull(boss);

            // 通过正式调试授予入口触发 WeaponAdded，确保致命命中前已有明确的获得时间。
            // 这里不依赖首帧时间窗口；等待期间产生正的生效时长后，DPS 断言才有业务意义。
            ScriptableObject finisher = CreateWeaponData("weapon.test.boss_finisher", 1);
            Component ownedFinisher = RuntimeComponentTestUtility.Invoke(
                levelUpManager,
                "DebugEnsureWeaponLevel",
                finisher,
                1) as Component;
            Assert.IsNotNull(ownedFinisher, "Boss 致命武器未通过正式武器授予路径获得。");

            yield return new WaitForSeconds(0.05f);
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

        /// <summary>
        /// 从真实 MainLevel 建立 Boss 弹幕夹具。
        /// 仅禁用测试夹具内两套普通波次组件，保留 BossEncounter、Boss Prefab、EnemyProjectile Prefab 和双世界模拟器的生产引用。
        /// </summary>
        private static BossEncounterFixture PrepareBossEncounter()
        {
            Component director = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunDirector")) as Component;
            Component coordinator = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("WorldLineCoordinator")) as Component;
            Assert.IsNotNull(director, "MainLevel 缺少 RunDirector。");
            Assert.IsNotNull(coordinator, "MainLevel 缺少 WorldLineCoordinator。");
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(coordinator, "MainWorldIsActive"),
                "Boss 弹幕夹具预期从主世界活动状态开始。");

            DisableWaveManager(coordinator, "MainWorldWaveManager");
            DisableWaveManager(coordinator, "SubWorldWaveManager");

            Assert.IsTrue(
                (bool)RuntimeComponentTestUtility.Invoke(director, "DebugTriggerBossEncounter"),
                "Boss 遭遇未能从正式 Encounter 数据生成。");

            Component activeSimulation = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                "ActiveWorldSimulation") as Component;
            Assert.IsNotNull(activeSimulation, "当前活动世界缺少 WorldEnemySimulation。");

            Type simulationType = RuntimeComponentTestUtility.RequireRuntimeType("WorldEnemySimulation");
            Component[] simulations = coordinator.GetComponentsInChildren(simulationType, true);
            Assert.That(simulations.Length, Is.EqualTo(2), "MainLevel 必须保留主/副两套世界模拟器。");
            Component inactiveSimulation = null;
            for (int index = 0; index < simulations.Length; index++)
            {
                if (simulations[index] != activeSimulation)
                {
                    inactiveSimulation = simulations[index];
                    break;
                }
            }

            Assert.IsNotNull(inactiveSimulation, "未能找到非活动世界模拟器。");
            Component boss = activeSimulation.GetComponentInChildren(
                RuntimeComponentTestUtility.RequireRuntimeType("BossEnemyController"),
                true);
            Assert.IsNotNull(boss, "活动世界中找不到正式 Boss 控制器。");

            object bossData = RuntimeComponentTestUtility.GetProperty<object>(boss, "BossData");
            Assert.IsNotNull(bossData, "Boss 未绑定正式 BossDataSO。");
            Assert.That(
                RuntimeComponentTestUtility.GetFieldValue<string>(bossData, "bossID"),
                Is.EqualTo("boss_armed_colossus"));
            GameObject projectilePrefab = RuntimeComponentTestUtility.GetFieldValue<GameObject>(
                bossData,
                "projectilePrefab");
            Assert.IsNotNull(projectilePrefab, "正式 BossDataSO 缺少 EnemyProjectile Prefab。");
            Assert.That(projectilePrefab.name, Is.EqualTo("EnemyProjectile_1"));
            Assert.IsFalse(
                projectilePrefab.scene.IsValid(),
                "Boss 弹幕测试必须使用 Project 资产 Prefab，而不是场景临时对象。");

            return new BossEncounterFixture
            {
                director = director,
                coordinator = coordinator,
                boss = boss,
                activeSimulation = activeSimulation,
                inactiveSimulation = inactiveSimulation,
                projectilePrefab = projectilePrefab
            };
        }

        /// <summary>只在测试夹具中停止普通波次 Update，避免普通远程敌人复用同一 EnemyProjectile 池键污染 Boss 计数。</summary>
        private static void DisableWaveManager(Component coordinator, string propertyName)
        {
            Behaviour waveManager = RuntimeComponentTestUtility.GetProperty<object>(
                coordinator,
                propertyName) as Behaviour;
            Assert.IsNotNull(waveManager, $"MainLevel 缺少 {propertyName}。");
            waveManager.enabled = false;
        }

        /// <summary>读取指定世界模拟器下仍处于激活状态且直接归属于该模拟器的真实 EnemyProjectile。</summary>
        private static List<Component> GetActiveProjectiles(Component simulation)
        {
            Type projectileType = RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile");
            Component[] allProjectiles = simulation.GetComponentsInChildren(projectileType, true);
            var activeProjectiles = new List<Component>(allProjectiles.Length);
            for (int index = 0; index < allProjectiles.Length; index++)
            {
                Component projectile = allProjectiles[index];
                if (projectile != null &&
                    projectile.gameObject.activeInHierarchy &&
                    projectile.transform.parent == simulation.transform)
                {
                    activeProjectiles.Add(projectile);
                }
            }

            return activeProjectiles;
        }

        /// <summary>断言边界前主/副世界都没有活跃 Boss 弹体。</summary>
        private static void AssertNoActiveProjectiles(
            BossEncounterFixture fixture,
            string boundaryDescription)
        {
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(
                    fixture.activeSimulation,
                    "ActiveProjectileCount"),
                Is.Zero,
                $"{boundaryDescription}活动世界不应有弹体。");
            Assert.That(
                GetActiveProjectiles(fixture.activeSimulation).Count,
                Is.Zero,
                $"{boundaryDescription}活动世界不应有 Boss 弹体。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(
                    fixture.inactiveSimulation,
                    "ActiveProjectileCount"),
                Is.Zero,
                $"{boundaryDescription}非活动世界不应有弹体。");
            Assert.That(
                GetActiveProjectiles(fixture.inactiveSimulation).Count,
                Is.Zero,
                $"{boundaryDescription}非活动世界不应有 Boss 弹体。");
        }

        /// <summary>核对本轮所有活跃弹体的数量、伤害、速度、Prefab 池键、WorldSimulation 和父级归属。</summary>
        private static void AssertBossBarrageProjectiles(
            BossEncounterFixture fixture,
            List<Component> projectiles,
            int expectedCount,
            float expectedDamage,
            float expectedSpeed,
            string phaseDescription)
        {
            Assert.That(projectiles.Count, Is.EqualTo(expectedCount), $"{phaseDescription}活跃弹体数量不正确。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(
                    fixture.activeSimulation,
                    "ActiveProjectileCount"),
                Is.EqualTo(expectedCount),
                $"{phaseDescription}WorldEnemySimulation 计数与真实弹体数量不一致。");
            Assert.That(
                GetActiveProjectiles(fixture.inactiveSimulation).Count,
                Is.Zero,
                $"{phaseDescription}非活动世界不应拥有 Boss 弹体。");

            for (int index = 0; index < projectiles.Count; index++)
            {
                Component projectile = projectiles[index];
                Assert.AreSame(
                    fixture.projectilePrefab,
                    RuntimeComponentTestUtility.GetFieldValue<GameObject>(
                        projectile,
                        "_prefabReference"),
                    $"{phaseDescription}第 {index + 1} 枚弹体未使用正式 BossData projectilePrefab 池键。");
                Assert.AreSame(
                    fixture.activeSimulation,
                    RuntimeComponentTestUtility.GetFieldValue<object>(
                        projectile,
                        "_worldSimulation"),
                    $"{phaseDescription}第 {index + 1} 枚弹体的 WorldSimulation 归属错误。");
                Assert.AreSame(
                    fixture.activeSimulation.transform,
                    projectile.transform.parent,
                    $"{phaseDescription}第 {index + 1} 枚弹体未挂在活动世界实体根节点。");
                Assert.That(
                    RuntimeComponentTestUtility.GetProperty<float>(projectile, "ResolvedDamage"),
                    Is.EqualTo(expectedDamage).Within(FloatTolerance),
                    $"{phaseDescription}第 {index + 1} 枚弹体伤害错误。");

                Rigidbody2D rigidbody = projectile.GetComponent<Rigidbody2D>();
                Assert.IsNotNull(rigidbody, $"{phaseDescription}第 {index + 1} 枚弹体缺少 Rigidbody2D。");
                Assert.That(
                    rigidbody.velocity.magnitude,
                    Is.EqualTo(expectedSpeed).Within(FloatTolerance),
                    $"{phaseDescription}第 {index + 1} 枚弹体速度错误。");
            }
        }

        /// <summary>等待真实 Player Loop 条件成立或超时，避免弹幕边界测试无限等待。</summary>
        private static IEnumerator WaitUntil(
            Func<bool> condition,
            float timeoutSeconds,
            string timeoutMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(condition(), timeoutMessage);
        }
    }
}
