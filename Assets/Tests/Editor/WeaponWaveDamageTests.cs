using NUnit.Framework;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>独立账本验证实例隔离、过量命中、跨波旧攻击与合成保留。</summary>
    public sealed class WeaponWaveDamageTests
    {
        /// <summary>同种武器也必须分账；无效命中不计数，上一波值在下一波期间保持不变。</summary>
        [Test] public void WaveLedger_IsolatesInstancesAndRejectsLateReceipts()
        {
            var a = new WeaponWaveDamage(); var b = new WeaponWaveDamage();
            a.Begin(1); b.Begin(1); int old = a.Token;
            var hit = new CombatDamageResult(150, 150, 10, true, true);
            a.Record(old, hit); b.Record(b.Token, new CombatDamageResult(7, 7, 7, true, false));
            a.Record(old, new CombatDamageResult(99, 0, 0, false, false));
            a.Complete(); b.Complete(); a.Complete();
            Assert.AreEqual(150, a.LastWaveDamage); Assert.AreEqual(7, b.LastWaveDamage);
            a.Record(old, hit); Assert.AreEqual(150, a.LastWaveDamage);
            a.Begin(2); a.Record(old, hit); Assert.AreEqual(0, a.CurrentDamage);
            Assert.AreEqual(150, a.LastWaveDamage);
            a.Complete(); Assert.AreEqual(0, a.LastWaveDamage); Assert.AreEqual(2, a.LastWaveNumber);
        }

        /// <summary>同波合成保留材料武器贡献；新买武器合入老武器也不丢历史。</summary>
        [Test] public void Merge_PreservesPreviousWaveWithoutRecountingCurrentWave()
        {
            var a = new WeaponWaveDamage(); var b = new WeaponWaveDamage(); var fresh = new WeaponWaveDamage();
            a.Begin(3); b.Begin(3);
            a.Record(a.Token, new CombatDamageResult(20, 20, 20, true, false));
            b.Record(b.Token, new CombatDamageResult(30, 30, 30, true, false));
            a.Complete(); b.Complete(); a.MergePrevious(b); fresh.MergePrevious(a);
            Assert.AreEqual(50, fresh.LastWaveDamage); Assert.AreEqual(3, fresh.LastWaveNumber);
            fresh.Begin(4); fresh.MergePrevious(a); fresh.Complete(); Assert.AreEqual(0, fresh.LastWaveDamage);
        }

        /// <summary>正式结算入口的直接命中和持续伤害共用同一实例账本，不改变伤害或暴击规则。</summary>
        [Test] public void Snapshot_AttributesAcceptedDirectAndPeriodicDamage()
        {
            var go = new GameObject("DamageLedgerTarget"); go.SetActive(false);
            go.AddComponent<Rigidbody2D>().gravityScale = 0; go.AddComponent<BoxCollider2D>();
            EnemyBase enemy = go.AddComponent<EnemyBase>();
            try
            {
                go.SetActive(true); enemy.ApplySpawnSnapshot(new EnemySpawnSnapshot(100, 0, 0, 1, false));
                var ledger = new WeaponWaveDamage(); ledger.Begin(1);
                var hit = new WeaponHitSnapshot(null, null, null, ledger);
                hit.Apply(enemy, 9, null); hit.ApplyDamageOverTime(enemy, 4, null);
                Assert.AreEqual(87, enemy.CurrentHealth); Assert.AreEqual(13, ledger.CurrentDamage);
                go.SetActive(false); hit.Apply(enemy, 8, null); Assert.AreEqual(13, ledger.CurrentDamage);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
