using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>
    /// 通过真实 Player Loop、Physics2D 和共享对象池验证远程敌人 MVP。
    /// </summary>
    public sealed class RangedEnemyPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;
        private const float HealthTimeoutSeconds = 1.5f;

        /// <summary>从生成时就在射程内按首发延迟和冷却发射，并验证回池复用会重新计时。</summary>
        [UnityTest]
        public IEnumerator RangedEnemyController_首发冷却与回池复用_按实际发射数量计时()
        {
            GameObject player = CreatePlayer(new Vector3(6f, 0f, 0f), out Component playerHealth);
            player.GetComponent<Collider2D>().enabled = false;
            Component poolManager = CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            GameObject enemyTemplate = CreateRangedEnemyTemplate(projectileTemplate);
            Component simulation = CreateWorldSimulation(player, enemyTemplate);
            Component worldOwner = CreateWorldWaveOwner("PlayModeTest_RangedWorldOwner");
            RuntimeComponentTestUtility.Invoke(simulation, "SetWorldActive", true);

            object snapshot = CreateEnemySnapshot(30f, 0f, 5f, 1f, false);
            ExpectRuntimePrefabWarning();
            float firstSpawnTime = Time.realtimeSinceStartup;
            GameObject firstEnemy = SpawnEnemy(
                simulation,
                enemyTemplate,
                worldOwner,
                Vector3.zero,
                snapshot);
            TrackObject(firstEnemy);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveEnemyCount"),
                Is.EqualTo(1));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.Zero,
                "敌人生成时已在射程内，但首发延迟未结束前不应有弹体。");

            yield return new WaitForSeconds(0.6f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.Zero,
                "首发延迟 0.8 秒未到时不应发射。");

            ExpectRuntimePrefabWarning();
            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount") >= 1,
                0.6f);
            float firstShotElapsed = Time.realtimeSinceStartup - firstSpawnTime;
            float firstShotTime = Time.realtimeSinceStartup;
            Assert.That(firstShotElapsed, Is.GreaterThanOrEqualTo(0.75f));
            Assert.That(firstShotElapsed, Is.LessThan(1.2f));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(1));

            yield return new WaitForSeconds(0.9f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(1),
                "成功发射后的 2 秒冷却未结束前不应出现第二发。");

            ExpectRuntimePrefabWarning();
            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount") >= 2,
                1.4f);
            float secondShotElapsed = Time.realtimeSinceStartup - firstShotTime;
            Assert.That(secondShotElapsed, Is.GreaterThanOrEqualTo(1.8f));
            Assert.That(secondShotElapsed, Is.LessThan(2.5f));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(2));

            ReleaseActiveProjectiles(poolManager, projectileTemplate);
            RuntimeComponentTestUtility.Invoke(poolManager, "Release", enemyTemplate, firstEnemy);
            yield return null;
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.Zero);

            ExpectRuntimePrefabWarning();
            float secondSpawnTime = Time.realtimeSinceStartup;
            GameObject secondEnemy = SpawnEnemy(
                simulation,
                enemyTemplate,
                worldOwner,
                Vector3.zero,
                snapshot);
            TrackObject(secondEnemy);
            Assert.AreSame(firstEnemy, secondEnemy, "回池后应复用同一个敌人实例。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.Zero,
                "敌人重新取出时首发计时必须重新从 0.8 秒开始。");

            yield return new WaitForSeconds(0.6f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.Zero,
                "回池复用后的首发延迟未结束时不应发射。");

            ExpectRuntimePrefabWarning();
            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount") >= 1,
                0.6f);
            float reusedFirstShotElapsed = Time.realtimeSinceStartup - secondSpawnTime;
            float reusedFirstShotTime = Time.realtimeSinceStartup;
            Assert.That(reusedFirstShotElapsed, Is.GreaterThanOrEqualTo(0.75f));
            Assert.That(reusedFirstShotElapsed, Is.LessThan(1.2f));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(1));

            yield return new WaitForSeconds(0.9f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(1));

            ExpectRuntimePrefabWarning();
            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount") >= 2,
                1.4f);
            float reusedSecondShotElapsed = Time.realtimeSinceStartup - reusedFirstShotTime;
            Assert.That(reusedSecondShotElapsed, Is.GreaterThanOrEqualTo(1.8f));
            Assert.That(reusedSecondShotElapsed, Is.LessThan(2.5f));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulation, "ActiveProjectileCount"),
                Is.EqualTo(2));

            ReleaseActiveProjectiles(poolManager, projectileTemplate);
            Assert.IsNotNull(playerHealth);
        }

        /// <summary>武装来源发射的弹体应命中玩家，造成十二点伤害并回到共享对象池。</summary>
        [UnityTest]
        public IEnumerator EnemyProjectile_武装来源_命中玩家并回池()
        {
            GameObject player = CreatePlayer(Vector3.zero, out Component playerHealth);
            CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            Component simulation = CreateWorldSimulation(player, projectileTemplate);
            Component sourceEnemy = CreateSourceEnemy(player, false);
            RuntimeComponentTestUtility.Invoke(simulation, "SetWorldActive", true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
            GameObject projectile = (GameObject)RuntimeComponentTestUtility.Invoke(
                simulation,
                "SpawnProjectile",
                projectileTemplate,
                new Vector3(-2f, 0f, 0f),
                Quaternion.identity,
                Vector2.right * 5.5f,
                12f,
                sourceEnemy,
                6f);
            Component projectileComponent = projectile.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile"));

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(projectileComponent, "ResolvedDamage"),
                Is.EqualTo(12f).Within(FloatTolerance));
            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth") < 100f,
                HealthTimeoutSeconds);

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(88f).Within(FloatTolerance));
            Assert.IsFalse(projectile.activeSelf);
        }

        /// <summary>玩家的辅助 Trigger 不应扩大远程弹体的实际受击范围。</summary>
        [UnityTest]
        public IEnumerator EnemyProjectile_玩家辅助Trigger_不会扩大受击范围()
        {
            GameObject player = CreatePlayer(Vector3.zero, out Component playerHealth);
            GameObject magnetRadius = CreateTrackedGameObject(
                "PlayModeTest_PlayerMagnetRadius",
                false);
            magnetRadius.transform.SetParent(player.transform, false);
            magnetRadius.layer = RequireLayer("Player");
            CircleCollider2D magnetCollider = magnetRadius.AddComponent<CircleCollider2D>();
            magnetCollider.isTrigger = true;
            magnetCollider.radius = 3f;
            magnetRadius.SetActive(true);

            CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            Component simulation = CreateWorldSimulation(player, projectileTemplate);
            RuntimeComponentTestUtility.Invoke(simulation, "SetWorldActive", true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
            GameObject projectile = SpawnProjectile(
                simulation,
                projectileTemplate,
                null,
                new Vector3(-4f, 0f, 0f));

            yield return new WaitForSeconds(0.25f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance),
                "弹体进入 Player 辅助 Trigger 后仍不应造成伤害。");

            yield return WaitUntil(
                () => RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth") < 100f,
                HealthTimeoutSeconds);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(88f).Within(FloatTolerance));
            Assert.IsFalse(projectile.activeSelf);
        }

        /// <summary>Defang 来源的弹体必须零伤害，命中回池后再次取出仍不能继承旧伤害。</summary>
        [UnityTest]
        public IEnumerator EnemyProjectile_Defang来源与池复用_伤害始终为零()
        {
            GameObject player = CreatePlayer(Vector3.zero, out Component playerHealth);
            CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            Component simulation = CreateWorldSimulation(player, projectileTemplate);
            Component sourceEnemy = CreateSourceEnemy(player, true);
            RuntimeComponentTestUtility.Invoke(simulation, "SetWorldActive", true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
            GameObject firstProjectile = SpawnProjectile(
                simulation,
                projectileTemplate,
                sourceEnemy,
                new Vector3(-2f, 0f, 0f));
            Component projectileType = firstProjectile.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(projectileType, "ResolvedDamage"),
                Is.Zero);

            yield return WaitUntil(
                () => !firstProjectile.activeSelf,
                HealthTimeoutSeconds);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance));

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
            GameObject secondProjectile = SpawnProjectile(
                simulation,
                projectileTemplate,
                sourceEnemy,
                new Vector3(-2f, 0f, 0f));
            Component secondProjectileComponent = secondProjectile.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile"));
            Assert.AreSame(firstProjectile, secondProjectile);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(secondProjectileComponent, "ResolvedDamage"),
                Is.Zero);
            yield return new WaitForFixedUpdate();

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance));
        }

        /// <summary>Default 层掩体必须在玩家之前接收弹体并阻止伤害。</summary>
        [UnityTest]
        public IEnumerator EnemyProjectile_Default掩体_先于玩家阻挡弹体()
        {
            GameObject player = CreatePlayer(new Vector3(3f, 0f, 0f), out Component playerHealth);
            CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            Component simulation = CreateWorldSimulation(player, projectileTemplate);
            RuntimeComponentTestUtility.Invoke(simulation, "SetWorldActive", true);

            GameObject cover = CreateTrackedGameObject("PlayModeTest_DefaultCover", false);
            cover.layer = RequireLayer("Default");
            BoxCollider2D coverCollider = cover.AddComponent<BoxCollider2D>();
            coverCollider.size = Vector2.one;
            cover.transform.position = new Vector3(1f, 0f, 0f);
            cover.SetActive(true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
            GameObject projectile = (GameObject)RuntimeComponentTestUtility.Invoke(
                simulation,
                "SpawnProjectile",
                projectileTemplate,
                new Vector3(-1f, 0f, 0f),
                Quaternion.identity,
                Vector2.right * 5.5f,
                12f,
                null,
                6f);

            yield return WaitUntil(
                () => !projectile.activeSelf,
                HealthTimeoutSeconds);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance));
        }

        /// <summary>真实生成的非当前世界敌人和弹体继续模拟但不交互，且跨世界复用不被旧世界夺回。</summary>
        [UnityTest]
        public IEnumerator WorldEnemySimulation_敌人跨世界复用与弹体寿命_归属隔离()
        {
            GameObject player = CreatePlayer(Vector3.zero, out Component playerHealth);
            Component poolManager = CreatePoolManager();
            GameObject projectileTemplate = CreateProjectileTemplate();
            GameObject enemyTemplate = CreateRangedEnemyTemplate(projectileTemplate);
            Component simulationA = CreateWorldSimulation(player, enemyTemplate);
            Component simulationB = CreateWorldSimulation(player, enemyTemplate);
            Component worldOwnerA = CreateWorldWaveOwner("PlayModeTest_RangedWorldOwnerA");
            Component worldOwnerB = CreateWorldWaveOwner("PlayModeTest_RangedWorldOwnerB");
            RuntimeComponentTestUtility.Invoke(simulationA, "SetWorldActive", false);
            RuntimeComponentTestUtility.Invoke(simulationB, "SetWorldActive", true);

            object snapshot = CreateEnemySnapshot(30f, 1.6f, 5f, 1f, false);
            ExpectRuntimePrefabWarning();
            GameObject firstEnemy = SpawnEnemy(
                simulationA,
                enemyTemplate,
                worldOwnerA,
                new Vector3(-1f, 0f, 0f),
                snapshot);
            TrackObject(firstEnemy);
            Type enemyBaseType = RuntimeComponentTestUtility.RequireRuntimeType("EnemyBase");
            Component firstEnemyBase = firstEnemy.GetComponent(enemyBaseType);
            SpriteRenderer firstEnemyRenderer = firstEnemy.GetComponent<SpriteRenderer>();
            Collider2D firstEnemyCollider = firstEnemy.GetComponent<Collider2D>();
            float firstEnemyPosition = firstEnemy.transform.position.x;
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveEnemyCount"),
                Is.EqualTo(1));
            Assert.IsFalse(firstEnemyRenderer.enabled);
            Assert.IsFalse(firstEnemyCollider.enabled);
            Assert.IsFalse(
                RuntimeComponentTestUtility.GetProperty<bool>(
                    firstEnemyBase,
                    "IsWorldInteractionEnabled"));

            yield return new WaitForSeconds(0.2f);
            Assert.That(
                firstEnemy.transform.position.x,
                Is.LessThan(firstEnemyPosition),
                "非当前世界敌人仍应按玩家距离推进后退模拟。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance),
                "非当前世界敌人不得对玩家造成接触伤害。");

            ExpectRuntimePrefabWarning();
            GameObject firstProjectile = SpawnProjectile(
                simulationA,
                projectileTemplate,
                null,
                new Vector3(-2f, 0f, 0f));
            TrackObject(firstProjectile);
            SpriteRenderer firstRenderer = firstProjectile.GetComponent<SpriteRenderer>();
            Collider2D firstCollider = firstProjectile.GetComponent<Collider2D>();
            float firstPosition = firstProjectile.transform.position.x;
            Component firstProjectileComponent = firstProjectile.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile"));
            float firstLifetime = RuntimeComponentTestUtility.GetProperty<float>(
                firstProjectileComponent,
                "RemainingLifetime");
            Assert.IsFalse(firstRenderer.enabled);
            Assert.IsFalse(firstCollider.enabled);

            yield return new WaitForSeconds(0.2f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(100f).Within(FloatTolerance));
            Assert.That(firstProjectile.transform.position.x, Is.GreaterThan(firstPosition));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(
                    firstProjectileComponent,
                    "RemainingLifetime"),
                Is.LessThan(firstLifetime));

            RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Release",
                projectileTemplate,
                firstProjectile);
            RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Release",
                enemyTemplate,
                firstEnemy);
            yield return null;
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveEnemyCount"),
                Is.Zero);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveProjectileCount"),
                Is.Zero);

            ExpectRuntimePrefabWarning();
            GameObject secondEnemy = SpawnEnemy(
                simulationB,
                enemyTemplate,
                worldOwnerB,
                new Vector3(-10f, 0f, 0f),
                snapshot);
            TrackObject(secondEnemy);
            SpriteRenderer secondEnemyRenderer = secondEnemy.GetComponent<SpriteRenderer>();
            Collider2D secondEnemyCollider = secondEnemy.GetComponent<Collider2D>();
            Component secondEnemyBase = secondEnemy.GetComponent(enemyBaseType);
            Assert.AreSame(firstEnemy, secondEnemy);
            Assert.IsTrue(secondEnemyRenderer.enabled);
            Assert.IsTrue(secondEnemyCollider.enabled);
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(
                    secondEnemyBase,
                    "IsWorldInteractionEnabled"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveEnemyCount"),
                Is.Zero);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationB, "ActiveEnemyCount"),
                Is.EqualTo(1));

            ExpectRuntimePrefabWarning();
            GameObject secondProjectile = SpawnProjectile(
                simulationB,
                projectileTemplate,
                null,
                new Vector3(-10f, 0f, 0f));
            TrackObject(secondProjectile);
            Assert.AreSame(firstProjectile, secondProjectile);
            Assert.IsTrue(secondProjectile.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(secondProjectile.GetComponent<Collider2D>().enabled);

            RuntimeComponentTestUtility.Invoke(simulationA, "SetWorldActive", true);
            Assert.IsTrue(secondEnemyRenderer.enabled);
            Assert.IsTrue(secondEnemyCollider.enabled);
            Assert.IsTrue(secondProjectile.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(secondProjectile.GetComponent<Collider2D>().enabled);
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(
                    secondEnemyBase,
                    "IsWorldInteractionEnabled"));

            RuntimeComponentTestUtility.Invoke(simulationA, "SetWorldActive", false);
            Assert.IsTrue(secondEnemyRenderer.enabled);
            Assert.IsTrue(secondEnemyCollider.enabled);
            Assert.IsTrue(secondProjectile.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(secondProjectile.GetComponent<Collider2D>().enabled);
            Assert.IsTrue(
                RuntimeComponentTestUtility.GetProperty<bool>(
                    secondEnemyBase,
                    "IsWorldInteractionEnabled"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveEnemyCount"),
                Is.Zero);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationB, "ActiveEnemyCount"),
                Is.EqualTo(1));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationA, "ActiveProjectileCount"),
                Is.Zero);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(simulationB, "ActiveProjectileCount"),
                Is.EqualTo(1));
        }

        /// <summary>创建无外部场景依赖的玩家生命夹具。</summary>
        private GameObject CreatePlayer(Vector3 position, out Component playerHealth)
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_RangedPlayer", false);
            player.tag = "Player";
            player.layer = RequireLayer("Player");
            player.transform.position = position;

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            player.AddComponent<BoxCollider2D>().size = Vector2.one;

            playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "maxHealth", 100f);
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0f);
            player.SetActive(true);
            return player;
        }

        /// <summary>创建共享对象池并返回生产 PoolManager 组件。</summary>
        private Component CreatePoolManager()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_RangedPool");
            return RuntimeComponentTestUtility.AddRuntimeComponent(managerObject, "PoolManager");
        }

        /// <summary>创建可供 PoolManager 复用的场景弹体模板。</summary>
        private GameObject CreateProjectileTemplate()
        {
            GameObject projectile = CreateTrackedGameObject(
                "PlayModeTest_EnemyProjectileTemplate",
                false);
            projectile.layer = RequireLayer("EnemyProjectile");
            Rigidbody2D body = projectile.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            projectile.AddComponent<SpriteRenderer>();
            CircleCollider2D collider = projectile.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.12f;
            RuntimeComponentTestUtility.AddRuntimeComponent(projectile, "EnemyProjectile");
            return projectile;
        }

        /// <summary>创建真实 WorldWaveManager 所需的测试拥有者，供 SpawnEnemy 绑定池实例归属。</summary>
        private Component CreateWorldWaveOwner(string name)
        {
            GameObject ownerObject = CreateTrackedGameObject(name, false);
            Component owner = RuntimeComponentTestUtility.AddRuntimeComponent(
                ownerObject,
                "WorldWaveManager");
            ownerObject.SetActive(true);
            return owner;
        }

        /// <summary>创建带敌人基础能力和远程攻击配置的池化敌人模板。</summary>
        private GameObject CreateRangedEnemyTemplate(GameObject projectileTemplate)
        {
            ScriptableObject enemyData = CreateEnemyData(30f, 1.6f, 5f);
            ScriptableObject attackData = CreateAttackData(projectileTemplate);
            GameObject enemy = CreateTrackedGameObject(
                "PlayModeTest_RangedEnemyTemplate",
                false);
            enemy.layer = RequireLayer("Enemy");
            Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            BoxCollider2D collider = enemy.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.7f, 0.9f);
            enemy.AddComponent<SpriteRenderer>();
            Component controller = RuntimeComponentTestUtility.AddRuntimeComponent(
                enemy,
                "RangedEnemyController");
            RuntimeComponentTestUtility.SetField(controller, "enemyData", enemyData);
            RuntimeComponentTestUtility.SetField(controller, "attackData", attackData);
            return enemy;
        }

        /// <summary>创建带有效世界线配置的世界敌人模拟器，避免测试被无关校验日志干扰。</summary>
        private Component CreateWorldSimulation(GameObject player, GameObject rulePrefab)
        {
            ScriptableObject worldLine = CreateRuntimeData("WorldLineDataSO");
            ScriptableObject mapTheme = CreateRuntimeData("MapThemeDataSO");
            ScriptableObject waveConfig = CreateRuntimeData("WaveConfigSO");
            Texture2D texture = TrackObject(new Texture2D(1, 1));
            Sprite sprite = TrackObject(Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f));

            RuntimeComponentTestUtility.SetField(mapTheme, "groundSprites", new[] { sprite });
            Type waveType = RuntimeComponentTestUtility.RequireRuntimeType("WaveConfigSO");
            Type ruleType = waveType.GetNestedType("SpawnRule", BindingFlags.Public);
            object rule = Activator.CreateInstance(ruleType);
            ruleType.GetField("enemyPrefab").SetValue(rule, rulePrefab);
            IList rules = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ruleType));
            rules.Add(rule);
            RuntimeComponentTestUtility.SetField(waveConfig, "rules", rules);
            RuntimeComponentTestUtility.SetField(worldLine, "groundTheme", mapTheme);
            RuntimeComponentTestUtility.SetField(worldLine, "coverSprite", sprite);
            RuntimeComponentTestUtility.SetField(worldLine, "waveConfig", waveConfig);

            GameObject simulationObject = CreateTrackedGameObject(
                "PlayModeTest_WorldEnemySimulation",
                false);
            Component simulation = RuntimeComponentTestUtility.AddRuntimeComponent(
                simulationObject,
                "WorldEnemySimulation");
            RuntimeComponentTestUtility.SetField(simulation, "worldLine", worldLine);
            simulationObject.SetActive(true);
            Assert.IsTrue(player.activeInHierarchy);
            return simulation;
        }

        /// <summary>创建并配置远程敌人的静态数据。</summary>
        private ScriptableObject CreateEnemyData(float health, float speed, float collisionDamage)
        {
            ScriptableObject data = CreateRuntimeData("EnemyDataSO");
            RuntimeComponentTestUtility.SetField(data, "maxHealth", health);
            RuntimeComponentTestUtility.SetField(data, "moveSpeed", speed);
            RuntimeComponentTestUtility.SetField(data, "collisionDamage", collisionDamage);
            RuntimeComponentTestUtility.SetField(data, "canBeDefanged", true);
            return data;
        }

        /// <summary>创建指定来源快照的敌人夹具，并让其远离弹道。</summary>
        private Component CreateSourceEnemy(GameObject player, bool defanged)
        {
            ScriptableObject data = CreateEnemyData(30f, 0f, 5f);
            GameObject enemyObject = CreateTrackedGameObject(
                "PlayModeTest_RangedSourceEnemy",
                false);
            enemyObject.layer = RequireLayer("Enemy");
            enemyObject.transform.position = new Vector3(-5f, 3f, 0f);
            Rigidbody2D body = enemyObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            enemyObject.AddComponent<BoxCollider2D>();
            Component enemy = RuntimeComponentTestUtility.AddRuntimeComponent(enemyObject, "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemy, "enemyData", data);
            enemyObject.SetActive(true);
            RuntimeComponentTestUtility.Invoke(
                enemy,
                "ApplySpawnSnapshot",
                CreateEnemySnapshot(30f, 0f, defanged ? 0f : 5f, defanged ? 0f : 1f, defanged));
            Assert.IsNotNull(player);
            return enemy;
        }

        /// <summary>创建远程攻击数据并绑定测试弹体模板。</summary>
        private ScriptableObject CreateAttackData(GameObject projectileTemplate)
        {
            ScriptableObject attackData = CreateRuntimeData("RangedEnemyAttackDataSO");
            RuntimeComponentTestUtility.SetField(attackData, "maxRange", 8f);
            RuntimeComponentTestUtility.SetField(attackData, "firstShotDelay", 0.8f);
            RuntimeComponentTestUtility.SetField(attackData, "cooldown", 2f);
            RuntimeComponentTestUtility.SetField(attackData, "baseDamage", 12f);
            RuntimeComponentTestUtility.SetField(attackData, "projectileSpeed", 5.5f);
            RuntimeComponentTestUtility.SetField(attackData, "projectileLifetime", 6f);
            RuntimeComponentTestUtility.SetField(attackData, "projectilePrefab", projectileTemplate);
            return attackData;
        }

        /// <summary>通过真实 WorldEnemySimulation 生成一个带快照的池化敌人。</summary>
        private static GameObject SpawnEnemy(
            Component simulation,
            GameObject enemyTemplate,
            Component worldOwner,
            Vector3 position,
            object snapshot)
        {
            return (GameObject)RuntimeComponentTestUtility.Invoke(
                simulation,
                "SpawnEnemy",
                enemyTemplate,
                position,
                worldOwner,
                0,
                snapshot);
        }

        /// <summary>创建并登记运行时 ScriptableObject。</summary>
        private ScriptableObject CreateRuntimeData(string typeName)
        {
            return TrackObject(RuntimeComponentTestUtility.CreateRuntimeScriptableObject(typeName));
        }

        /// <summary>按生产 EnemySpawnSnapshot 构造确定性测试快照。</summary>
        private static object CreateEnemySnapshot(
            float health,
            float speed,
            float collisionDamage,
            float outgoingMultiplier,
            bool defanged)
        {
            Type snapshotType = RuntimeComponentTestUtility.RequireRuntimeType("EnemySpawnSnapshot");
            return Activator.CreateInstance(
                snapshotType,
                health,
                speed,
                collisionDamage,
                outgoingMultiplier,
                defanged);
        }

        /// <summary>通过世界模拟器生成一个向玩家飞行的测试弹体。</summary>
        private static GameObject SpawnProjectile(
            Component simulation,
            GameObject projectileTemplate,
            Component sourceEnemy,
            Vector3 position)
        {
            return (GameObject)RuntimeComponentTestUtility.Invoke(
                simulation,
                "SpawnProjectile",
                projectileTemplate,
                position,
                Quaternion.identity,
                Vector2.right * 5.5f,
                12f,
                sourceEnemy,
                6f);
        }

        /// <summary>释放当前测试中仍激活的弹体，并登记池实例以便测试结束时清理。</summary>
        private void ReleaseActiveProjectiles(Component poolManager, GameObject projectileTemplate)
        {
            Type projectileType = RuntimeComponentTestUtility.RequireRuntimeType("EnemyProjectile");
            UnityEngine.Object[] projectiles = UnityEngine.Object.FindObjectsOfType(
                projectileType,
                true);
            for (int index = 0; index < projectiles.Length; index++)
            {
                Component projectile = projectiles[index] as Component;
                if (projectile == null ||
                    !projectile.gameObject.activeSelf ||
                    projectile.gameObject == projectileTemplate)
                {
                    continue;
                }

                TrackObject(projectile.gameObject);
                RuntimeComponentTestUtility.Invoke(
                    poolManager,
                    "Release",
                    projectileTemplate,
                    projectile.gameObject);
            }
        }

        /// <summary>声明场景模板进入对象池时预期产生的编辑器警告。</summary>
        private static void ExpectRuntimePrefabWarning()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"PoolManager\.Spawn 收到的对象不是 Prefab 资产"));
        }

        /// <summary>等待条件成立或超时，避免物理回调测试无限等待。</summary>
        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(condition(), "PlayMode 物理条件在超时前未成立。");
        }
    }
}
