using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证阶段二掉落与 Defang 快照在真实对象池和物理循环中的状态重置。</summary>
    public sealed class PhaseTwoPoolLifecyclePlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>Defang 敌人回池再取出后必须恢复基础伤害、状态和 SpriteRenderer 原色。</summary>
        [UnityTest]
        public IEnumerator EnemyPool_Defang快照回收_完整恢复基础状态()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_PhaseTwoPool");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 2f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 5f);
            RuntimeComponentTestUtility.SetField(enemyData, "canBeDefanged", true);

            GameObject template = CreateTrackedGameObject("PlayModeTest_DefangEnemyTemplate", false);
            template.AddComponent<SpriteRenderer>().color = Color.white;
            template.AddComponent<Rigidbody2D>().gravityScale = 0f;
            template.AddComponent<BoxCollider2D>();
            Component templateEnemy = RuntimeComponentTestUtility.AddRuntimeComponent(template, "EnemyBase");
            RuntimeComponentTestUtility.SetField(templateEnemy, "enemyData", enemyData);

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject first = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component firstEnemy = first.GetComponent("EnemyBase");
            object defangedSnapshot = CreateEnemySnapshot(20f, 4f, 0f, 0f, true);
            RuntimeComponentTestUtility.Invoke(firstEnemy, "ApplySpawnSnapshot", defangedSnapshot);

            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(firstEnemy, "IsDefanged"));
            Assert.That(RuntimeComponentTestUtility.GetProperty<float>(firstEnemy, "CurrentCollisionDamage"), Is.Zero);
            Assert.AreNotEqual(Color.white, first.GetComponent<SpriteRenderer>().color);

            RuntimeComponentTestUtility.Invoke(poolManager, "Release", template, first);
            Assert.AreEqual(Color.white, first.GetComponent<SpriteRenderer>().color);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(firstEnemy, "CurrentCollisionDamage"),
                Is.Zero);
            Assert.That(
                (float)RuntimeComponentTestUtility.Invoke(firstEnemy, "ResolveOutgoingDamage", 7f),
                Is.Zero);

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject second = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                template,
                Vector3.zero,
                Quaternion.identity));
            Component secondEnemy = second.GetComponent("EnemyBase");

            Assert.AreSame(first, second);
            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(secondEnemy, "IsDefanged"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(secondEnemy, "CurrentCollisionDamage"),
                Is.EqualTo(5f).Within(FloatTolerance));
            Assert.AreEqual(Color.white, second.GetComponent<SpriteRenderer>().color);
            yield return null;
        }

        /// <summary>Defang 敌人持续包围并在接触中停用时，玩家生命始终不得下降。</summary>
        [UnityTest]
        public IEnumerator DefangedEnemy_持续碰撞并停用_玩家不受伤()
        {
            GameObject player = CreateTrackedGameObject("PlayModeTest_DefangCollisionPlayer", false);
            player.tag = "Player";
            player.layer = RequireLayer("Player");
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
            player.AddComponent<BoxCollider2D>();
            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            player.SetActive(true);

            ScriptableObject enemyData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("EnemyDataSO"));
            RuntimeComponentTestUtility.SetField(enemyData, "maxHealth", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "moveSpeed", 0f);
            RuntimeComponentTestUtility.SetField(enemyData, "collisionDamage", 10f);
            RuntimeComponentTestUtility.SetField(enemyData, "canBeDefanged", true);

            GameObject enemyObject = CreateTrackedGameObject("PlayModeTest_DefangCollisionEnemy", false);
            enemyObject.layer = RequireLayer("Enemy");
            Rigidbody2D enemyBody = enemyObject.AddComponent<Rigidbody2D>();
            enemyBody.gravityScale = 0f;
            enemyBody.constraints = RigidbodyConstraints2D.FreezeAll;
            enemyObject.AddComponent<BoxCollider2D>();
            Component enemy = RuntimeComponentTestUtility.AddRuntimeComponent(enemyObject, "EnemyBase");
            RuntimeComponentTestUtility.SetField(enemy, "enemyData", enemyData);
            enemyObject.SetActive(true);
            object snapshot = CreateEnemySnapshot(10f, 0f, 0f, 0f, true);
            RuntimeComponentTestUtility.Invoke(enemy, "ApplySpawnSnapshot", snapshot);

            float initialHealth = RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(initialHealth).Within(FloatTolerance));

            enemyObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(initialHealth).Within(FloatTolerance));
        }

        /// <summary>池化金币与玩家真实触发器接触后应按 Greed 结算，并把实例归还对象池。</summary>
        [UnityTest]
        public IEnumerator CoinPickup_碰到Greed玩家_按倍率入账并回池()
        {
            GameObject managerObject = CreateTrackedGameObject("PlayModeTest_CoinPool");
            Component poolManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "PoolManager");

            ScriptableObject character = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("CharacterDataSO"));
            Type baseStatsType = RuntimeComponentTestUtility.RequireRuntimeType("CharacterBaseStats");
            object baseStats = Activator.CreateInstance(baseStatsType);
            baseStatsType.GetField("greed")?.SetValue(baseStats, 2f);
            RuntimeComponentTestUtility.SetField(character, "baseStats", baseStats);

            GameObject player = CreateTrackedGameObject("PlayModeTest_GreedPlayer", false);
            player.tag = "Player";
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            player.AddComponent<BoxCollider2D>();
            Component playerStats = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            RuntimeComponentTestUtility.Invoke(playerStats, "SetCharacterData", character);
            player.SetActive(true);

            GameObject coinTemplate = CreateTrackedGameObject("PlayModeTest_CoinTemplate", false);
            CircleCollider2D coinCollider = coinTemplate.AddComponent<CircleCollider2D>();
            coinCollider.isTrigger = true;
            RuntimeComponentTestUtility.AddRuntimeComponent(coinTemplate, "CoinPickup");

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Warning, new Regex("PoolManager\\.Spawn 收到的对象不是 Prefab 资产"));
#endif
            GameObject coin = TrackObject((GameObject)RuntimeComponentTestUtility.Invoke(
                poolManager,
                "Spawn",
                coinTemplate,
                player.transform.position,
                Quaternion.identity));
            Component coinPickup = coin.GetComponent("CoinPickup");
            RuntimeComponentTestUtility.Invoke(coinPickup, "ConfigureValue", 3);

            yield return new WaitForFixedUpdate();
            yield return null;

            Component runState = player.GetComponent(RuntimeComponentTestUtility.RequireRuntimeType("RunState"));
            Assert.IsNotNull(runState);
            Assert.That(RuntimeComponentTestUtility.GetProperty<int>(runState, "GoldCount"), Is.EqualTo(6));
            Assert.IsFalse(coin.activeSelf);
        }

        /// <summary>
        /// 宝箱生成在玩家碰撞体内时必须先经过拾取保护，再自动发放武器并立即显示奖励来源。
        /// 第二次奖励仍只升级同一武器，并排入 HUD 队列等待展示。
        /// </summary>
        [UnityTest]
        public IEnumerator TreasureChest_出生重叠玩家_保护后发奖并显示HUD队列()
        {
            ScriptableObject weaponData = CreateChestWeaponData();
            ScriptableObject upgradeData = CreateChestUpgradeData(weaponData);

            GameObject player = CreateTrackedGameObject("PlayModeTest_ChestPlayer", false);
            player.tag = "Player";
            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
            player.AddComponent<BoxCollider2D>();
            RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            player.SetActive(true);

            GameObject managerObject = CreateTrackedGameObject(
                "PlayModeTest_ChestLevelUpManager",
                false);
            Component levelUpManager = RuntimeComponentTestUtility.AddRuntimeComponent(
                managerObject,
                "LevelUpManager");
            IList upgrades = CreateRuntimeList(
                RuntimeComponentTestUtility.RequireRuntimeType("UpgradeDataSO"));
            upgrades.Add(upgradeData);
            RuntimeComponentTestUtility.SetField(
                levelUpManager,
                "allAvailableUpgrades",
                upgrades);
            managerObject.SetActive(true);

            GameObject canvasObject = CreateTrackedGameObject("PlayModeTest_ChestHudCanvas");
            canvasObject.AddComponent<Canvas>();
            Component toast = RuntimeComponentTestUtility.AddRuntimeComponent(
                canvasObject,
                "TreasureChestRewardToastUI");
            yield return null;

            GameObject chestObject = CreateTrackedGameObject("PlayModeTest_ProtectedChest", false);
            CircleCollider2D chestCollider = chestObject.AddComponent<CircleCollider2D>();
            chestCollider.isTrigger = true;
            Component chestPickup = RuntimeComponentTestUtility.AddRuntimeComponent(
                chestObject,
                "TreasureChestPickup");
            RuntimeComponentTestUtility.SetField(
                chestPickup,
                "pickupProtectionDuration",
                0.12f);
            chestObject.transform.position = player.transform.position;
            chestObject.SetActive(true);

            Assert.IsFalse(RuntimeComponentTestUtility.GetProperty<bool>(chestPickup, "IsPickupArmed"));
            yield return new WaitForSeconds(0.05f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(levelUpManager, "OwnedWeaponCount"),
                Is.Zero,
                "拾取保护期间不应授予任何武器。");
            Assert.IsTrue(chestObject.activeSelf);

            yield return new WaitForSeconds(0.1f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(levelUpManager, "OwnedWeaponCount"),
                Is.EqualTo(1));
            Assert.IsFalse(chestObject.activeSelf);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(toast, "IsShowingReward"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<string>(toast, "DisplayedRewardName"),
                Is.EqualTo("守护光环"));
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(toast, "DisplayedRewardLevel"),
                Is.EqualTo(1));

            object secondReward = RuntimeComponentTestUtility.Invoke(
                levelUpManager,
                "GrantRandomChestReward");
            Assert.IsNotNull(secondReward);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(levelUpManager, "OwnedWeaponCount"),
                Is.EqualTo(1),
                "后续宝箱只能升级已持有武器，不能创建重复种类。");
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<int>(toast, "QueuedRewardCount"),
                Is.EqualTo(1),
                "横幅播放期间到达的后续奖励必须排队。");
        }

        /// <summary>宝箱开启爆闪应扩张淡出、播放结束停用，并可从确定初始状态重播。</summary>
        [UnityTest]
        public IEnumerator TreasureChestBurstVfx_播放生命周期_扩张淡出并可重复初始化()
        {
            GameObject vfxObject = CreateTrackedGameObject("PlayModeTest_ChestBurst", false);
            SpriteRenderer spriteRenderer = vfxObject.AddComponent<SpriteRenderer>();
            Component burstVfx = RuntimeComponentTestUtility.AddRuntimeComponent(
                vfxObject,
                "PooledSpriteBurstVfx");
            RuntimeComponentTestUtility.SetField(burstVfx, "duration", 0.12f);
            RuntimeComponentTestUtility.SetField(burstVfx, "startScale", 0.25f);
            RuntimeComponentTestUtility.SetField(burstVfx, "endScale", 1.5f);
            RuntimeComponentTestUtility.SetField(
                burstVfx,
                "startColor",
                new Color(1f, 0.8f, 0.2f, 0.9f));
            vfxObject.SetActive(true);

            RuntimeComponentTestUtility.Invoke(burstVfx, "Play");
            float initialScale = vfxObject.transform.localScale.x;
            float initialAlpha = spriteRenderer.color.a;
            Assert.That(initialScale, Is.EqualTo(0.25f).Within(FloatTolerance));
            Assert.That(initialAlpha, Is.EqualTo(0.9f).Within(FloatTolerance));

            yield return new WaitForSeconds(0.05f);
            Assert.That(vfxObject.transform.localScale.x, Is.GreaterThan(initialScale));
            Assert.That(spriteRenderer.color.a, Is.LessThan(initialAlpha));

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(vfxObject.activeSelf);

            vfxObject.SetActive(true);
            RuntimeComponentTestUtility.Invoke(burstVfx, "Play");
            Assert.That(vfxObject.transform.localScale.x, Is.EqualTo(0.25f).Within(FloatTolerance));
            Assert.That(spriteRenderer.color.a, Is.EqualTo(0.9f).Within(FloatTolerance));
        }

        /// <summary>创建两级的真实武器资产，名称用于验证奖励横幅显示权威词条。</summary>
        private ScriptableObject CreateChestWeaponData()
        {
            ScriptableObject weaponData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("WeaponDataSO"));
            RuntimeComponentTestUtility.SetField(
                weaponData,
                "weaponID",
                "playmode_chest_guardian_aura");
            RuntimeComponentTestUtility.SetField(
                weaponData,
                "weaponNameKey",
                "weapon.aura.name");
            RuntimeComponentTestUtility.SetField(weaponData, "weaponDisplayName", "守护光环");

            Type levelDataType = RuntimeComponentTestUtility.RequireRuntimeType("WeaponLevelData");
            IList levels = CreateRuntimeList(levelDataType);
            levels.Add(Activator.CreateInstance(levelDataType));
            levels.Add(Activator.CreateInstance(levelDataType));
            RuntimeComponentTestUtility.SetField(weaponData, "levelConfigs", levels);
            return weaponData;
        }

        /// <summary>创建只包含指定武器且权重固定的真实宝箱候选。</summary>
        private ScriptableObject CreateChestUpgradeData(ScriptableObject weaponData)
        {
            ScriptableObject upgradeData = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("UpgradeDataSO"));
            RuntimeComponentTestUtility.SetField(
                upgradeData,
                "upgradeID",
                "playmode_upgrade_chest_guardian_aura");
            RuntimeComponentTestUtility.SetField(upgradeData, "baseWeight", 100f);
            RuntimeComponentTestUtility.SetField(upgradeData, "luckInfluence", 0f);
            RuntimeComponentTestUtility.SetField(upgradeData, "weaponToGrant", weaponData);
            return upgradeData;
        }

        /// <summary>按运行时元素类型创建可赋给生产泛型字段的 List 实例。</summary>
        private static IList CreateRuntimeList(Type elementType)
        {
            Type listType = typeof(List<>).MakeGenericType(elementType);
            return (IList)Activator.CreateInstance(listType);
        }

        /// <summary>通过反射构造默认程序集中的只读 EnemySpawnSnapshot。</summary>
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
    }
}
