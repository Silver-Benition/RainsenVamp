using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>F7 的批量操作必须有常数上限，并通过正式持有容器遵守道具限制。</summary>
    public sealed class ItemDebugPanelTests
    {
        /// <summary>即便传入 int.MaxValue，也只获得五件；限量、唯一和非道具输入均有边界保护。</summary>
        [Test] public void GrantsAreBoundedAndRespectItemLimits()
        {
            var player=new GameObject("DebugItemTestPlayer");
            var manager=player.AddComponent<AbilityManager>();
            var view=new GameObject("DebugItemTestPanel");
            var panel=view.AddComponent<AbilityDebugPanel>();
            typeof(AbilityDebugPanel).GetField("_abilityManager",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,manager);
            var unlimited=Item("unlimited",0);var limited=Item("limited",3);var unique=Item("unique",1);
            var legacy=Item("legacy_ability",0);legacy.presentationCategory=AbilityPresentationCategory.Ability;
            try
            {
                foreach(var item in new[]{unlimited,limited,unique,legacy}) Call(panel,"AddItem",item);
                Assert.AreEqual(5,Call(panel,"GrantItems",unlimited,int.MaxValue));
                Assert.AreEqual(5,manager.GetOwnedAbility(unlimited).CurrentLevel);
                Assert.AreEqual(0,Call(panel,"GrantItems",unlimited,0));
                Assert.AreEqual(3,Call(panel,"GrantItems",limited,5));
                Assert.AreEqual(0,Call(panel,"GrantItems",limited,1));
                Assert.AreEqual(1,Call(panel,"GrantItems",unique,5));
                Assert.AreEqual(0,Call(panel,"GrantItems",unique,1));
                Assert.AreEqual(0,Call(panel,"GrantItems",legacy,1));
                Assert.IsNull(manager.GetOwnedAbility(legacy));
            }
            finally
            {
                Object.DestroyImmediate(view);Object.DestroyImmediate(player);
                foreach(var item in new[]{unlimited,limited,unique,legacy}) Object.DestroyImmediate(item);
            }
        }

        /// <summary>创建具有唯一 ID 的独立测试道具，不修改正式共享资产。</summary>
        private static AbilityDataSO Item(string id,int limit)
        {
            var item=ScriptableObject.CreateInstance<AbilityDataSO>();item.abilityID=id;
            item.stackPerCopy=true;item.maxCopies=limit;item.presentationCategory=AbilityPresentationCategory.Item;
            item.levelConfigs[0].statModifiers.Add(new PlayerStatModifier(PlayerStatType.MaxHealth,PlayerStatModifierMode.Flat,1));
            return item;
        }

        /// <summary>调用与实际界面按钮共用的非公开操作入口。</summary>
        private static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
    }
}
