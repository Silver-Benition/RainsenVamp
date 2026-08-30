using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证真实 Physics2D 接触回调可以贯通敌人伤害与玩家生命系统。</summary>
    public sealed class CombatPhysicsPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>Enemy 与 Player 持续接触时，OnCollisionStay2D 应造成一次有效伤害。</summary>
        [UnityTest]
        public IEnumerator EnemyContact_真实物理接触_玩家收到配置伤害()
        {
            int playerLayer = RequireLayer("Player");
            int enemyLayer = RequireLayer("Enemy");
            Assert.IsFalse(
                Physics2D.GetIgnoreLayerCollision(playerLayer, enemyLayer),
                "Physics2D 碰撞矩阵禁止了 Player 与 Enemy 接触。");

            Component playerHealth = CreatePlayer(playerLayer);
            CreateEnemy(enemyLayer);
            Physics2D.SyncTransforms();

            float timeoutAt = Time.realtimeSinceStartup + 1f;
            while (RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth") >= 100f &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(83f).Within(FloatTolerance),
                "真实 OnCollisionStay2D 没有把 EnemyDataSO.collisionDamage 传给 PlayerHealth。");
        }

        /// <summary>创建带真实 Rigidbody2D、Collider2D 和 PlayerHealth 的玩家。</summary>
        private Component CreatePlayer(int playerLayer)
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_Player", false);
            player.tag = "Player";
            player.layer = playerLayer;
            player.transform.position = Vector3.zero;

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            player.AddComponent<BoxCollider2D>().size = Vector2.one;
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHurtbox");

            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(
                player,
                "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "maxHealth", 100f);
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0.5f);
            player.SetActive(true);
            return playerHealth;
        }

        /// <summary>创建由真实 EnemyDataSO 驱动的动态敌人，并与玩家保持轻微重叠。</summary>
        private void CreateEnemy(int enemyLayer)
        {
            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 20f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 0f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 17f);

            GameObject enemy = CreateTrackedGameObject("PlayModeTest_Enemy", false);
            enemy.layer = enemyLayer;
            enemy.transform.position = new Vector3(0.9f, 0f, 0f);

            Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            enemy.AddComponent<BoxCollider2D>().size = Vector2.one;

            Component enemyBase = RuntimeComponentTestUtility.AddRuntimeComponent(enemy, "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemyBase, "enemyData", enemyData);
            enemy.SetActive(true);
        }
    }
}
