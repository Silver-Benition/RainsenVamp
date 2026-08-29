using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace RainsenVampSur.Tests
{
    /// <summary>验证角色选择页的固定结构、展示数据和跨场景选择来源。</summary>
    public sealed class CharacterSelectionTests : EditModeComponentTestBase
    {
        private const float FloatTolerance = 0.0001f;

        /// <summary>每项选择页测试使用独立默认账号，避免静态解锁状态影响断言。</summary>
        [SetUp]
        public void ResetAccountProgress()
        {
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>本期配置一个角色时仍应生成 12 个槽位，并正确展示三项角色属性。</summary>
        [Test]
        public void Show_单角色配置_创建十二槽并展示角色信息()
        {
            CharacterDataSO character = CreateCharacter("测试角色", 135f, 1.25f, 4.5f);
            try
            {
                GameObject canvasObject = CreateTrackedGameObject("AutomationTest_CharacterSelectionCanvas");
                canvasObject.SetActive(false);
                canvasObject.AddComponent<Canvas>();
                CharacterSelectionUI selector = canvasObject.AddComponent<CharacterSelectionUI>();
                TestObjectUtility.SetPrivateField(
                    selector,
                    "availableCharacters",
                    new List<CharacterDataSO> { character });

                canvasObject.SetActive(true);
                selector.Show();

                Assert.IsTrue(selector.IsVisible);
                Assert.That(selector.SlotCount, Is.EqualTo(12));
                Assert.That(selector.AvailableSlotCount, Is.EqualTo(1));
                Assert.That(selector.SelectedCharacter, Is.SameAs(character));
                StringAssert.Contains("生命  135", selector.StatsText);
                StringAssert.Contains("力量  125%", selector.StatsText);
                StringAssert.Contains("移动速度  4.5", selector.StatsText);
                Assert.That(selector.PanelRoot.name, Is.EqualTo("CharacterSelectPanel"));
                Assert.That(
                    selector.PanelRoot.transform.Find("CharacterSlotGrid").childCount,
                    Is.EqualTo(12));
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>确认角色后会发出该角色，PlayerStats 初始化时会采用同一份角色基础值。</summary>
        [Test]
        public void Confirm_选择角色_PlayerStats使用选择结果()
        {
            CharacterDataSO character = CreateCharacter("跨场景角色", 88f, 1.4f, 5.25f);
            try
            {
                GameObject canvasObject = CreateTrackedGameObject("AutomationTest_CharacterSelectionConfirm");
                canvasObject.SetActive(false);
                canvasObject.AddComponent<Canvas>();
                CharacterSelectionUI selector = canvasObject.AddComponent<CharacterSelectionUI>();
                TestObjectUtility.SetPrivateField(
                    selector,
                    "availableCharacters",
                    new List<CharacterDataSO> { character });
                canvasObject.SetActive(true);
                selector.Show();

                CharacterDataSO confirmedCharacter = null;
                selector.CharacterConfirmed += selected => confirmedCharacter = selected;
                selector.ConfirmButton.onClick.Invoke();

                Assert.That(confirmedCharacter, Is.SameAs(character));
                Assert.IsTrue(CharacterSelectionSession.Select(confirmedCharacter));

                GameObject playerObject = CreateTrackedGameObject("AutomationTest_SelectedPlayer");
                playerObject.SetActive(false);
                PlayerStats stats = playerObject.AddComponent<PlayerStats>();

                // 故意让生命组件先读取属性，复现真实场景中父子物体与同物体组件
                // Awake 顺序不稳定时，属性缓存可能早于 PlayerStats.Awake 建立的情况。
                PlayerHealth health = playerObject.AddComponent<PlayerHealth>();
                TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
                TestObjectUtility.InvokeNonPublicMethod(stats, "Awake");

                Assert.That(stats.CharacterData, Is.SameAs(character));
                Assert.That(stats.MaxHealth, Is.EqualTo(88f).Within(FloatTolerance));
                Assert.That(stats.Might, Is.EqualTo(1.4f).Within(FloatTolerance));
                Assert.That(stats.FinalMoveSpeed, Is.EqualTo(5.25f).Within(FloatTolerance));
                Assert.That(health.MaxHealth, Is.EqualTo(88f).Within(FloatTolerance));
                Assert.That(health.CurrentHealth, Is.EqualTo(88f).Within(FloatTolerance));
            }
            finally
            {
                CharacterSelectionSession.Clear();
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>锁定角色槽位应显示黑影，选中后只显示解锁浮层而不泄露角色资料。</summary>
        [Test]
        public void Hover_累计击杀锁定角色_显示黑影与条件浮层()
        {
            CharacterDataSO defaultCharacter = CreateCharacter("默认测试角色", 100f, 1f, 3f);
            defaultCharacter.characterID = AccountProgressData.DefaultCharacterId;
            CharacterDataSO lockedCharacter = CreateCharacter("秘密角色", 999f, 9f, 9f);
            lockedCharacter.characterID = "character_locked_test";
            lockedCharacter.unlock.conditionType = CharacterUnlockConditionType.LifetimeKills;
            lockedCharacter.unlock.requiredAmount = 100;

            try
            {
                GameObject canvasObject = CreateTrackedGameObject("AutomationTest_LockedCharacterSelection");
                canvasObject.SetActive(false);
                canvasObject.AddComponent<Canvas>();
                CharacterSelectionUI selector = canvasObject.AddComponent<CharacterSelectionUI>();
                TestObjectUtility.SetPrivateField(
                    selector,
                    "availableCharacters",
                    new List<CharacterDataSO> { defaultCharacter, lockedCharacter });
                canvasObject.SetActive(true);
                selector.Show();

                Transform lockedSlotTransform = selector.PanelRoot.transform
                    .Find("CharacterSlotGrid/CharacterSlot_02");
                CharacterSelectionSlotUI lockedSlot = lockedSlotTransform
                    .GetComponent<CharacterSelectionSlotUI>();
                selector.HandleSlotHover(lockedSlot);

                Image portrait = lockedSlotTransform.Find("Portrait").GetComponent<Image>();
                Assert.That(portrait.color, Is.EqualTo(Color.black));
                Assert.IsTrue(selector.IsUnlockOverlayVisible);
                Assert.That(selector.StatsText, Is.Empty);
                StringAssert.Contains("0 / 100", selector.UnlockConditionText);
                Assert.IsFalse(selector.UnlockPurchaseButton.gameObject.activeSelf);
                Assert.IsFalse(selector.ConfirmButton.interactable);
            }
            finally
            {
                Object.DestroyImmediate(defaultCharacter);
                Object.DestroyImmediate(lockedCharacter);
            }
        }

        /// <summary>金币条件浮层按钮应扣除账号金币、解锁角色并立即恢复完整详情。</summary>
        [Test]
        public void Purchase_金币锁定角色_扣款并立即解锁()
        {
            AccountProgressService.Current.RecordRunResults(50, 0);
            CharacterDataSO defaultCharacter = CreateCharacter("默认测试角色", 100f, 1f, 3f);
            defaultCharacter.characterID = AccountProgressData.DefaultCharacterId;
            CharacterDataSO purchasable = CreateCharacter("金币角色", 120f, 1.2f, 3.2f);
            purchasable.characterID = "character_gold_test";
            purchasable.unlock.conditionType = CharacterUnlockConditionType.GoldPurchase;
            purchasable.unlock.requiredAmount = 40;

            try
            {
                GameObject canvasObject = CreateTrackedGameObject("AutomationTest_GoldCharacterSelection");
                canvasObject.SetActive(false);
                canvasObject.AddComponent<Canvas>();
                CharacterSelectionUI selector = canvasObject.AddComponent<CharacterSelectionUI>();
                TestObjectUtility.SetPrivateField(
                    selector,
                    "availableCharacters",
                    new List<CharacterDataSO> { defaultCharacter, purchasable });
                canvasObject.SetActive(true);
                selector.Show();

                CharacterSelectionSlotUI goldSlot = selector.PanelRoot.transform
                    .Find("CharacterSlotGrid/CharacterSlot_02")
                    .GetComponent<CharacterSelectionSlotUI>();
                selector.HandleSlotHover(goldSlot);
                Assert.IsTrue(selector.UnlockPurchaseButton.gameObject.activeSelf);

                selector.UnlockPurchaseButton.onClick.Invoke();

                Assert.That(AccountProgressService.Current.Gold, Is.EqualTo(10));
                Assert.IsTrue(AccountProgressService.Current.IsCharacterUnlocked(purchasable.characterID));
                Assert.IsFalse(selector.IsUnlockOverlayVisible);
                Assert.IsTrue(selector.ConfirmButton.interactable);
                StringAssert.Contains("生命  120", selector.StatsText);
            }
            finally
            {
                Object.DestroyImmediate(defaultCharacter);
                Object.DestroyImmediate(purchasable);
            }
        }

        private static CharacterDataSO CreateCharacter(
            string displayName,
            float maxHealth,
            float might,
            float moveSpeed)
        {
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            character.characterID = "test_character";
            character.characterDisplayName = displayName;
            character.baseStats.maxHealth = maxHealth;
            character.baseStats.might = might;
            character.baseStats.moveSpeed = moveSpeed;
            return character;
        }
    }
}
