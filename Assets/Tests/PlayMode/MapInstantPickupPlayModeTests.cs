using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在真实 Player Loop 与 Physics2D 中验证资源磁吸、地图近身拾取与冻结生命周期。</summary>
    public sealed class MapInstantPickupPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.02f;

        /// <summary>磁吸捕获后必须先远离玩家，再切换为加速追踪，并在复用时清空状态。</summary>
        [UnityTest]
        public IEnumerator MagneticPickupMotion_先外飘再追踪并在复用时重置()
        {
            GameObject target = CreateTrackedGameObject("PlayModeTest_MagneticTarget");
            GameObject pickup = CreateTrackedGameObject("PlayModeTest_MagneticPickup", false);
            pickup.transform.position = new Vector3(2f, 0f, 0f);
            Component motion = RuntimeComponentTestUtility.AddRuntimeComponent(
                pickup,
                "MagneticPickupMotion");
            RuntimeComponentTestUtility.SetField(motion, "scatterDuration", 0.18f);
            RuntimeComponentTestUtility.SetField(motion, "scatterDistance", 0.45f);
            RuntimeComponentTestUtility.SetField(motion, "baseFlySpeed", 5f);
            RuntimeComponentTestUtility.SetField(motion, "acceleration", 15f);
            pickup.SetActive(true);

            float initialDistance = Vector2.Distance(pickup.transform.position, target.transform.position);
            RuntimeComponentTestUtility.Invoke(
                motion,
                "StartFlyingTowards",
                target.transform);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<object>(motion, "State").ToString(),
                Is.EqualTo("Scatter"));

            yield return new WaitForSeconds(0.06f);
            float scatteredDistance = Vector2.Distance(
                pickup.transform.position,
                target.transform.position);
            Assert.That(scatteredDistance, Is.GreaterThan(initialDistance));

            yield return new WaitForSeconds(0.15f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<object>(motion, "State").ToString(),
                Is.EqualTo("Homing"));
            float homingStartDistance = Vector2.Distance(
                pickup.transform.position,
                target.transform.position);

            yield return new WaitForSeconds(0.2f);
            Assert.That(
                Vector2.Distance(pickup.transform.position, target.transform.position),
                Is.LessThan(homingStartDistance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(motion, "CurrentSpeed"),
                Is.GreaterThan(5f));

            pickup.SetActive(false);
            pickup.SetActive(true);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<object>(motion, "State").ToString(),
                Is.EqualTo("Idle"));
            Assert.IsNull(RuntimeComponentTestUtility.GetProperty<Transform>(motion, "TargetPlayer"));
        }

        /// <summary>舰长在磁吸范围内必须静止，只有玩家本体近身接触才恢复 45 点生命。</summary>
        [UnityTest]
        public IEnumerator CaptainPickup_碰到玩家_恢复45点并消费()
        {
            ScriptableObject healingEffect = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject(
                    "HealingMapInstantEffectSO"));
            RuntimeComponentTestUtility.SetField(healingEffect, "healAmount", 45f);
            ScriptableObject pickupData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject(
                    "MapInstantEffectPickupDataSO"));
            RuntimeComponentTestUtility.SetField(pickupData, "effect", healingEffect);

            GameObject player = CreateTrackedGameObject("PlayModeTest_CaptainPlayer", false);
            player.tag = "Player";
            player.layer = RequireLayer("Player");
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.bodyType = RigidbodyType2D.Kinematic;
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            player.AddComponent<BoxCollider2D>();
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            Component health = RuntimeComponentTestUtility.AddRuntimeComponent(
                player,
                "PlayerHealth");

            GameObject magnetRadius = CreateTrackedGameObject(
                "PlayModeTest_CaptainMagnetRadius",
                false);
            magnetRadius.layer = RequireLayer("Player");
            magnetRadius.transform.SetParent(player.transform, false);
            CircleCollider2D magnetCollider = magnetRadius.AddComponent<CircleCollider2D>();
            magnetCollider.isTrigger = true;
            RuntimeComponentTestUtility.AddRuntimeComponent(magnetRadius, "PlayerMagnet");
            magnetRadius.SetActive(true);
            player.SetActive(true);
            RuntimeComponentTestUtility.Invoke(health, "TakeDamage", 60f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(health, "CurrentHealth"),
                Is.EqualTo(40f).Within(FloatTolerance));

            GameObject pickup = CreateTrackedGameObject("PlayModeTest_CaptainPickup", false);
            pickup.layer = RequireLayer("ExpGem");
            CircleCollider2D pickupCollider = pickup.AddComponent<CircleCollider2D>();
            pickupCollider.isTrigger = true;
            RuntimeComponentTestUtility.AddRuntimeComponent(pickup, "MapInstantEffectPickup");
            Component reporter = pickup.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType(
                    "MapInstantEffectPickupReporter"));
            RuntimeComponentTestUtility.SetField(reporter, "pickupData", pickupData);
            pickup.transform.position = player.transform.position + Vector3.right * 2f;
            pickup.SetActive(true);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 positionInsideMagnet = pickup.transform.position;
            yield return new WaitForSeconds(0.15f);
            Assert.That(pickup.transform.position, Is.EqualTo(positionInsideMagnet));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(health, "CurrentHealth"),
                Is.EqualTo(40f).Within(FloatTolerance));
            Assert.IsTrue(pickup.activeSelf);

            player.transform.position = pickup.transform.position;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(health, "CurrentHealth"),
                Is.EqualTo(85f).Within(FloatTolerance));
            Assert.IsFalse(pickup.activeSelf);
        }

        /// <summary>水晶球的真实触发器接触必须开启 15 秒冻结，同时停止敌人移动与动画。</summary>
        [UnityTest]
        public IEnumerator CrystalBallPickup_碰到玩家_冻结敌人移动与动画()
        {
            GameObject controllerObject = CreateTrackedGameObject("PlayModeTest_CrystalFreeze");
            Component controller = RuntimeComponentTestUtility.AddRuntimeComponent(
                controllerObject,
                "WorldFreezeController");
            GameObject overlayObject = CreateTrackedGameObject(
                "PlayModeTest_WorldFreezeOverlay",
                false);
            SpriteRenderer overlayRenderer = overlayObject.AddComponent<SpriteRenderer>();
            overlayRenderer.enabled = false;
            Component overlay = RuntimeComponentTestUtility.AddRuntimeComponent(
                overlayObject,
                "WorldFreezeOverlay");
            RuntimeComponentTestUtility.SetField(overlay, "freezeController", controller);
            overlayObject.SetActive(true);
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(overlay, "IsVisible"));
            ScriptableObject freezeEffect = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject(
                    "WorldFreezeMapInstantEffectSO"));
            RuntimeComponentTestUtility.SetField(freezeEffect, "duration", 15f);
            ScriptableObject pickupData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject(
                    "MapInstantEffectPickupDataSO"));
            RuntimeComponentTestUtility.SetField(pickupData, "effect", freezeEffect);

            GameObject player = CreateTrackedGameObject("PlayModeTest_CrystalPlayer", false);
            player.tag = "Player";
            player.layer = RequireLayer("Player");
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.bodyType = RigidbodyType2D.Kinematic;
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
            player.AddComponent<BoxCollider2D>();
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            player.SetActive(true);

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 2f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 5f);
            GameObject enemy = CreateTrackedGameObject("PlayModeTest_CrystalEnemy", false);
            enemy.layer = RequireLayer("Enemy");
            enemy.transform.position = Vector3.right * 2f;
            Rigidbody2D enemyBody = enemy.AddComponent<Rigidbody2D>();
            enemyBody.gravityScale = 0f;
            enemyBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            enemy.AddComponent<BoxCollider2D>();
            Animator enemyAnimator = enemy.AddComponent<Animator>();
            enemyAnimator.speed = 0.75f;
            Component enemyComponent = RuntimeComponentTestUtility.AddRuntimeComponent(
                enemy,
                "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemyComponent, "enemyData", enemyData);
            enemy.SetActive(true);

            GameObject pickup = CreateTrackedGameObject("PlayModeTest_CrystalPickup", false);
            pickup.layer = RequireLayer("ExpGem");
            CircleCollider2D pickupCollider = pickup.AddComponent<CircleCollider2D>();
            pickupCollider.isTrigger = true;
            RuntimeComponentTestUtility.AddRuntimeComponent(pickup, "MapInstantEffectPickup");
            Component reporter = pickup.GetComponent(
                RuntimeComponentTestUtility.RequireRuntimeType(
                    "MapInstantEffectPickupReporter"));
            RuntimeComponentTestUtility.SetField(reporter, "pickupData", pickupData);
            pickup.transform.position = player.transform.position;
            pickup.SetActive(true);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsFalse(pickup.activeSelf);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(controller, "RemainingDuration"),
                Is.GreaterThan(14.8f));
            Assert.That(enemyBody.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.That(enemyBody.constraints, Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
            Assert.That(enemyAnimator.speed, Is.EqualTo(0f).Within(FloatTolerance));
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(overlay, "IsVisible"));

            RuntimeComponentTestUtility.Invoke(controller, "CancelFreeze");
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(overlay, "IsVisible"));
            yield return new WaitForFixedUpdate();
            Assert.That(
                enemyBody.constraints,
                Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));
            Assert.That(enemyAnimator.speed, Is.EqualTo(0.75f).Within(FloatTolerance));
        }

        /// <summary>冻结期间敌方弹体必须停止位移与寿命，暂停菜单期间冻结倒计时也必须停止。</summary>
        [UnityTest]
        public IEnumerator WorldFreeze_暂停敌方弹体并按游戏时间恢复()
        {
            GameObject controllerObject = CreateTrackedGameObject("PlayModeTest_WorldFreeze");
            Component controller = RuntimeComponentTestUtility.AddRuntimeComponent(
                controllerObject,
                "WorldFreezeController");

            GameObject projectileObject = CreateTrackedGameObject(
                "PlayModeTest_FrozenProjectile",
                false);
            Rigidbody2D body = projectileObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            CircleCollider2D collider = projectileObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            Component projectile = RuntimeComponentTestUtility.AddRuntimeComponent(
                projectileObject,
                "EnemyProjectile");
            projectileObject.SetActive(true);
            RuntimeComponentTestUtility.Invoke(
                projectile,
                "Launch",
                Vector2.right * 4f,
                10f,
                null,
                1f);

            float lifetimeBeforeFreeze = RuntimeComponentTestUtility.GetProperty<float>(
                projectile,
                "RemainingLifetime");
            Assert.IsTrue((bool)RuntimeComponentTestUtility.Invoke(
                controller,
                "TryFreeze",
                0.2f));
            yield return null;
            Assert.That(body.velocity.sqrMagnitude, Is.EqualTo(0f).Within(FloatTolerance));
            float frozenLifetime = RuntimeComponentTestUtility.GetProperty<float>(
                projectile,
                "RemainingLifetime");
            Assert.That(frozenLifetime, Is.EqualTo(lifetimeBeforeFreeze).Within(FloatTolerance));

            float remainingBeforePause = RuntimeComponentTestUtility.GetProperty<float>(
                controller,
                "RemainingDuration");
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(controller, "RemainingDuration"),
                Is.EqualTo(remainingBeforePause).Within(FloatTolerance));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(projectile, "RemainingLifetime"),
                Is.EqualTo(frozenLifetime).Within(FloatTolerance));

            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.25f);
            yield return null;
            Assert.That(body.velocity.x, Is.EqualTo(4f).Within(0.1f));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(projectile, "RemainingLifetime"),
                Is.LessThan(frozenLifetime));
        }
    }
}
