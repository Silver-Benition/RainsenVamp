using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>
    /// 验证远程敌人的数据契约、距离移动意图、正式资源引用和物理层配置。
    /// </summary>
    public sealed class RangedEnemyTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>负值攻击参数读取时必须统一钳制为非负值。</summary>
        [Test]
        public void RangedEnemyAttackData_负值参数_读取时钳制为零()
        {
            RangedEnemyAttackDataSO data = ScriptableObject.CreateInstance<RangedEnemyAttackDataSO>();
            try
            {
                TestObjectUtility.SetPrivateFloat(data, "maxRange", -1f);
                TestObjectUtility.SetPrivateFloat(data, "firstShotDelay", -2f);
                TestObjectUtility.SetPrivateFloat(data, "cooldown", -3f);
                TestObjectUtility.SetPrivateFloat(data, "baseDamage", -4f);
                TestObjectUtility.SetPrivateFloat(data, "projectileSpeed", -5f);
                TestObjectUtility.SetPrivateFloat(data, "projectileLifetime", -6f);

                Assert.That(data.MaxRange, Is.Zero);
                Assert.That(data.FirstShotDelay, Is.Zero);
                Assert.That(data.Cooldown, Is.Zero);
                Assert.That(data.BaseDamage, Is.Zero);
                Assert.That(data.ProjectileSpeed, Is.Zero);
                Assert.That(data.ProjectileLifetime, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>远程敌人在玩家距离小于四、四到六和大于六时分别后退、保持和接近。</summary>
        [Test]
        public void RangedEnemyController_玩家距离分带_后退保持接近()
        {
            GameObject player = CreateTrackedGameObject("EditModeTest_RangedPlayer");
            player.tag = "Player";

            GameObject enemyObject = CreateTrackedGameObject("EditModeTest_RangedEnemy");
            enemyObject.SetActive(false);
            enemyObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            enemyObject.AddComponent<BoxCollider2D>();
            RangedEnemyController controller = enemyObject.AddComponent<RangedEnemyController>();
            enemyObject.SetActive(true);
            TestObjectUtility.InvokeNonPublicMethod(controller, "Awake");

            MethodInfo movementMethod = typeof(RangedEnemyController).GetMethod(
                "GetMovementDirection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(movementMethod);

            player.transform.position = new Vector3(2f, 0f, 0f);
            Vector2 retreat = (Vector2)movementMethod.Invoke(controller, null);
            Assert.That(retreat.x, Is.EqualTo(-1f).Within(FloatTolerance));

            player.transform.position = new Vector3(5f, 0f, 0f);
            Vector2 hold = (Vector2)movementMethod.Invoke(controller, null);
            Assert.That(hold, Is.EqualTo(Vector2.zero));

            player.transform.position = new Vector3(8f, 0f, 0f);
            Vector2 approach = (Vector2)movementMethod.Invoke(controller, null);
            Assert.That(approach.x, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>正式远程敌人和弹体 prefab 必须引用冻结的数据与复用精灵。</summary>
        [Test]
        public void RangedEnemyAssets_正式资源_引用完整且数值正确()
        {
            EnemyDataSO enemyData = AssetDatabase.LoadAssetAtPath<EnemyDataSO>(
                "Assets/Data/RangedEnemy_1.asset");
            RangedEnemyAttackDataSO attackData = AssetDatabase.LoadAssetAtPath<RangedEnemyAttackDataSO>(
                "Assets/Data/RangedEnemyAttack_1.asset");
            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefab/Enemy/EnemyRanged_1.prefab");
            GameObject projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefab/Enemy/EnemyProjectile_1.prefab");

            Assert.IsNotNull(enemyData);
            Assert.IsNotNull(attackData);
            Assert.IsNotNull(enemyPrefab);
            Assert.IsNotNull(projectilePrefab);
            Assert.That(enemyData.maxHealth, Is.EqualTo(30f).Within(FloatTolerance));
            Assert.That(enemyData.moveSpeed, Is.EqualTo(1.6f).Within(FloatTolerance));
            Assert.That(enemyData.collisionDamage, Is.EqualTo(5f).Within(FloatTolerance));
            Assert.That(attackData.MaxRange, Is.EqualTo(8f).Within(FloatTolerance));
            Assert.That(attackData.FirstShotDelay, Is.EqualTo(0.8f).Within(FloatTolerance));
            Assert.That(attackData.Cooldown, Is.EqualTo(2f).Within(FloatTolerance));
            Assert.That(attackData.BaseDamage, Is.EqualTo(12f).Within(FloatTolerance));
            Assert.That(attackData.ProjectileSpeed, Is.EqualTo(5.5f).Within(FloatTolerance));
            Assert.That(attackData.ProjectileLifetime, Is.EqualTo(6f).Within(FloatTolerance));

            RangedEnemyController enemy = enemyPrefab.GetComponent<RangedEnemyController>();
            EnemyProjectile projectile = projectilePrefab.GetComponent<EnemyProjectile>();
            Assert.IsNotNull(enemy);
            Assert.IsNotNull(projectile);
            Assert.AreSame(attackData, enemy.AttackData);
            Assert.AreSame(projectilePrefab, attackData.ProjectilePrefab);
            Assert.AreEqual(
                "melee_enemy_2_strong",
                enemyPrefab.GetComponent<SpriteRenderer>().sprite.texture.name);
            Assert.AreEqual(
                "fire-ball",
                projectilePrefab.GetComponent<SpriteRenderer>().sprite.texture.name);
            Assert.That(projectilePrefab.GetComponent<SpriteRenderer>().color.r, Is.EqualTo(1f));
            Assert.That(projectilePrefab.GetComponent<SpriteRenderer>().color.g, Is.LessThan(0.5f));
        }

        /// <summary>EnemyProjectile 层必须能碰到 Default/Player，并与敌人层隔离。</summary>
        [Test]
        public void EnemyProjectilePhysics_层矩阵_只开放玩家与Default()
        {
            int projectileLayer = LayerMask.NameToLayer("EnemyProjectile");
            int defaultLayer = LayerMask.NameToLayer("Default");
            int playerLayer = LayerMask.NameToLayer("Player");
            int enemyLayer = LayerMask.NameToLayer("Enemy");

            Assert.That(projectileLayer, Is.EqualTo(8));
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(projectileLayer, defaultLayer));
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(projectileLayer, playerLayer));
            Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(projectileLayer, enemyLayer));
            Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(projectileLayer, projectileLayer));
        }

        /// <summary>Grass 波次必须使用冻结的远程敌人出现时间、速率、上限和生成半径。</summary>
        [Test]
        public void GrassWaveConfig_远程敌人规则_匹配冻结参数()
        {
            WaveConfigSO config = AssetDatabase.LoadAssetAtPath<WaveConfigSO>(
                "Assets/Data/Map/GrassWaveConfig.asset");
            GameObject rangedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefab/Enemy/EnemyRanged_1.prefab");

            Assert.IsNotNull(config);
            Assert.IsNotNull(rangedPrefab);
            WaveConfigSO.SpawnRule rangedRule = null;
            for (int index = 0; index < config.rules.Count; index++)
            {
                if (config.rules[index] != null && config.rules[index].enemyPrefab == rangedPrefab)
                {
                    rangedRule = config.rules[index];
                    break;
                }
            }

            Assert.IsNotNull(rangedRule);
            Assert.That(rangedRule.startTime, Is.EqualTo(8f).Within(FloatTolerance));
            Assert.That(rangedRule.spawnsPerSecond, Is.EqualTo(0.15f).Within(FloatTolerance));
            Assert.That(rangedRule.maxAlive, Is.EqualTo(3));
            Assert.That(rangedRule.spawnRadiusMin, Is.EqualTo(7f).Within(FloatTolerance));
            Assert.That(rangedRule.spawnRadiusMax, Is.EqualTo(10f).Within(FloatTolerance));
        }
    }
}
