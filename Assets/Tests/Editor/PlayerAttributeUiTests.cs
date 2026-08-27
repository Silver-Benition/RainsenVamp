using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证暂停属性看板和开发调试面板都通过正式 PlayerStats 快照工作。</summary>
    public sealed class PlayerAttributeUiTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>看板必须建立 21 行固定顺序，并在最终属性变化后立即刷新显示值。</summary>
        [Test]
        public void PlayerStatBoard_属性变化_刷新右侧二十一行最终值()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                GameObject player = CreateTrackedGameObject("AutomationTest_AttributeBoardPlayer");
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(character);

                GameObject pausePanel = CreateTrackedGameObject("AutomationTest_PausePanel");
                pausePanel.SetActive(false);
                PlayerStatBoardUI board = pausePanel.AddComponent<PlayerStatBoardUI>();
                TestObjectUtility.SetPrivateField(board, "_playerStats", stats);
                TestObjectUtility.InvokeNonPublicMethod(board, "Awake");
                TestObjectUtility.InvokeNonPublicMethod(board, "OnEnable");

                Assert.That(board.DisplayedStatCount, Is.EqualTo(21));
                Assert.IsNotNull(board.BoardRoot);
                Assert.That(board.BoardRoot.anchorMin.x, Is.EqualTo(1f).Within(FloatTolerance));
                Assert.That(board.BoardRoot.pivot.x, Is.EqualTo(1f).Within(FloatTolerance));

                TMP_Text labels = board.BoardRoot.Find("Labels").GetComponent<TMP_Text>();
                string[] labelRows = labels.text.Split('\n');
                string[] valueRows = board.CurrentValuesText.Split('\n');
                Assert.That(labelRows.Length, Is.EqualTo(21));
                Assert.That(valueRows.Length, Is.EqualTo(21));
                Assert.That(labelRows[0], Is.EqualTo("最大生命"));
                Assert.That(labelRows[20], Is.EqualTo("削弱"));
                Assert.That(valueRows[(int)PlayerStatType.Might], Is.EqualTo("0%"));

                stats.SetModifiers(
                    "ability.board_test",
                    new[]
                    {
                        new PlayerStatModifier(
                            PlayerStatType.Might,
                            PlayerStatModifierMode.AdditivePercent,
                            0.5f)
                    });

                valueRows = board.CurrentValuesText.Split('\n');
                Assert.That(valueRows[(int)PlayerStatType.Might], Is.EqualTo("+50%"));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>F9 工具应保留多个调试项，并可一次性移除专用来源恢复角色基础值。</summary>
        [Test]
        public void PlayerAttributeDebugPanel_设置与清除_只修改调试来源()
        {
            CharacterDataSO character = CreateCharacterData();
            try
            {
                GameObject player = CreateTrackedGameObject("AutomationTest_AttributeDebugPlayer");
                PlayerStats stats = player.AddComponent<PlayerStats>();
                stats.SetCharacterData(character);

                GameObject panelObject = CreateTrackedGameObject("AutomationTest_AttributeDebugPanel");
                PlayerAttributeDebugPanel panel = panelObject.AddComponent<PlayerAttributeDebugPanel>();
                TestObjectUtility.SetPrivateField(panel, "_playerStats", stats);
                TestObjectUtility.InvokeNonPublicMethod(panel, "Awake");
                TestObjectUtility.SetPrivateField(panel, "_playerStats", stats);

                Assert.IsTrue(panel.DebugSetModifier(
                    PlayerStatType.MoveSpeed,
                    PlayerStatModifierMode.Flat,
                    2f));
                Assert.IsTrue(panel.DebugSetModifier(
                    PlayerStatType.Might,
                    PlayerStatModifierMode.AdditivePercent,
                    0.5f));
                Assert.That(stats.FinalMoveSpeed, Is.EqualTo(5f).Within(FloatTolerance));
                Assert.That(stats.Might, Is.EqualTo(1.5f).Within(FloatTolerance));

                Assert.IsTrue(panel.DebugClearModifiers());
                Assert.That(stats.FinalMoveSpeed, Is.EqualTo(3f).Within(FloatTolerance));
                Assert.That(stats.Might, Is.EqualTo(1f).Within(FloatTolerance));
                Assert.That(character.baseStats.moveSpeed, Is.EqualTo(3f).Within(FloatTolerance));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }
#endif

        private static CharacterDataSO CreateCharacterData()
        {
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            character.characterID = "character_attribute_ui_test";
            character.baseStats = new CharacterBaseStats();
            return character;
        }
    }
}
