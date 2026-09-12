using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RainsenVampSur.Tests
{
    /// <summary>验证卡片展示与实际成长数值一致，不接触真实账号。</summary>
    public sealed class AccountShopPresentationTests
    {
        /// <summary>当前累计与本次增量分别显示，概率、冷却与绝对单位不混淆。</summary>
        [TestCase("account_might", 0, "0%", "+5%")]
        [TestCase("account_might", 1, "+5%", "+5%")]
        [TestCase("account_cooldown", 1, "-5%", "-5%")]
        [TestCase("account_defang", 0, "0%", "+1%")]
        [TestCase("account_defang", 1, "+1%", "+1%")]
        [TestCase("account_recovery", 1, "+0.1/秒", "+0.1/秒")]
        [TestCase("account_maxhealth", 1, "+10", "+10")]
        [TestCase("account_movespeed", 2, "+10%", "+5%")]
        [TestCase("account_magnet", 3, "+15%", "已满级")]
        public void CumulativeAndIncrement_UseActualModifierUnits(string id, int level, string current, string increment)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath);
            Assert.That(AccountShopEffectPresentation.Format(catalog.Find(id), level, false), Is.EqualTo(current));
            Assert.That(AccountShopEffectPresentation.Format(catalog.Find(id), level, true), Is.EqualTo(increment));
        }

        /// <summary>移速和磁吸真实属性按百分比生效，旧实付记录仍按原价退款。</summary>
        [TestCase("account_movespeed", PlayerStatType.MoveSpeed)]
        [TestCase("account_magnet", PlayerStatType.Magnet)]
        [TestCase("account_defang", PlayerStatType.Defang)]
        public void ExistingPurchase_NewStaticConfig_ChangesActualStatsAndPreservesRefund(string id, PlayerStatType stat)
        {
            var data = AccountProgressData.CreateDefault();
            data.upgradePurchases.Add(new AccountUpgradePurchaseRecord { stableId = id, paidCosts = new List<int> { 73 } });
            var storage = new InMemoryAccountProgressStorage(); storage.Save(data);
            AccountProgressService.SetStorageForTests(storage);
            var player = new GameObject("AccountPresentationTest"); player.SetActive(false);
            try
            {
                var stats = player.AddComponent<PlayerStats>();
                var serialized = new SerializedObject(stats);
                serialized.FindProperty("accountUpgradeCatalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(stats.GetFinalStat(stat), Is.EqualTo(stat == PlayerStatType.Defang ? 0.01f : 3.15f).Within(0.0001f));
                Assert.IsTrue(AccountProgressService.Current.TryRefundUpgrade(id));
                Assert.That(AccountProgressService.Current.Gold, Is.EqualTo(73));
            }
            finally { Object.DestroyImmediate(player); AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage()); }
        }

        /// <summary>定向作者逻辑仅转换批准的旧数值，保留价格/上限且不会二次覆盖自定义数值。</summary>
        [Test]
        public void TargetedMigration_PreservesAuthorPricesAndCaps()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath);
            var definition = Object.Instantiate(catalog.Find("account_movespeed"));
            try
            {
                definition.maxLevel = 2;
                for (int i = 0; i < 3; i++)
                {
                    definition.levels[i].cost = 71 + i;
                    definition.levels[i].modifiers[0] = new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.Flat, 0.2f * (i + 1));
                }
                AccountUpgradeSetup.ApplyApprovedPercentUpgrade(definition);
                Assert.That(definition.maxLevel, Is.EqualTo(2));
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(definition.levels[i].cost, Is.EqualTo(71 + i));
                    Assert.That(definition.levels[i].modifiers[0].Mode, Is.EqualTo(PlayerStatModifierMode.AdditivePercent));
                    Assert.That(definition.levels[i].modifiers[0].Value, Is.EqualTo(0.05f * (i + 1)));
                }
                definition.levels[0].modifiers[0] = new PlayerStatModifier(PlayerStatType.MoveSpeed, PlayerStatModifierMode.AdditivePercent, 0.07f);
                AccountUpgradeSetup.ApplyApprovedPercentUpgrade(definition);
                Assert.That(definition.levels[0].modifiers[0].Value, Is.EqualTo(0.07f));
            }
            finally { Object.DestroyImmediate(definition); }
        }

        /// <summary>模板包含图标框、信息框和动态等级格；13 级分行，降上限后隐藏多余格子。</summary>
        [Test]
        public void CardTemplate_DynamicLevelCellsAndReusablePointIcons()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AccountUpgradeCatalogSO>(AccountUpgradeSetup.CatalogPath);
            foreach (var definition in catalog.upgrades)
            {
                Assert.IsNotNull(definition.icon);
                Assert.That(definition.icon.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
            Assert.IsNotNull(catalog.sealSlotIcon);
            Assert.IsNotNull(catalog.goldIcon);
            Assert.That(catalog.advancedIcons.Count, Is.GreaterThanOrEqualTo(4));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/UI/AccountShopEntry.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var entry = instance.GetComponent<AccountShopEntryUI>();
                entry.Refresh(catalog.upgrades[0].icon, 6, 13, "999", true);
                Assert.That(entry.VisibleLevelCount, Is.EqualTo(13));
                var grid = instance.GetComponentInChildren<GridLayoutGroup>();
                Assert.That(grid.constraintCount, Is.EqualTo(5));
                Assert.That(grid.transform.childCount, Is.EqualTo(14)); // 一份禁用模板 + 13 个真实格。
                Assert.IsTrue(entry.IsLocked);
                entry.Refresh(catalog.upgrades[0].icon, 6, 4, "已满级", false);
                Assert.That(entry.VisibleLevelCount, Is.EqualTo(4));
                int active = 0;
                foreach (Transform child in grid.transform) if (child.gameObject.activeSelf) active++;
                Assert.That(active, Is.EqualTo(4));
                Assert.IsNotNull(instance.transform.Find("IconSlot/IconFrame/Icon"));
                Assert.IsNotNull(instance.transform.Find("InformationFrame/Price"));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
