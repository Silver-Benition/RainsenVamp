using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在真实 Player Loop 与对象启用顺序中验证角色属性消费和池化尺寸重置。</summary>
    public sealed class PlayerAttributePlayModeTests : PlayModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>角色最大生命、护甲和 Recovery 应在真实运行时间中共同生效。</summary>
        [UnityTest]
        public IEnumerator PlayerHealth_角色属性驱动_护甲减伤且持续恢复()
        {
            ScriptableObject character = CreateCharacterData(
                maxHealth: 100f,
                recovery: 10f,
                armor: 2f);
            GameObject player = CreateTrackedGameObject("PlayModeTest_AttributePlayer", false);
            Component playerStats = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            RuntimeComponentTestUtility.SetField(playerStats, "characterData", character);
            Component playerHealth = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerHealth");
            RuntimeComponentTestUtility.SetField(playerHealth, "invulnerabilityDuration", 0f);

            player.SetActive(true);
            yield return null;

            RuntimeComponentTestUtility.Invoke(playerHealth, "TakeDamage", 12f);
            Assert.That(
                RuntimeComponentTestUtility.GetProperty<float>(playerHealth, "CurrentHealth"),
                Is.EqualTo(90f).Within(FloatTolerance));

            yield return new WaitForSeconds(0.25f);
            float recoveredHealth = RuntimeComponentTestUtility.GetProperty<float>(
                playerHealth,
                "CurrentHealth");
            Assert.That(recoveredHealth, Is.GreaterThan(91.5f));
            Assert.That(recoveredHealth, Is.LessThan(94f));
        }

        /// <summary>池化直飞投射物的 Area 尺寸必须在禁用时恢复 Prefab 初始值。</summary>
        [UnityTest]
        public IEnumerator ProjectileBase_Area快照_对象池禁用时恢复初始尺寸()
        {
            GameObject projectileObject = CreateTrackedGameObject("PlayModeTest_AttributeProjectile", false);
            projectileObject.transform.localScale = new Vector3(1.5f, 0.75f, 1f);
            projectileObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            projectileObject.AddComponent<CircleCollider2D>().isTrigger = true;
            Component projectile = RuntimeComponentTestUtility.AddRuntimeComponent(
                projectileObject,
                "ProjectileBase");

            projectileObject.SetActive(true);

            Type bounceModeType = RuntimeComponentTestUtility.RequireRuntimeType("BounceMode");
            object noBounce = Enum.ToObject(bounceModeType, 0);
            RuntimeComponentTestUtility.Invoke(
                projectile,
                "Initialize",
                null,
                Vector3.right,
                10f,
                5f,
                0,
                2f,
                0,
                noBounce,
                2f);

            Assert.That(projectileObject.transform.localScale.x, Is.EqualTo(3f).Within(FloatTolerance));
            Assert.That(projectileObject.transform.localScale.y, Is.EqualTo(1.5f).Within(FloatTolerance));

            projectileObject.SetActive(false);
            Assert.That(projectileObject.transform.localScale.x, Is.EqualTo(1.5f).Within(FloatTolerance));
            Assert.That(projectileObject.transform.localScale.y, Is.EqualTo(0.75f).Within(FloatTolerance));

            yield return null;
        }

        /// <summary>暂停时间下看板仍应接收调试来源变化并显示最终移动速度。</summary>
        [UnityTest]
        public IEnumerator PlayerStatBoard_暂停期间属性变化_即时刷新最终值()
        {
            ScriptableObject character = CreateCharacterData(
                maxHealth: 100f,
                recovery: 0f,
                armor: 0f);
            GameObject player = CreateTrackedGameObject("PlayModeTest_AttributeBoardPlayer", false);
            player.tag = "Player";
            Component playerStats = RuntimeComponentTestUtility.AddRuntimeComponent(player, "PlayerStats");
            RuntimeComponentTestUtility.SetField(playerStats, "characterData", character);

            GameObject pausePanel = CreateTrackedGameObject("PlayModeTest_AttributePausePanel", false);
            Component board = RuntimeComponentTestUtility.AddRuntimeComponent(
                pausePanel,
                "PlayerStatBoardUI");

            player.SetActive(true);
            pausePanel.SetActive(true);
            yield return null;

            Time.timeScale = 0f;
            Type statType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatType");
            Type modeType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatModifierMode");
            Type modifierType = RuntimeComponentTestUtility.RequireRuntimeType("PlayerStatModifier");
            object moveSpeed = Enum.ToObject(statType, 3);
            object flatMode = Enum.ToObject(modeType, 0);
            object modifier = Activator.CreateInstance(modifierType, moveSpeed, flatMode, 2f);
            Array modifiers = Array.CreateInstance(modifierType, 1);
            modifiers.SetValue(modifier, 0);

            RuntimeComponentTestUtility.Invoke(
                playerStats,
                "SetModifiers",
                "debug.playmode_attribute_board",
                modifiers);
            yield return null;

            string valuesText = RuntimeComponentTestUtility.GetProperty<string>(
                board,
                "CurrentValuesText");
            string[] valueRows = valuesText.Split('\n');
            RectTransform boardRoot = RuntimeComponentTestUtility.GetProperty<RectTransform>(
                board,
                "BoardRoot");

            Assert.That(valueRows.Length, Is.EqualTo(21));
            Assert.That(valueRows[3], Is.EqualTo("5"));
            Assert.IsNotNull(boardRoot);
            Assert.That(boardRoot.anchorMax.x, Is.EqualTo(1f).Within(FloatTolerance));
        }

        /// <summary>创建并登记带指定生存属性的运行时角色资产。</summary>
        private ScriptableObject CreateCharacterData(float maxHealth, float recovery, float armor)
        {
            ScriptableObject character = TrackObject(
                RuntimeComponentTestUtility.CreateRuntimeScriptableObject("CharacterDataSO"));
            Type statsType = RuntimeComponentTestUtility.RequireRuntimeType("CharacterBaseStats");
            object baseStats = Activator.CreateInstance(statsType);
            RuntimeComponentTestUtility.SetField(baseStats, "maxHealth", maxHealth);
            RuntimeComponentTestUtility.SetField(baseStats, "recovery", recovery);
            RuntimeComponentTestUtility.SetField(baseStats, "armor", armor);
            RuntimeComponentTestUtility.SetField(character, "baseStats", baseStats);
            return character;
        }
    }
}
