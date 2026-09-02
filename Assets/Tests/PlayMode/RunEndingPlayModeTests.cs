using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
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

        /// <summary>真实 MainLevel 结果页必须在六武器、六 Item、六 Ability 满载时保持五列表格绑定和区域不重叠。</summary>
        [UnityTest]
        public IEnumerator MainLevel_RunResultsUI_六武器与双六格满载_表格绑定和区域不重叠()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneName, LoadSceneMode.Single);
            yield return null;

            Component resultsUi = Object.FindObjectOfType(
                RuntimeComponentTestUtility.RequireRuntimeType("RunResultsUI")) as Component;
            Assert.IsNotNull(resultsUi, "MainLevel 缺少 RunResultsUI。");

            Texture2D iconTexture = TrackObject(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            Sprite testIcon = TrackObject(Sprite.Create(
                iconTexture,
                new Rect(0f, 0f, iconTexture.width, iconTexture.height),
                new Vector2(0.5f, 0.5f)));
            object snapshot = CreateRunResultsUiSnapshot(testIcon);
            RuntimeComponentTestUtility.Invoke(resultsUi, "Show", snapshot);
            Canvas.ForceUpdateCanvases();

            Transform panel = resultsUi.transform.Find("RunResultsOverlay/RunResultsPanel");
            Assert.IsNotNull(panel, "结果页缺少 RunResultsPanel。");
            AssertWeaponTableLayout(panel.Find("WeaponTable"), testIcon);
            AssertLoadoutLayout(panel.Find("Loadout"));
            AssertCanvasScaleContract(resultsUi, panel);
        }

        /// <summary>通过真实运行时类型构造六武器、六 Item 和六 Ability 的冻结结果快照。</summary>
        private object CreateRunResultsUiSnapshot(Sprite testIcon)
        {
            Type weaponType = RuntimeComponentTestUtility.RequireRuntimeType("RunResultWeaponSnapshot");
            Type weaponListType = typeof(List<>).MakeGenericType(weaponType);
            System.Collections.IList weapons = (System.Collections.IList)Activator.CreateInstance(weaponListType);
            for (int index = 0; index < 6; index++)
            {
                int rowNumber = index + 1;
                weapons.Add(Activator.CreateInstance(
                    weaponType,
                    new object[]
                    {
                        $"weapon.ui.{rowNumber}",
                        $"weapon.ui.{rowNumber}.name",
                        $"Weapon {rowNumber}",
                        testIcon,
                        rowNumber,
                        6,
                        120f + index * 10f,
                        rowNumber,
                        12f + index
                    }));
            }

            Type abilityType = RuntimeComponentTestUtility.RequireRuntimeType("RunResultAbilitySnapshot");
            Type abilityListType = typeof(List<>).MakeGenericType(abilityType);
            System.Collections.IList items = (System.Collections.IList)Activator.CreateInstance(abilityListType);
            System.Collections.IList abilities = (System.Collections.IList)Activator.CreateInstance(abilityListType);
            Type categoryType = RuntimeComponentTestUtility.RequireRuntimeType("AbilityPresentationCategory");
            object itemCategory = Enum.Parse(categoryType, "Item");
            object abilityCategory = Enum.Parse(categoryType, "Ability");
            for (int index = 0; index < 6; index++)
            {
                int cellNumber = index + 1;
                items.Add(Activator.CreateInstance(
                    abilityType,
                    new object[]
                    {
                        $"item.ui.{cellNumber}",
                        $"item.ui.{cellNumber}.name",
                        $"Item {cellNumber}",
                        testIcon,
                        cellNumber,
                        6,
                        itemCategory
                    }));
                abilities.Add(Activator.CreateInstance(
                    abilityType,
                    new object[]
                    {
                        $"ability.ui.{cellNumber}",
                        $"ability.ui.{cellNumber}.name",
                        $"Ability {cellNumber}",
                        testIcon,
                        cellNumber,
                        6,
                        abilityCategory
                    }));
            }

            Type characterType = RuntimeComponentTestUtility.RequireRuntimeType("RunResultCharacterSnapshot");
            object character = Activator.CreateInstance(
                characterType,
                new object[] { "character.ui", "character.ui.name", "UI Test Character", testIcon });

            Type pickupType = RuntimeComponentTestUtility.RequireRuntimeType("RunResultPickupSnapshot");
            Type pickupListType = typeof(List<>).MakeGenericType(pickupType);
            System.Collections.IList pickups = (System.Collections.IList)Activator.CreateInstance(pickupListType);

            Type snapshotType = RuntimeComponentTestUtility.RequireRuntimeType("RunResultSnapshot");
            object outcome = Enum.Parse(RuntimeComponentTestUtility.RequireRuntimeType("RunOutcome"), "Victory");
            return Activator.CreateInstance(
                snapshotType,
                new object[]
                {
                    outcome,
                    false,
                    "map.ui",
                    "UI Test Map",
                    125f,
                    123,
                    456,
                    6,
                    character,
                    weapons,
                    items,
                    abilities,
                    pickups
                });
        }

        /// <summary>核对武器表固定五列、表头首位、六行绑定、列宽一致和隐藏模板状态。</summary>
        private static void AssertWeaponTableLayout(Transform table, Sprite expectedIcon)
        {
            Assert.IsNotNull(table, "结果页缺少 WeaponTable。");
            Transform content = table.Find("Content");
            Assert.IsNotNull(content, "WeaponTable 缺少 Content。");
            Transform header = content.Find("WeaponTableHeader");
            Assert.IsNotNull(header, "WeaponTable 缺少固定表头。");
            Assert.IsTrue(header.gameObject.activeSelf, "武器表头必须可见。");
            Assert.IsNull(content.Find("Summary"), "武器表不应保留旧的占位 Summary 文本。");

            string[] columnNames = { "Weapon", "Level", "Damage", "Time", "Dps" };
            string[] headerTexts = { "武器", "等级", "伤害", "时间", "每秒伤害" };
            List<Transform> headerColumns = GetActiveDirectChildren(header);
            Assert.That(headerColumns.Count, Is.EqualTo(columnNames.Length), "表头必须正好包含五列。");
            for (int index = 0; index < columnNames.Length; index++)
            {
                Assert.That(headerColumns[index].name, Is.EqualTo(columnNames[index]));
                Transform text = headerColumns[index].Find("Text");
                Assert.IsNotNull(text, $"表头列 {columnNames[index]} 缺少独立文本节点。");
                Assert.That(GetUiText(text), Is.EqualTo(headerTexts[index]));
                Assert.That(
                    headerColumns[index].GetComponent<LayoutElement>().flexibleWidth,
                    Is.EqualTo(index == 0 ? 1f : 0f).Within(FloatTolerance),
                    $"表头 {columnNames[index]} 列的弹性配置不正确。");
            }
            AssertWeaponColumnsFillRow(header, headerColumns, "表头");
            Rect headerRect = GetWorldRect(header.GetComponent<RectTransform>());

            List<Transform> visibleRows = new List<Transform>();
            for (int index = 0; index < content.childCount; index++)
            {
                Transform child = content.GetChild(index);
                if (child.gameObject.activeSelf && child != header)
                {
                    visibleRows.Add(child);
                }
            }

            Assert.That(visibleRows.Count, Is.EqualTo(6), "六武器满载时必须创建六条可见武器行。");
            Transform hiddenTemplate = content.Find("WeaponRowTemplate");
            Assert.IsNotNull(hiddenTemplate, "武器表缺少动态行模板。");
            Assert.IsFalse(hiddenTemplate.gameObject.activeSelf, "武器行模板必须保持隐藏。");

            for (int rowIndex = 0; rowIndex < visibleRows.Count; rowIndex++)
            {
                Transform row = visibleRows[rowIndex];
                List<Transform> columns = GetActiveDirectChildren(row);
                Assert.That(columns.Count, Is.EqualTo(columnNames.Length), $"第 {rowIndex + 1} 行必须是五列独立结构。");
                Assert.IsNull(row.Find("Label"), "武器行不得回退到旧双列 Label。");
                Assert.IsNull(row.Find("Value"), "武器行不得回退到旧拼接 Value。");

                for (int columnIndex = 0; columnIndex < columnNames.Length; columnIndex++)
                {
                    Assert.That(columns[columnIndex].name, Is.EqualTo(columnNames[columnIndex]));
                    LayoutElement rowColumnLayout = columns[columnIndex].GetComponent<LayoutElement>();
                    Assert.That(
                        rowColumnLayout.flexibleWidth,
                        Is.EqualTo(columnIndex == 0 ? 1f : 0f).Within(FloatTolerance),
                        $"第 {rowIndex + 1} 行 {columnNames[columnIndex]} 列的弹性配置不正确。");
                    Rect headerColumnRect = GetWorldRect(headerColumns[columnIndex].GetComponent<RectTransform>());
                    Rect rowColumnRect = GetWorldRect(columns[columnIndex].GetComponent<RectTransform>());
                    Assert.That(
                        rowColumnRect.width,
                        Is.EqualTo(headerColumnRect.width).Within(0.5f),
                        $"第 {rowIndex + 1} 行 {columnNames[columnIndex]} 列宽必须与表头一致。");
                    Assert.That(
                        rowColumnRect.xMin,
                        Is.EqualTo(headerColumnRect.xMin).Within(0.5f),
                        $"第 {rowIndex + 1} 行 {columnNames[columnIndex]} 左边界必须与表头一致。");
                    Assert.That(
                        rowColumnRect.xMax,
                        Is.EqualTo(headerColumnRect.xMax).Within(0.5f),
                        $"第 {rowIndex + 1} 行 {columnNames[columnIndex]} 右边界必须与表头一致。");
                }
                Assert.That(
                    GetWorldRect(row.GetComponent<RectTransform>()).xMin,
                    Is.EqualTo(headerRect.xMin).Within(0.5f),
                    $"第 {rowIndex + 1} 行左边界必须与表头容器一致。");
                Assert.That(
                    GetWorldRect(row.GetComponent<RectTransform>()).xMax,
                    Is.EqualTo(headerRect.xMax).Within(0.5f),
                    $"第 {rowIndex + 1} 行右边界必须与表头容器一致。");
                AssertWeaponColumnsFillRow(row, columns, $"第 {rowIndex + 1} 行");

                Transform weaponColumn = row.Find("Weapon");
                Assert.IsNotNull(weaponColumn);
                List<Transform> weaponParts = GetActiveDirectChildren(weaponColumn);
                Assert.That(weaponParts.Count, Is.EqualTo(2), "武器列必须独立包含图标和显示名称。");
                Assert.That(weaponParts[0].name, Is.EqualTo("Icon"));
                Assert.That(weaponParts[1].name, Is.EqualTo("Text"));
                Image icon = weaponParts[0].GetComponent<Image>();
                Assert.IsNotNull(icon);
                Assert.IsTrue(icon.enabled, "绑定了武器图标时图标节点必须可见。");
                Assert.AreSame(expectedIcon, icon.sprite);
                Assert.That(GetUiText(weaponParts[1]), Is.EqualTo($"Weapon {rowIndex + 1}"));

                Assert.That(
                    GetUiText(row.Find("Level/Text")),
                    Is.EqualTo($"Lv.{rowIndex + 1}/6"));
                Assert.That(
                    GetUiText(row.Find("Damage/Text")),
                    Is.EqualTo($"{120f + rowIndex * 10f:F0}"));
                Assert.That(
                    GetUiText(row.Find("Time/Text")),
                    Is.EqualTo($"00:{rowIndex + 1:00}"));
                Assert.That(
                    GetUiText(row.Find("Dps/Text")),
                    Is.EqualTo($"{12f + rowIndex:F1}"));
            }
        }

        /// <summary>断言五列覆盖行内可用宽度，并验证最后 DPS 列贴近行容器右边界。</summary>
        private static void AssertWeaponColumnsFillRow(
            Transform row,
            List<Transform> columns,
            string description)
        {
            const float LayoutTolerance = 0.5f;
            Rect rowRect = GetWorldRect(row.GetComponent<RectTransform>());
            Rect firstColumnRect = GetWorldRect(columns[0].GetComponent<RectTransform>());
            Rect lastColumnRect = GetWorldRect(columns[columns.Count - 1].GetComponent<RectTransform>());
            float leadingInset = firstColumnRect.xMin - rowRect.xMin;
            float trailingInset = rowRect.xMax - lastColumnRect.xMax;

            Assert.That(leadingInset, Is.GreaterThanOrEqualTo(-LayoutTolerance), $"{description}首列不得越过行容器。");
            Assert.That(
                trailingInset,
                Is.EqualTo(leadingInset).Within(LayoutTolerance),
                $"{description}五列必须覆盖行内可用宽度。");
            Assert.That(
                lastColumnRect.xMax,
                Is.EqualTo(rowRect.xMax - leadingInset).Within(LayoutTolerance),
                $"{description}最后一列必须到达行内可用宽度的右边界。");

            for (int index = 1; index < columns.Count; index++)
            {
                Rect previousRect = GetWorldRect(columns[index - 1].GetComponent<RectTransform>());
                Rect currentRect = GetWorldRect(columns[index].GetComponent<RectTransform>());
                Assert.That(
                    currentRect.xMin,
                    Is.GreaterThanOrEqualTo(previousRect.xMax - LayoutTolerance),
                    $"{description}第 {index} 与第 {index + 1} 列不得重叠。");
            }

            Rect dpsRect = GetWorldRect(columns[4].GetComponent<RectTransform>());
            Assert.That(
                dpsRect.xMax,
                Is.EqualTo(rowRect.xMax - leadingInset).Within(LayoutTolerance),
                $"{description} DPS 列右边界必须贴近行容器右边界。");
        }

        /// <summary>核对角色、Item、Ability 五个纵向区域、六列单行网格和隐藏装备模板。</summary>
        private static void AssertLoadoutLayout(Transform loadout)
        {
            Assert.IsNotNull(loadout, "结果页缺少 Loadout。");
            Transform content = loadout.Find("Content");
            Assert.IsNotNull(content, "Loadout 缺少 Content。");

            Transform character = content.Find("Character");
            Transform itemTitle = content.Find("ItemTitle");
            Transform itemGrid = content.Find("ItemGrid");
            Transform abilityTitle = content.Find("AbilityTitle");
            Transform abilityGrid = content.Find("AbilityGrid");
            Transform[] regions = { character, itemTitle, itemGrid, abilityTitle, abilityGrid };
            for (int index = 0; index < regions.Length; index++)
            {
                Assert.IsNotNull(regions[index], $"Loadout 缺少第 {index + 1} 个结构区域。");
            }

            Assert.That(GetUiText(itemTitle), Is.EqualTo("Items  (6)"));
            Assert.That(GetUiText(abilityTitle), Is.EqualTo("Abilities  (6)"));
            for (int firstIndex = 0; firstIndex < regions.Length; firstIndex++)
            {
                Rect firstRect = GetWorldRect(regions[firstIndex].GetComponent<RectTransform>());
                for (int secondIndex = firstIndex + 1; secondIndex < regions.Length; secondIndex++)
                {
                    AssertNoIntersection(
                        firstRect,
                        GetWorldRect(regions[secondIndex].GetComponent<RectTransform>()),
                        $"Loadout 区域 {regions[firstIndex].name} 与 {regions[secondIndex].name} 不得重叠。");
                }
            }

            AssertGridLayout(itemGrid, "ItemGrid");
            AssertGridLayout(abilityGrid, "AbilityGrid");
            Transform hiddenCellTemplate = content.Find("LoadoutCellTemplate");
            Assert.IsNotNull(hiddenCellTemplate, "Loadout 缺少动态单元模板。");
            Assert.IsFalse(hiddenCellTemplate.gameObject.activeSelf, "装备网格模板必须保持隐藏。");
        }

        /// <summary>核对网格六列约束、六个可见单元均在容器内并且首行不换行。</summary>
        private static void AssertGridLayout(Transform gridTransform, string gridName)
        {
            GridLayoutGroup grid = gridTransform.GetComponent<GridLayoutGroup>();
            Assert.IsNotNull(grid, $"{gridName} 缺少 GridLayoutGroup。");
            Assert.That(grid.constraint, Is.EqualTo(GridLayoutGroup.Constraint.FixedColumnCount));
            Assert.That(grid.constraintCount, Is.EqualTo(6), $"{gridName} 必须使用六列约束。");

            List<Transform> cells = GetActiveDirectChildren(gridTransform);
            Assert.That(cells.Count, Is.EqualTo(6), $"{gridName} 必须包含六个可见单元。");
            Rect gridRect = GetWorldRect(gridTransform.GetComponent<RectTransform>());
            float firstCenterY = GetWorldRect(cells[0].GetComponent<RectTransform>()).center.y;
            for (int index = 0; index < cells.Count; index++)
            {
                Rect cellRect = GetWorldRect(cells[index].GetComponent<RectTransform>());
                AssertRectInside(gridRect, cellRect, $"{gridName} 第 {index + 1} 个单元必须完整位于网格内。");
                Assert.That(
                    cellRect.center.y,
                    Is.EqualTo(firstCenterY).Within(0.5f),
                    $"{gridName} 六个单元必须保持在同一行。");
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    AssertNoIntersection(
                        cellRect,
                        GetWorldRect(cells[previousIndex].GetComponent<RectTransform>()),
                        $"{gridName} 单元不得互相重叠。");
                }
            }
        }

        /// <summary>核对 CanvasScaler 的 1920/1080 基准和 1280/720 同比例缩放契约。</summary>
        private static void AssertCanvasScaleContract(Component resultsUi, Transform panel)
        {
            Canvas canvas = resultsUi.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas, "RunResultsUI 必须位于 Canvas 下。");
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "结果页 Canvas 缺少 CanvasScaler。");
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution.x, Is.EqualTo(1920f).Within(FloatTolerance));
            Assert.That(scaler.referenceResolution.y, Is.EqualTo(1080f).Within(FloatTolerance));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(1f).Within(FloatTolerance));

            float referenceScale = CalculateCanvasScale(new Vector2(1920f, 1080f), scaler);
            float compactScale = CalculateCanvasScale(new Vector2(1280f, 720f), scaler);
            Assert.That(referenceScale, Is.EqualTo(1f).Within(FloatTolerance));
            Assert.That(compactScale, Is.EqualTo(2f / 3f).Within(FloatTolerance));
            Assert.That(1920f / 1080f, Is.EqualTo(1280f / 720f).Within(FloatTolerance));

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            AssertRectInside(
                GetWorldRect(canvasRect),
                GetWorldRect(panelRect),
                "结果页面板必须位于 Canvas 内，统一 16:9 缩放时该关系保持不变。");
        }

        /// <summary>按 Unity CanvasScaler MatchWidthOrHeight 公式计算指定分辨率下的结构缩放比例。</summary>
        private static float CalculateCanvasScale(Vector2 resolution, CanvasScaler scaler)
        {
            float logWidth = Mathf.Log(resolution.x / scaler.referenceResolution.x, 2f);
            float logHeight = Mathf.Log(resolution.y / scaler.referenceResolution.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
        }

        /// <summary>读取父节点下当前可见的直接子节点，排除隐藏模板但保留视觉顺序。</summary>
        private static List<Transform> GetActiveDirectChildren(Transform parent)
        {
            List<Transform> children = new List<Transform>();
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.gameObject.activeSelf)
                {
                    children.Add(child);
                }
            }

            return children;
        }

        /// <summary>把 RectTransform 世界角点转换为轴对齐矩形，供结构化重叠断言使用。</summary>
        private static Rect GetWorldRect(RectTransform target)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float maxY = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>断言两个 UI 矩形没有面积重叠，允许极小布局浮点误差。</summary>
        private static void AssertNoIntersection(Rect first, Rect second, string message)
        {
            const float LayoutTolerance = 0.5f;
            bool separated = first.xMax <= second.xMin + LayoutTolerance ||
                second.xMax <= first.xMin + LayoutTolerance ||
                first.yMax <= second.yMin + LayoutTolerance ||
                second.yMax <= first.yMin + LayoutTolerance;
            Assert.IsTrue(separated, message);
        }

        /// <summary>断言子矩形完整位于父矩形内，允许 Canvas 布局带来的小量舍入误差。</summary>
        private static void AssertRectInside(Rect container, Rect child, string message)
        {
            const float LayoutTolerance = 0.5f;
            Assert.That(child.xMin, Is.GreaterThanOrEqualTo(container.xMin - LayoutTolerance), message);
            Assert.That(child.xMax, Is.LessThanOrEqualTo(container.xMax + LayoutTolerance), message);
            Assert.That(child.yMin, Is.GreaterThanOrEqualTo(container.yMin - LayoutTolerance), message);
            Assert.That(child.yMax, Is.LessThanOrEqualTo(container.yMax + LayoutTolerance), message);
        }

        /// <summary>不增加测试程序集对 TMP 的编译期依赖，按真实组件属性读取结果页文本。</summary>
        private static string GetUiText(Transform textTransform)
        {
            Assert.IsNotNull(textTransform, "UI 文本节点不能为空。");
            Component[] components = textTransform.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null || component.GetType().FullName != "TMPro.TextMeshProUGUI")
                {
                    continue;
                }

                System.Reflection.PropertyInfo textProperty = component.GetType().GetProperty(
                    "text",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                Assert.IsNotNull(textProperty, "TextMeshProUGUI 缺少公开 text 属性。");
                return (string)textProperty.GetValue(component, null);
            }

            Assert.Fail($"节点 {textTransform.name} 缺少 TextMeshProUGUI。");
            return string.Empty;
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
