/// <summary>一把武器的波次伤害账本；攻击快照携带代次，延迟命中不会写入下一波。</summary>
public sealed class WeaponWaveDamage
{
    private bool _recording;
    private int _token, _wave;
    private double _current;
    public int Token => _token;
    public double CurrentDamage => _current;
    public double LastWaveDamage { get; private set; }
    public int LastWaveNumber { get; private set; }

    /// <summary>仅在回合开始调用；清空本波并使所有旧攻击快照失效，保留上一波展示值。</summary>
    public void Begin(int wave)
    {
        _token = _token == int.MaxValue ? 1 : _token + 1;
        _wave = wave; _current = 0; _recording = true;
    }

    /// <summary>记录目标已接受的完整命中值（含过量伤害）；拒绝、旧代次和结算期请求不入账。</summary>
    public void Record(int token, CombatDamageResult result)
    {
        if (_recording && token == _token && result.Accepted && result.AppliedDamage > 0)
            _current += result.AppliedDamage;
    }

    /// <summary>在关闭战斗时固定上一波数值；重复关闭不会覆盖历史。</summary>
    public void Complete()
    {
        if (!_recording) return;
        LastWaveDamage = _current; LastWaveNumber = _wave; _recording = false;
    }

    /// <summary>局间合成保留两把材料武器的上一波贡献，下一波重新独立计数。</summary>
    public void MergePrevious(WeaponWaveDamage other)
    {
        if (_recording || other == null || other._recording || other == this) return;
        if (LastWaveNumber < other.LastWaveNumber)
        { LastWaveNumber = other.LastWaveNumber; LastWaveDamage = other.LastWaveDamage; }
        else if (LastWaveNumber == other.LastWaveNumber) LastWaveDamage += other.LastWaveDamage;
    }
}
