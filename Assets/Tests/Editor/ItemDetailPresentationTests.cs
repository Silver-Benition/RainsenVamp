using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>验证详情与真实计算一致、无效属性隐藏和专用图集导入契约。</summary>
    public sealed class ItemDetailPresentationTests
    {
        private readonly List<Object> _objects = new List<Object>();
        /// <summary>释放每个用例独占对象。</summary>
        [TearDown] public void Cleanup()
        { for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]); _objects.Clear(); }
        /// <summary>创建不影响玩家存档的显式新体系角色。</summary>
        private PlayerStats Player()
        {
            var character = ScriptableObject.CreateInstance<CharacterDataSO>(); _objects.Add(character);
            character.useBrotatoStats = true;
            var go = new GameObject("DetailTest"); _objects.Add(go);
            var stats = go.AddComponent<PlayerStats>(); stats.SetCharacterData(character); return stats;
        }
        /// <summary>只读详情使用与武器相同的最终伤害、负值吸血及缩放系数。</summary>
        [Test] public void WeaponDetails_MixedScalingAndSignedEffects_AreAccurate()
        {
            var player = Player();
            player.SetModifiers("detail", new[] {
                new PlayerStatModifier(PlayerStatType.RangedDamage, PlayerStatModifierMode.Flat, 2),
                new PlayerStatModifier(PlayerStatType.LifeSteal, PlayerStatModifierMode.Flat, -20),
                new PlayerStatModifier(PlayerStatType.CritChance, PlayerStatModifierMode.Flat, -2) });
            var data = ScriptableObject.CreateInstance<WeaponDataSO>(); _objects.Add(data);
            var level = new WeaponLevelData { damage = 6, rangedScaling = 1, lifeSteal = 50, critChance = 5, critMultiplier = 1.5f, cooldown = 1.12f, attackRange = 4 };
            data.levelConfigs = new List<WeaponLevelData> { level };
            string text = RoundShopPresentation.WeaponDetails(data, 1, player);
            StringAssert.Contains("伤害：8（6 + 100%远程伤害）", text);
            StringAssert.Contains("暴击：x1.5（3%概率）", text);
            StringAssert.Contains("生命窃取：30%", text);
            StringAssert.Contains("冷却：1.12秒\n范围：400（远程）", text);
            StringAssert.DoesNotContain("穿透", text); StringAssert.DoesNotContain("数量", text);
            StringAssert.DoesNotContain("近战伤害", text); StringAssert.DoesNotContain("元素伤害", text);
            level.meleeScaling = .25f; level.pierceCount = 1; level.projectileCount = 2;
            text = RoundShopPresentation.WeaponDetails(data, 1, player, true);
            StringAssert.Contains("25%<sprite index=0>", text); StringAssert.Contains("100%<sprite index=1>", text);
            StringAssert.Contains("数量：2\n穿透：1", text);
        }
        /// <summary>普通武器可继承角色吸血，自带吸血被负值抵消后仍显示零值反馈。</summary>
        [Test] public void WeaponDetails_HidesAbsentLifeSteal_ButKeepsSuppressedInnateEffect()
        {
            var player = Player(); var data = ScriptableObject.CreateInstance<WeaponDataSO>(); _objects.Add(data);
            var level = new WeaponLevelData { elementalScaling = 1 }; data.levelConfigs = new List<WeaponLevelData> { level };
            StringAssert.DoesNotContain("生命窃取", RoundShopPresentation.WeaponDetails(data, 1, player));
            StringAssert.Contains("100%元素伤害", RoundShopPresentation.WeaponDetails(data, 1, player));
            player.SetModifiers("life", new[] { new PlayerStatModifier(PlayerStatType.LifeSteal, PlayerStatModifierMode.Flat, 10) });
            StringAssert.Contains("生命窃取：10%", RoundShopPresentation.WeaponDetails(data, 1, player));
            player.SetModifiers("life", new[] { new PlayerStatModifier(PlayerStatType.LifeSteal, PlayerStatModifierMode.Flat, -60) });
            level.lifeSteal = 50;
            StringAssert.Contains("生命窃取：0%", RoundShopPresentation.WeaponDetails(data, 1, player));
        }
        /// <summary>道具逐行显示正负修改器，零效果不占用一行。</summary>
        [Test] public void ItemDetails_OneAttributePerRow_PreservesPenalty()
        {
            var data = ScriptableObject.CreateInstance<AbilityDataSO>(); _objects.Add(data);
            data.levelConfigs[0].statModifiers = new List<PlayerStatModifier> {
                new PlayerStatModifier(PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 3),
                new PlayerStatModifier(PlayerStatType.SpeedPercent, PlayerStatModifierMode.Flat, -5),
                new PlayerStatModifier(PlayerStatType.Range, PlayerStatModifierMode.Flat, 0) };
            Assert.AreEqual("最大生命：+3\n速度：-5%", RoundShopPresentation.ItemDetails(data, 1));
        }
        /// <summary>图集必须包含三个独立字形、透明源纹理和像素导入设置。</summary>
        [Test] public void StatIcons_ThreeDistinctGlyphs_AreProjectAssets()
        {
            TMP_SpriteAsset icons = StatIconPresentation.Icons;
            Assert.IsNotNull(icons); Assert.IsNotNull(icons.material); icons.UpdateLookupTables();
            Assert.AreEqual(3, icons.spriteCharacterTable.Count);
            Assert.AreEqual("melee", icons.spriteCharacterTable[0].name);
            Assert.AreEqual("ranged", icons.spriteCharacterTable[1].name);
            Assert.AreEqual("elemental", icons.spriteCharacterTable[2].name);
            Assert.AreEqual(2172, icons.spriteSheet.width); Assert.AreEqual(724, icons.spriteSheet.height);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(icons.spriteSheet));
            Assert.AreEqual(FilterMode.Point, importer.filterMode); Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.DoesSourceTextureHaveAlpha());
            for (int i = 0; i < 3; i++) Assert.AreEqual(724 * i, icons.spriteGlyphTable[i].glyphRect.x);
        }
    }
}
