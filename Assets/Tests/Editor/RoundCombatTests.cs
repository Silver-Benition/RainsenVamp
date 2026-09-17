using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>回合规则与独立钱包测试；覆盖边界语义而非复制生产判定公式。</summary>
public sealed class RoundCombatTests
{
    /// <summary>目标提前完成仍需达到最低存活时间。</summary>
    [Test]
    public void ObjectiveCompletedEarly_WaitsForMinimumTime()
    {
        var definition = new RoundDefinition { survivalSeconds = 60 };
        definition.objectives.Add(new RoundObjectiveDefinition { key = "kills", required = 3 });
        var round = new RoundRuntime(definition, 4);
        Assert.IsFalse(round.Report("kills", 10, 3));
        round.Report("kills", 3, 4); round.Tick(59);
        Assert.AreEqual(RoundEvaluation.Running, round.Evaluate(true));
        round.Tick(1);
        Assert.AreEqual(RoundEvaluation.Passed, round.Evaluate(true));
        Assert.AreEqual(RoundEvaluation.Failed, round.Evaluate(false));
    }

    /// <summary>最低时间未完成目标允许加时；硬截止到达才失败。</summary>
    [Test]
    public void MissingObjective_OvertimeAndDeadlineAreIndependent()
    {
        var definition = new RoundDefinition { survivalSeconds = 10, deadlineSeconds = 15 };
        definition.objectives.Add(new RoundObjectiveDefinition { key = "token", required = 1 });
        var round = new RoundRuntime(definition, 1);
        round.Tick(10); Assert.AreEqual(RoundEvaluation.Running, round.Evaluate(true));
        round.Tick(5); Assert.AreEqual(RoundEvaluation.Failed, round.Evaluate(true));
        round.Report("token", 1, 1);
        Assert.AreEqual(RoundEvaluation.Passed, round.Evaluate(true), "恰在截止完成目标时允许通过。");
    }

    /// <summary>通关关闭后迟到目标和时间不得改变快照。</summary>
    [Test]
    public void ClosedRound_RejectsDuplicateAndLateReports()
    {
        var round = new RoundRuntime(new RoundDefinition { survivalSeconds = 1 }, 9);
        round.Tick(1); Assert.IsTrue(round.TryClose(round.Evaluate(true)));
        Assert.IsFalse(round.TryClose(RoundEvaluation.Failed));
        Assert.IsFalse(round.Report("kills", 100, 9));
        round.Tick(99);
        Assert.AreEqual(1, round.Elapsed);
        Assert.AreEqual(RoundEvaluation.Passed, round.Evaluate(false));
    }

    /// <summary>仅明确配置的首领回合允许提前结束。</summary>
    [TestCase(false, RoundEvaluation.Running)]
    [TestCase(true, RoundEvaluation.Passed)]
    public void BossEarlyFinish_IsExplicit(bool early, RoundEvaluation expected)
    {
        var round = new RoundRuntime(new RoundDefinition { survivalSeconds = 90, allowEarlyBossVictory = early }, 1);
        round.Report("boss", 1, 1);
        Assert.AreEqual(expected, round.Evaluate(true));
    }

    /// <summary>任意目标模式仍需时间门槛，同时全部目标模式必须全部完成。</summary>
    [TestCase(false, RoundEvaluation.Passed)]
    [TestCase(true, RoundEvaluation.Running)]
    public void ObjectiveComposition_IsConfigurable(bool all, RoundEvaluation expected)
    {
        var data = new RoundDefinition { survivalSeconds = 10, requireAllObjectives = all };
        data.objectives.Add(new RoundObjectiveDefinition { key = "a" });
        data.objectives.Add(new RoundObjectiveDefinition { key = "b" });
        var round = new RoundRuntime(data, 1); round.Report("a", 1, 1); round.Tick(10);
        Assert.AreEqual(expected, round.Evaluate(true));
    }

    /// <summary>储备不能在当轮消费，后续拾取才兑付；回收不动用储备。</summary>
    [Test]
    public void Materials_BaggedValueDoesNotBecomeImmediateMoney()
    {
        var wallet = new RunMaterialWallet();
        wallet.Bag(8);
        Assert.AreEqual(0, wallet.Balance);
        Assert.IsFalse(wallet.TrySpend(1));
        Assert.AreEqual(6, wallet.Collect(3));
        Assert.AreEqual(5, wallet.Bagged);
        Assert.IsTrue(wallet.TrySpend(4));
        wallet.Credit(2);
        Assert.AreEqual(4, wallet.Balance);
        Assert.AreEqual(5, wallet.Bagged);
        Assert.AreEqual(4, wallet.Spent);
    }

    /// <summary>非法输入与整数边界不会产生负余额或凭空消费。</summary>
    [Test]
    public void Materials_InvalidAndOverflowInputsStaySafe()
    {
        var wallet = new RunMaterialWallet();
        Assert.IsFalse(wallet.TrySpend(-1)); wallet.Bag(-1);
        Assert.AreEqual(0, wallet.Collect(-1));
        wallet.Credit(int.MaxValue); wallet.Credit(1);
        Assert.AreEqual(int.MaxValue, wallet.Balance);
        Assert.IsTrue(wallet.TrySpend(int.MaxValue));
        Assert.AreEqual(0, wallet.Balance);
    }

    /// <summary>正式二十回合表与四档装备资产完整，商店封印映射 ID 不丢失。</summary>
    [Test]
    public void ProductionConfiguration_HasTwentyRoundsAndFourWeaponTiers()
    {
        RoundRunConfigSO data = AssetDatabase.LoadAssetAtPath<RoundRunConfigSO>(RoundCombatSetup.ConfigPath);
        Assert.IsNotNull(data); Assert.IsTrue(data.Validate(out string error), error);
        Assert.AreEqual(20, data.rounds.Count);
        Assert.AreEqual(20, data.rounds[0].survivalSeconds);
        Assert.AreEqual(55, data.rounds[7].survivalSeconds);
        Assert.AreEqual(60, data.rounds[18].survivalSeconds);
        Assert.AreEqual(90, data.rounds[19].survivalSeconds);
        Assert.IsTrue(data.rounds[19].spawnBoss);
        foreach (RunShopProduct product in data.shopCatalog.products)
            if (product.IsWeapon) Assert.AreEqual(4, product.content.weaponToGrant.roundTierConfigs.Count);
    }
}
