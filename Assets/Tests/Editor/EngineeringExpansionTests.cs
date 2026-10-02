using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>第二批内容的数据、正式入口和工程数值契约。</summary>
public sealed class EngineeringExpansionTests
{
    /// <summary>二十件内容与四十档武器全部具有正式商店入口和池化资产。</summary>
    [Test] public void ProductionAssets_ExactlyMatchTwentyApprovedDefinitions()
    {
        var definitions=JsonUtility.FromJson<EngineeringExpansionSetup.Definition>(File.ReadAllText(EngineeringExpansionSetup.Root+"/definitions.json"));
        var shop=AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
        Assert.AreEqual(10,definitions.weapons.Length);Assert.AreEqual(10,definitions.items.Length);
        foreach(var w in definitions.weapons)
        {
            var data=AssetDatabase.LoadAssetAtPath<WeaponDataSO>(EngineeringExpansionSetup.Root+"/Weapons/"+w.id+".asset");
            Assert.NotNull(data.icon,w.id);Assert.AreEqual(4,data.roundTierConfigs.Count);Assert.AreEqual(w.retired,data.retiredFromPool);
            Assert.NotNull(data.projectilePrefab.GetComponent<IPoolable>());Assert.NotNull(data.expansionEffectPrefab.GetComponent<ExpansionEffect>());
            Assert.AreEqual(1,shop.products.Count(x=>x.content.weaponToGrant==data));
            if(data.runtimeType==WeaponRuntimeType.Melee)
            {var sprite=data.projectilePrefab.GetComponentInChildren<SpriteRenderer>(true);Assert.AreSame(data.icon,sprite.sprite);Assert.AreEqual(Color.white,sprite.color,"持武与攻击不能继承旧模板染色");}
            for(int i=0;i<4;i++)Assert.AreEqual(JsonUtility.ToJson(w.tiers[i]),JsonUtility.ToJson(data.GetRoundTierConfig(i+1)),w.id);
        }
        foreach(var item in definitions.items)
        {
            var data=AssetDatabase.LoadAssetAtPath<AbilityDataSO>(EngineeringExpansionSetup.Root+"/Items/"+item.id+".asset");
            Assert.NotNull(data.icon);Assert.IsTrue(data.IsAvailableInBrotato());Assert.AreEqual(item.quality,data.quality);Assert.AreEqual(item.maxCopies,data.maxCopies);
            Assert.AreEqual(1,shop.products.Count(x=>x.content.abilityToGrant==data));
            Assert.AreEqual(item.price,shop.products.Single(x=>x.content.abilityToGrant==data).basePrice);
            Assert.AreEqual(JsonUtility.ToJson(new AbilityLevelData{statModifiers=item.modifiers}),JsonUtility.ToJson(new AbilityLevelData{statModifiers=data.GetLevelConfig(1).statModifiers}));
            if(item.kind>0){var config=(StructureItemMechanicSO)data.mechanic;Assert.NotNull(config.structurePrefab.GetComponent<EngineeringStructure>());Assert.IsNull(config.structurePrefab.GetComponent<Collider2D>());}
        }
        CollectionAssert.AreEqual(new[]{1,2,3,4},definitions.items.Where(i=>i.kind>0).Select(i=>i.quality));
        Assert.AreEqual(8,definitions.items.Where(i=>i.kind>0).Sum(i=>i.maxCopies));
        CollectionAssert.AreEqual(new[]{2f,3f,4f,5f},shop.stats.Single(s=>s.modifier.StatType==PlayerStatType.Engineering).tierValues);
    }

    /// <summary>混合武器保留普通伤害百分比；纯构筑物仅受工程学影响。</summary>
    [Test] public void Engineering_WeaponMixAndStructureIndependentFormula()
    {
        var go=new GameObject("EngineeringFormula");var character=ScriptableObject.CreateInstance<CharacterDataSO>();
        try
        {
            character.useBrotatoStats=true;var stats=go.AddComponent<PlayerStats>();stats.SetCharacterData(character);
            stats.SetModifiers("test",new[]{new PlayerStatModifier(PlayerStatType.Engineering,PlayerStatModifierMode.Flat,20),new PlayerStatModifier(PlayerStatType.MeleeDamage,PlayerStatModifierMode.Flat,10),new PlayerStatModifier(PlayerStatType.DamagePercent,PlayerStatModifierMode.Flat,50)});
            var level=new WeaponLevelData{damage=8,meleeScaling=.8f,engineeringScaling=.4f};
            Assert.AreEqual(36,BrotatoStatRules.Damage(level,stats));Assert.AreEqual(14,BrotatoStatRules.EngineeringPower(4,.5f,stats.GetFinalStat(PlayerStatType.Engineering)));
            var item=AssetDatabase.LoadAssetAtPath<AbilityDataSO>(EngineeringExpansionSetup.Root+"/Items/copper_sentry.asset");
            StringAssert.Contains("14",RoundShopPresentation.ItemOfferDetails(item,0,stats));
            Assert.Contains(PlayerStatType.Engineering,BrotatoStatRules.Primary);
        }
        finally{Object.DestroyImmediate(go);Object.DestroyImmediate(character);}
    }
    /// <summary>攻速缩短整套动作，范围只延长回收；基础冷却不再随范围整体倍乘。</summary>
    [Test] public void MeleeTiming_SpeedRangeAndDisplayedCooldownShareOneContract()
    {
        var level=new WeaponLevelData{activeDuration=.25f,meleeRange=1.5f,cooldown=1f};
        var baseline=new MeleeAttackTiming(level.activeDuration,1.5f,1f);
        var fast=new MeleeAttackTiming(level.activeDuration,1.5f,.5f);
        var wide=new MeleeAttackTiming(level.activeDuration,3f,1f);
        Assert.Less(fast.Windup,baseline.Windup);Assert.Less(fast.Swing,baseline.Swing);Assert.Less(fast.Recovery,baseline.Recovery);
        Assert.AreEqual(baseline.Swing,wide.Swing);Assert.Greater(wide.Recovery,baseline.Recovery);
        Assert.That(MeleeAttackTiming.Interval(level,3f,1f),Is.InRange(1f,1.2f));
        Assert.GreaterOrEqual(MeleeAttackTiming.Interval(level,3f,.01f),new MeleeAttackTiming(.25f,3f,.01f).Total);
        var data=ScriptableObject.CreateInstance<WeaponDataSO>();
        try
        {
            data.runtimeType=WeaponRuntimeType.Melee;data.roundTierConfigs=new System.Collections.Generic.List<WeaponLevelData>{level};
            StringAssert.Contains(MeleeAttackTiming.Interval(level,level.meleeRange,1f).ToString("0.##")+"秒",RoundShopPresentation.WeaponDetails(data,1));
        }
        finally{Object.DestroyImmediate(data);}
    }
}
