using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在真实 Player Loop 与 Physics2D 中验证正式机制能力。</summary>
    public sealed class AbilitySystemPlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>
        /// 反击脉冲应对多碰撞体敌人只伤害一次、遵守独立冷却，并忽略致命受伤。
        /// </summary>
        [UnityTest]
        public IEnumerator RetaliationPulse_非致命受伤_范围伤害去重并遵守冷却()
        {
            Component playerHealth;
            Component abilityManager;
            CreatePlayer(out playerHealth, out abilityManager);

            ScriptableObject pulseMechanic = CreatePulseMechanic(0.05f, 3f, 20f);
            ScriptableObject abilityData = CreateMechanicAbility(
                "playmode_retaliation_pulse",
                pulseMechanic);
            Component enemy = CreateEnemyWithTwoColliders(new Vector2(1f, 0f), 200f);

            yield return new WaitForFixedUpdate();

            object state = RuntimeComponentTestUtility.Invoke(
                abilityManager,
                "GrantOrUpgrade",
                abilityData);
            Assert.IsNotNull(state);

            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 1f);
            float afterFirstPulse = RuntimeComponentTestUtility.GetProperty<float>(enemy, "CurrentHealth");

            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 1f);
            float duringCooldown = RuntimeComponentTestUtility.GetProperty<float>(enemy, "CurrentHealth");

            yield return new WaitForSeconds(0.06f);
            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 1f);
            float afterSecondPulse = RuntimeComponentTestUtility.GetProperty<float>(enemy, "CurrentHealth");

            yield return new WaitForSeconds(0.06f);
            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 1000f);
            float afterLethalDamage = RuntimeComponentTestUtility.GetProperty<float>(enemy, "CurrentHealth");

            Assert.That(afterFirstPulse, Is.EqualTo(180f).Within(FloatTolerance),
                "同一 EnemyBase 的两个 Collider 被重复结算。 ");
            Assert.That(duringCooldown, Is.EqualTo(180f).Within(FloatTolerance));
            Assert.That(afterSecondPulse, Is.EqualTo(160f).Within(FloatTolerance));
            Assert.That(afterLethalDamage, Is.EqualTo(160f).Within(FloatTolerance));
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(playerHealth, "IsDead"));
        }

        /// <summary>创建最小玩家，并在启用前完成正式组件与无敌时间配置。</summary>
        private void CreatePlayer(out Component playerHealth, out Component abilityManager)
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_AbilityPlayer", false);
            player.tag = "Player";
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0f);
            abilityManager = RuntimeComponentTestUtility.AddRuntimeComponent(player, "AbilityManager");
            player.SetActive(true);
        }

        /// <summary>创建一个具有根与子碰撞体、但只有单一 EnemyBase 的敌人。</summary>
        private Component CreateEnemyWithTwoColliders(Vector2 position, float maxHealth)
        {
            int enemyLayer = RequireLayer("Enemy");
            GameObject enemyObject = CreateTrackedGameObject("PlayModeTest_MultiColliderEnemy", false);
            enemyObject.layer = enemyLayer;
            enemyObject.transform.position = position;
            Rigidbody2D body = enemyObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            CircleCollider2D rootCollider = enemyObject.AddComponent<CircleCollider2D>();
            rootCollider.radius = 0.4f;

            GameObject child = CreateTrackedGameObject("PlayModeTest_EnemySecondaryCollider", false);
            child.layer = enemyLayer;
            child.transform.SetParent(enemyObject.transform, false);
            child.transform.localPosition = new Vector3(0.1f, 0f, 0f);
            CircleCollider2D childCollider = child.AddComponent<CircleCollider2D>();
            childCollider.radius = 0.4f;
            child.SetActive(true);

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", maxHealth);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 0f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 0f);

            Component enemy = RuntimeComponentTestUtility.AddRuntimeComponent(enemyObject, "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemy, "enemyData", enemyData);
            enemyObject.SetActive(true);
            return enemy;
        }

        /// <summary>创建单级反击机制资产，表现 Prefab 留空以单独验证逻辑结算。</summary>
        private ScriptableObject CreatePulseMechanic(float cooldown, float radius, float damage)
        {
            ScriptableObject mechanic = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject(
                    "RetaliationPulseMechanicSO"));
            Type levelType = RuntimeComponentTestUtility.RequireRuntimeType(
                "RetaliationPulseLevelConfig");
            object level = Activator.CreateInstance(levelType);
            RuntimeComponentTestUtility.SetField(level, "cooldown", cooldown);
            RuntimeComponentTestUtility.SetField(level, "radius", radius);
            RuntimeComponentTestUtility.SetField(level, "baseDamage", damage);
            IList levels = CreateRuntimeList(levelType);
            levels.Add(level);
            RuntimeComponentTestUtility.SetField(mechanic, "levelConfigs", levels);
            RuntimeComponentTestUtility.SetField(mechanic, "overlapCapacity", 16);
            return mechanic;
        }

        /// <summary>创建引用指定机制的单级正式能力资产。</summary>
        private ScriptableObject CreateMechanicAbility(string abilityId, ScriptableObject mechanic)
        {
            ScriptableObject ability = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("AbilityDataSO"));
            RuntimeComponentTestUtility.SetField(ability, "abilityID", abilityId);
            RuntimeComponentTestUtility.SetField(ability, "mechanic", mechanic);

            Type levelType = RuntimeComponentTestUtility.RequireRuntimeType("AbilityLevelData");
            IList levels = CreateRuntimeList(levelType);
            levels.Add(Activator.CreateInstance(levelType));
            RuntimeComponentTestUtility.SetField(ability, "levelConfigs", levels);
            return ability;
        }

        /// <summary>按运行时元素类型创建可赋给生产泛型字段的 List 实例。</summary>
        private static IList CreateRuntimeList(Type elementType)
        {
            Type listType = typeof(List<>).MakeGenericType(elementType);
            return (IList)Activator.CreateInstance(listType);
        }
    }
}
