using System;
using System.Collections.Generic;

/// <summary>回合结束评估结果；由流程权威统一消费。</summary>
public enum RoundEvaluation { Running, Passed, Failed }

/// <summary>单回合独立状态；事件携带代次，拒绝前回合迟到报告。</summary>
public sealed class RoundRuntime
{
    private readonly Dictionary<string, int> _progress = new Dictionary<string, int>(StringComparer.Ordinal);
    public RoundDefinition Definition { get; }
    public int Generation { get; }
    public float Elapsed { get; private set; }
    public bool Closed { get; private set; }
    public RoundEvaluation Result { get; private set; }
    public float Remaining => Math.Max(0, Definition.survivalSeconds - Elapsed);

    /// <summary>建立新回合；每次使用独立字典，不修改 SO。</summary>
    public RoundRuntime(RoundDefinition definition, int generation)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Generation = generation;
    }

    /// <summary>推进有效战斗时间；暂停期间调用者传零或不调用。</summary>
    public void Tick(float seconds)
    {
        if (!Closed && seconds > 0 && !float.IsNaN(seconds) && !float.IsInfinity(seconds))
            Elapsed = Math.Min(float.MaxValue, (double)Elapsed + seconds) >= float.MaxValue
                ? float.MaxValue : Elapsed + seconds;
    }

    /// <summary>累计目标事件；关闭、无效数量和错误代次不会改变结果。</summary>
    public bool Report(string key, int amount, int generation)
    {
        if (Closed || generation != Generation || string.IsNullOrWhiteSpace(key) || amount <= 0) return false;
        _progress[key] = (int)Math.Min(int.MaxValue, (long)GetProgress(key) + amount);
        return true;
    }

    /// <summary>读取目标进度；尚无事件时返回零。</summary>
    public int GetProgress(string key) => key != null && _progress.TryGetValue(key, out int value) ? value : 0;

    /// <summary>先检查存活，再检查提前首领胜利、最低时间与目标，最后检查硬截止。</summary>
    public RoundEvaluation Evaluate(bool alive)
    {
        if (Closed) return Result;
        if (!alive) return RoundEvaluation.Failed;
        if (Definition.allowEarlyBossVictory && GetProgress("boss") > 0) return RoundEvaluation.Passed;
        bool goals = Definition.objectives.Count == 0 || Definition.requireAllObjectives;
        foreach (RoundObjectiveDefinition objective in Definition.objectives)
        {
            bool met = GetProgress(objective.key) >= objective.required;
            goals = Definition.requireAllObjectives ? goals && met : goals || met;
        }
        if (Elapsed >= Definition.survivalSeconds && goals) return RoundEvaluation.Passed;
        return Definition.deadlineSeconds > 0 && Elapsed >= Definition.deadlineSeconds
            ? RoundEvaluation.Failed : RoundEvaluation.Running;
    }

    /// <summary>原子关闭一个终态；重复调用不会改变已确定结果。</summary>
    public bool TryClose(RoundEvaluation result)
    {
        if (Closed || result == RoundEvaluation.Running) return false;
        Closed = true;
        Result = result;
        return true;
    }
}

/// <summary>局内材料钱包；获得、消费、储备分别计数，完全不访问账号存档。</summary>
public sealed class RunMaterialWallet
{
    public int Balance { get; private set; }
    public int Earned { get; private set; }
    public int Spent { get; private set; }
    public int Bagged { get; private set; }
    public event Action Changed;

    /// <summary>拾取材料时兑付等量储备，返回实际到账量供经验系统使用。</summary>
    public int Collect(int amount)
    {
        if (amount <= 0) return 0;
        int bonus = Math.Min(amount, Bagged);
        int credited = (int)Math.Min(int.MaxValue - (long)Balance, (long)amount + bonus);
        Bagged -= Math.Min(bonus, Math.Max(0, credited - amount));
        Balance += credited;
        Earned = (int)Math.Min(int.MaxValue, (long)Earned + credited);
        Changed?.Invoke();
        return credited;
    }

    /// <summary>回收或奖励只发货币，不额外生成经验或动用材料储备。</summary>
    public void Credit(int amount)
    {
        if (amount <= 0) return;
        int credited = Math.Min(amount, int.MaxValue - Balance);
        Balance += credited;
        Earned = (int)Math.Min(int.MaxValue, (long)Earned + credited);
        Changed?.Invoke();
    }

    /// <summary>把地面未拾取价值转为后续掉落储备，不增加当轮购买余额。</summary>
    public void Bag(int amount)
    {
        if (amount <= 0) return;
        Bagged = (int)Math.Min(int.MaxValue, (long)Bagged + amount);
        Changed?.Invoke();
    }

    /// <summary>检查余额后扣款；交易层负责先校验商品和装备容量。</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Balance) return false;
        Balance -= amount;
        Spent = (int)Math.Min(int.MaxValue, (long)Spent + amount);
        Changed?.Invoke();
        return true;
    }
}
