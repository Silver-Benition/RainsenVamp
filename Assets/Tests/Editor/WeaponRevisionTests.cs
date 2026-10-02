using NUnit.Framework;
using UnityEngine;
using System.Reflection;

/// <summary>本轮反馈的独立属性契约回归，防止 F9 再次用显示行号写入旧属性。</summary>
public sealed class WeaponRevisionTests
{
    /// <summary>正式新属性全部可调，旧属性被拒绝；清除只影响调试来源，支持负点数。</summary>
    [Test] public void F9_NewStatGroups_ApplySignedPoints_AndClearOnlyDebugSource()
    {
        var player = new GameObject("DebugPlayer");
        var panelObject = new GameObject("DebugPanel");
        var data = ScriptableObject.CreateInstance<CharacterDataSO>();
        try
        {
            data.useBrotatoStats = true;
            var stats = player.AddComponent<PlayerStats>(); stats.SetCharacterData(data);
            stats.SetModifiers("fixture.item", new[]{new PlayerStatModifier(PlayerStatType.Range, PlayerStatModifierMode.Flat, 40f)});
            var panel = panelObject.AddComponent<PlayerAttributeDebugPanel>();
            typeof(PlayerAttributeDebugPanel).GetField("_playerStats",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,stats);
            foreach (var group in new[]{BrotatoStatRules.Primary,BrotatoStatRules.Secondary,BrotatoStatRules.Resources})
            foreach (var stat in group) Assert.IsTrue(panel.DebugSetModifier(stat,PlayerStatModifierMode.Flat,25f),stat.ToString());
            Assert.AreEqual(65f,stats.GetFinalStat(PlayerStatType.Range));
            Assert.AreEqual(25f,stats.GetFinalStat(PlayerStatType.AttackSpeed));
            Assert.AreEqual(25f,stats.GetFinalStat(PlayerStatType.DamagePercent));
            Assert.IsFalse(panel.DebugSetModifier(PlayerStatType.Area,PlayerStatModifierMode.Flat,5f));
            Assert.IsTrue(panel.DebugSetModifier(PlayerStatType.Engineering,PlayerStatModifierMode.Flat,5f));
            Assert.IsFalse(panel.DebugSetModifier(PlayerStatType.Range,PlayerStatModifierMode.Flat,float.NaN));
            Assert.IsTrue(panel.DebugSetModifier(PlayerStatType.Range,PlayerStatModifierMode.Flat,-60f));
            Assert.AreEqual(-20f,stats.GetFinalStat(PlayerStatType.Range));
            Assert.IsTrue(panel.DebugClearModifiers());
            Assert.AreEqual(40f,stats.GetFinalStat(PlayerStatType.Range));
            Assert.AreEqual(0f,stats.GetFinalStat(PlayerStatType.AttackSpeed));
        }
        finally { Object.DestroyImmediate(panelObject); Object.DestroyImmediate(player); Object.DestroyImmediate(data); }
    }
}
