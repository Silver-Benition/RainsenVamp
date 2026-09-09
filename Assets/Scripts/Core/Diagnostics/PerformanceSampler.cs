using System;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 主世界性能采样器。
/// 
/// 采样器在构造时一次性分配所有帧数组，并在每帧只写入数值和读取已经启动的
/// ProfilerRecorder。阶段结束后才排序并计算分位数，避免排序、字符串和文件 IO 进入
/// 游戏热路径。计数器不存在时保持 -1，并通过 support 标记说明不可用，而不是伪造 0。
/// </summary>
public sealed class PerformanceSampler : IDisposable
{
    private readonly float[] _frameMilliseconds;
    private readonly float[] _elapsedMilliseconds;
    private readonly float[] _mainThreadMilliseconds;
    private readonly float[] _renderThreadMilliseconds;
    private readonly float[] _gpuMilliseconds;
    private readonly float[] _rawGpuMilliseconds;
    private readonly float[] _gcAllocatedBytes;
    private readonly float[] _systemMemoryBytes;
    private readonly float[] _totalMemoryBytes;
    private readonly float[] _graphicsUsedMemoryBytes;
    private readonly float[] _textureMemoryBytes;
    private readonly float[] _batches;
    private readonly float[] _setPassCalls;
    private readonly float[] _drawCalls;
    private readonly int[] _activeProjectiles;
    private readonly int[] _activePickups;
    private readonly int[] _activeEnemies;
    private readonly int[] _visibleEnemyEstimates;
    private readonly float[] _sortScratch;

    private ProfilerRecorder _mainThreadRecorder;
    private ProfilerRecorder _renderThreadRecorder;
    private ProfilerRecorder _gpuRecorder;
    private ProfilerRecorder _gcAllocatedRecorder;
    private ProfilerRecorder _systemMemoryRecorder;
    private ProfilerRecorder _totalMemoryRecorder;
    private ProfilerRecorder _graphicsUsedMemoryRecorder;
    private ProfilerRecorder _textureMemoryRecorder;
    private ProfilerRecorder _batchesRecorder;
    private ProfilerRecorder _setPassCallsRecorder;
    private ProfilerRecorder _drawCallsRecorder;

    private MainWorldPerformanceCounterSupport _support;
    private MainWorldPerformanceCounterSupport _availableSupport;
    private int _frameCount;
    private int _droppedFrameCount;
    private int _lowFrequencyCount;
    private int _coveredTargetCount;
    private int _minimumActiveEnemies = int.MaxValue;
    private int _maximumActiveEnemies;
    private int _requestedTarget;
    private float _minimumCoverage;
    private float _stageElapsed;
    private bool _stageInstrumentationEnabled;
    private bool _countersActive;
    private bool _disposed;
    private int _gpuCounterAnomalyCount;
    private bool _gpuCounterInvalidated;
    private string _gpuCounterInvalidReason;

    // 60 秒是保守的异常判据，能够保留真实长帧而拒绝本次出现的 1e12 ms 级计数器溢出。
    private const float MaxReasonableGpuMilliseconds = 60000f;

    private bool _instrumentationControlActive;
    private double _instrumentationControlRemaining;
    private double _instrumentationControlFrameTotal;
    private double _instrumentationControlMillisecondsTotal;
    private int _instrumentationControlFrames;
    private double _instrumentationEnabledMillisecondsTotal;
    private int _instrumentationEnabledFrames;

    /// <summary>创建固定容量的采样器并启动当前 Unity 可用的性能计数器。</summary>
    public PerformanceSampler(int frameCapacity)
    {
        int safeCapacity = Mathf.Max(1024, frameCapacity);
        _frameMilliseconds = new float[safeCapacity];
        _elapsedMilliseconds = new float[safeCapacity];
        _mainThreadMilliseconds = new float[safeCapacity];
        _renderThreadMilliseconds = new float[safeCapacity];
        _gpuMilliseconds = new float[safeCapacity];
        _rawGpuMilliseconds = new float[safeCapacity];
        _gcAllocatedBytes = new float[safeCapacity];
        _systemMemoryBytes = new float[safeCapacity];
        _totalMemoryBytes = new float[safeCapacity];
        _graphicsUsedMemoryBytes = new float[safeCapacity];
        _textureMemoryBytes = new float[safeCapacity];
        _batches = new float[safeCapacity];
        _setPassCalls = new float[safeCapacity];
        _drawCalls = new float[safeCapacity];
        _activeProjectiles = new int[safeCapacity];
        _activePickups = new int[safeCapacity];
        _activeEnemies = new int[safeCapacity];
        _visibleEnemyEstimates = new int[safeCapacity];
        _sortScratch = new float[safeCapacity];

        StartCounters();
        _availableSupport = _support;
    }

    /// <summary>当前阶段是否开启计数器读取；关闭时仍记录基础帧时间用于 A/B 对照。</summary>
    public bool InstrumentationEnabled => _stageInstrumentationEnabled;

    /// <summary>当前阶段写入的帧样本数量。</summary>
    public int FrameCount => _frameCount;

    /// <summary>因固定预分配容量不足而丢弃的帧样本数量。</summary>
    public int DroppedFrameCount => _droppedFrameCount;

    /// <summary>只要发生过帧数组溢出，就不能把该阶段当作完整证据。</summary>
    public bool CapacityOverflowed => _droppedFrameCount > 0;

    /// <summary>当前阶段 GPU 计数器异常样本数量；异常值仍保留在 raw JSON 中。</summary>
    public int GpuCounterAnomalyCount => _gpuCounterAnomalyCount;

    /// <summary>当前阶段 GPU 计数器是否因物理上不合理的样本被判定不可用。</summary>
    public bool GpuCounterInvalidated => _gpuCounterInvalidated;

    /// <summary>当前阶段 GPU 计数器失效原因；没有异常时为空。</summary>
    public string GpuCounterInvalidReason => _gpuCounterInvalidReason;

    /// <summary>当前阶段低频对象计数样本数量。</summary>
    public int LowFrequencyCount => _lowFrequencyCount;

    /// <summary>控制采样器额外开销的无计数器平均帧时间。</summary>
    public float InstrumentationControlAverageMilliseconds => _instrumentationControlFrames > 0
        ? (float)(_instrumentationControlMillisecondsTotal / _instrumentationControlFrames)
        : MainWorldPerformanceCounterSupport.UnavailableValue;

    /// <summary>开启计数器后的平均帧时间，用于与控制窗口比较采样器开销。</summary>
    public float InstrumentationEnabledAverageMilliseconds => _instrumentationEnabledFrames > 0
        ? (float)(_instrumentationEnabledMillisecondsTotal / _instrumentationEnabledFrames)
        : MainWorldPerformanceCounterSupport.UnavailableValue;

    /// <summary>采样器计数器窗口和控制窗口之间的平均帧时间差。</summary>
    public float InstrumentationOverheadMilliseconds
    {
        get
        {
            if (_instrumentationControlFrames <= 0 || _instrumentationEnabledFrames <= 0)
            {
                return MainWorldPerformanceCounterSupport.UnavailableValue;
            }

            return InstrumentationEnabledAverageMilliseconds - InstrumentationControlAverageMilliseconds;
        }
    }

    /// <summary>开始一个新的阶段并清空上个阶段的帧和对象数量统计。</summary>
    public void BeginStage(
        MainWorldPerformanceStageKind stage,
        int requestedTargetActiveEnemies,
        float minimumCoverage,
        bool instrumentationEnabled)
    {
        if (_stageInstrumentationEnabled != instrumentationEnabled ||
            (!instrumentationEnabled && _countersActive) ||
            (instrumentationEnabled && !_countersActive))
        {
            SetInstrumentationEnabled(instrumentationEnabled);
        }

        _frameCount = 0;
        _droppedFrameCount = 0;
        _lowFrequencyCount = 0;
        _coveredTargetCount = 0;
        _minimumActiveEnemies = int.MaxValue;
        _maximumActiveEnemies = 0;
        _requestedTarget = Mathf.Max(0, requestedTargetActiveEnemies);
        _minimumCoverage = Mathf.Clamp01(minimumCoverage);
        _stageElapsed = 0f;
        _stageInstrumentationEnabled = instrumentationEnabled;
        _support = instrumentationEnabled
            ? _availableSupport
            : default(MainWorldPerformanceCounterSupport);
        _gpuCounterAnomalyCount = 0;
        _gpuCounterInvalidated = false;
        _gpuCounterInvalidReason = null;
    }

    /// <summary>开始不读取 ProfilerRecorder 的 A/B 控制窗口。</summary>
    public void BeginInstrumentationControl(float seconds)
    {
        _instrumentationControlActive = seconds > 0f;
        _instrumentationControlRemaining = Math.Max(0d, seconds);
        _instrumentationControlFrameTotal = 0d;
        _instrumentationControlMillisecondsTotal = 0d;
        _instrumentationControlFrames = 0;
        _instrumentationEnabledMillisecondsTotal = 0d;
        _instrumentationEnabledFrames = 0;
    }

    /// <summary>
    /// 切换后续阶段是否读取计数器。
    /// 关闭时释放全部 ProfilerRecorder 原生资源，使 A/B 对照只保留帧时和权威敌人数；
    /// 开启时重新探测当前平台可用计数器，不把关闭阶段的旧 support 状态带入报告。
    /// </summary>
    public void SetInstrumentationEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (enabled)
        {
            if (!_countersActive)
            {
                StartCounters();
            }

            _support = _availableSupport;
            _stageInstrumentationEnabled = true;
            _instrumentationControlActive = false;
            return;
        }

        if (_countersActive)
        {
            StopCounters();
        }

        _support = default(MainWorldPerformanceCounterSupport);
        _stageInstrumentationEnabled = false;
    }

    /// <summary>
    /// 记录当前帧的数值和低频对象计数。
    /// 
    /// activeEnemyCount 由 WorldEnemySimulation 的权威计数提供；visibleEnemyEstimate 仅
    /// 由低频相机范围查询得到，报告会将两者分开。数组达到容量上限后只停止新样本写入，
    /// 不会扩容或在热路径产生垃圾。
    /// </summary>
    public void RecordFrame(
        float unscaledDeltaTime,
        int activeEnemyCount,
        int activeProjectileCount,
        int activePickupCount,
        int visibleEnemyEstimate,
        bool lowFrequencySample)
    {
        if (_disposed)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, unscaledDeltaTime);
        float frameMilliseconds = safeDeltaTime * 1000f;
        _stageElapsed += safeDeltaTime;

        if (_instrumentationControlActive)
        {
            _instrumentationControlRemaining -= safeDeltaTime;
            _instrumentationControlFrameTotal += 1d;
            _instrumentationControlMillisecondsTotal += frameMilliseconds;
            _instrumentationControlFrames++;
            if (_instrumentationControlRemaining <= 0d)
            {
                _instrumentationControlActive = false;
            }
        }
        else if (_stageInstrumentationEnabled)
        {
            _instrumentationEnabledMillisecondsTotal += frameMilliseconds;
            _instrumentationEnabledFrames++;
        }

        if (lowFrequencySample)
        {
            _lowFrequencyCount++;
            if (_requestedTarget > 0 &&
                activeEnemyCount >= Mathf.CeilToInt(_requestedTarget * _minimumCoverage))
            {
                _coveredTargetCount++;
            }

            if (activeEnemyCount < _minimumActiveEnemies)
            {
                _minimumActiveEnemies = activeEnemyCount;
            }

            if (activeEnemyCount > _maximumActiveEnemies)
            {
                _maximumActiveEnemies = activeEnemyCount;
            }
        }

        if (_frameCount >= _frameMilliseconds.Length)
        {
            _droppedFrameCount++;
            return;
        }

        int index = _frameCount++;
        _frameMilliseconds[index] = frameMilliseconds;
        _elapsedMilliseconds[index] = _stageElapsed * 1000f;
        _activeEnemies[index] = Mathf.Max(0, activeEnemyCount);
        if (_stageInstrumentationEnabled)
        {
            _mainThreadMilliseconds[index] = ReadMilliseconds(_mainThreadRecorder, _support.mainThread);
            _renderThreadMilliseconds[index] = ReadMilliseconds(_renderThreadRecorder, _support.renderThread);
            float rawGpuMilliseconds = _gpuCounterInvalidated
                ? MainWorldPerformanceCounterSupport.UnavailableValue
                : ReadMilliseconds(_gpuRecorder, _support.gpu);
            _rawGpuMilliseconds[index] = rawGpuMilliseconds;
            _gpuMilliseconds[index] = ValidateGpuMilliseconds(rawGpuMilliseconds);
            _gcAllocatedBytes[index] = ReadValue(_gcAllocatedRecorder, _support.gcAllocatedInFrame);
            _systemMemoryBytes[index] = ReadValue(_systemMemoryRecorder, _support.systemUsedMemory);
            _totalMemoryBytes[index] = ReadValue(_totalMemoryRecorder, _support.totalUsedMemory);
            _graphicsUsedMemoryBytes[index] = ReadValue(_graphicsUsedMemoryRecorder, _support.graphicsUsedMemory);
            _textureMemoryBytes[index] = ReadValue(_textureMemoryRecorder, _support.textureMemory);
            _batches[index] = ReadValue(_batchesRecorder, _support.batches);
            _setPassCalls[index] = ReadValue(_setPassCallsRecorder, _support.setPassCalls);
            _drawCalls[index] = ReadValue(_drawCallsRecorder, _support.drawCalls);
        }
        else
        {
            // A/B 控制窗口不读取任何计数器，避免把“关闭采样”伪装成同等开销。
            _mainThreadMilliseconds[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _renderThreadMilliseconds[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _gpuMilliseconds[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _rawGpuMilliseconds[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _gcAllocatedBytes[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _systemMemoryBytes[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _totalMemoryBytes[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _graphicsUsedMemoryBytes[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _textureMemoryBytes[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _batches[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _setPassCalls[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
            _drawCalls[index] = MainWorldPerformanceCounterSupport.UnavailableValue;
        }
        _activeProjectiles[index] = Mathf.Max(0, activeProjectileCount);
        _activePickups[index] = Mathf.Max(0, activePickupCount);
        _visibleEnemyEstimates[index] = Mathf.Max(0, visibleEnemyEstimate);
    }

    /// <summary>计算当前阶段的统计快照；排序只发生在阶段边界，不影响持续采样。</summary>
    public MainWorldPerformanceFrameStatistics EndStage(out int minimumActiveEnemies, out int maximumActiveEnemies)
    {
        MainWorldPerformanceFrameStatistics statistics = new MainWorldPerformanceFrameStatistics
        {
            averageMilliseconds = Average(_frameMilliseconds, _frameCount, false),
            p95Milliseconds = Percentile(_frameMilliseconds, _frameCount, 0.95f),
            p99Milliseconds = Percentile(_frameMilliseconds, _frameCount, 0.99f),
            maximumMilliseconds = Maximum(_frameMilliseconds, _frameCount, false),
            ratioOver16Point67Milliseconds = RatioAbove(_frameMilliseconds, _frameCount, 16.67f),
            ratioOver33Point33Milliseconds = RatioAbove(_frameMilliseconds, _frameCount, 33.33f),
            ratioOver50Milliseconds = RatioAbove(_frameMilliseconds, _frameCount, 50f),
            averageMainThreadMilliseconds = Average(_mainThreadMilliseconds, _frameCount, true),
            averageRenderThreadMilliseconds = Average(_renderThreadMilliseconds, _frameCount, true),
            averageGpuMilliseconds = !_availableSupport.gpu || _gpuCounterInvalidated
                ? MainWorldPerformanceCounterSupport.UnavailableValue
                : Average(_gpuMilliseconds, _frameCount, true),
            averageGcAllocatedBytesPerFrame = Average(_gcAllocatedBytes, _frameCount, true),
            averageSystemUsedMemoryBytes = Average(_systemMemoryBytes, _frameCount, true),
            averageTotalUsedMemoryBytes = Average(_totalMemoryBytes, _frameCount, true),
            averageGraphicsUsedMemoryBytes = Average(_graphicsUsedMemoryBytes, _frameCount, true),
            averageTextureMemoryBytes = Average(_textureMemoryBytes, _frameCount, true),
            averageBatches = Average(_batches, _frameCount, true),
            averageSetPassCalls = Average(_setPassCalls, _frameCount, true),
            averageDrawCalls = Average(_drawCalls, _frameCount, true),
            firstSystemUsedMemoryBytes = First(_systemMemoryBytes, _frameCount, true),
            lastSystemUsedMemoryBytes = Last(_systemMemoryBytes, _frameCount, true),
            maximumActiveProjectiles = Maximum(_activeProjectiles, _frameCount),
            averageActiveProjectiles = Mathf.RoundToInt(Average(_activeProjectiles, _frameCount)),
            maximumActivePickups = Maximum(_activePickups, _frameCount),
            averageActivePickups = Mathf.RoundToInt(Average(_activePickups, _frameCount)),
            maximumVisibleEnemyEstimate = Maximum(_visibleEnemyEstimates, _frameCount),
            averageVisibleEnemyEstimate = Mathf.RoundToInt(Average(_visibleEnemyEstimates, _frameCount)),
            support = _support
        };

        statistics.systemUsedMemoryTrendBytes = statistics.lastSystemUsedMemoryBytes >= 0f &&
            statistics.firstSystemUsedMemoryBytes >= 0f
            ? statistics.lastSystemUsedMemoryBytes - statistics.firstSystemUsedMemoryBytes
            : MainWorldPerformanceCounterSupport.UnavailableValue;

        minimumActiveEnemies = _minimumActiveEnemies == int.MaxValue ? 0 : _minimumActiveEnemies;
        maximumActiveEnemies = _maximumActiveEnemies;
        return statistics;
    }

    /// <summary>返回当前低频样本中达到目标覆盖率的比例。</summary>
    public float GetTargetCoverageRatio()
    {
        if (_requestedTarget <= 0)
        {
            return 1f;
        }

        return _lowFrequencyCount > 0 ? (float)_coveredTargetCount / _lowFrequencyCount : 0f;
    }

    /// <summary>导出阶段原始样本，文件只在阶段边界生成，便于与外部录屏或 Profiler 时间轴对齐。</summary>
    public string CreateRawSamplesJson()
    {
        StringBuilder builder = new StringBuilder(Mathf.Max(256, _frameCount * 320));
        builder.Append('[');
        for (int index = 0; index < _frameCount; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append('{');
            builder.Append("\"index\":").Append(index);
            builder.Append(",\"elapsedMs\":").Append(_elapsedMilliseconds[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"frameMs\":").Append(_frameMilliseconds[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"mainMs\":").Append(_mainThreadMilliseconds[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"renderMs\":").Append(_renderThreadMilliseconds[index].ToString("R", CultureInfo.InvariantCulture));
            // gpuMs 保留计数器原始异常证据；gpuMsUsed 才是经过有效性筛选后用于聚合的值。
            builder.Append(",\"gpuMs\":");
            AppendJsonFloat(builder, _rawGpuMilliseconds[index]);
            builder.Append(",\"gpuMsUsed\":").Append(_gpuMilliseconds[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"gcBytes\":").Append(_gcAllocatedBytes[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"systemMemoryBytes\":").Append(_systemMemoryBytes[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"totalMemoryBytes\":").Append(_totalMemoryBytes[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"graphicsUsedMemoryBytes\":").Append(_graphicsUsedMemoryBytes[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"textureMemoryBytes\":").Append(_textureMemoryBytes[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"batches\":").Append(_batches[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"setPassCalls\":").Append(_setPassCalls[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"drawCalls\":").Append(_drawCalls[index].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"activeEnemies\":").Append(_activeEnemies[index]);
            builder.Append(",\"activeProjectiles\":").Append(_activeProjectiles[index]);
            builder.Append(",\"activePickups\":").Append(_activePickups[index]);
            builder.Append(",\"visibleEnemyEstimate\":").Append(_visibleEnemyEstimates[index]);
            builder.Append('}');
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>释放 ProfilerRecorder，允许场景和测试干净退出。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCounters();
    }

    /// <summary>启动可用的 Unity 计数器；不同平台缺少 GPU 或渲染计数器时保留 false。</summary>
    private void StartCounters()
    {
        StopCounters();
        _support.mainThread = TryStartCounter(ProfilerCategory.Internal, "Main Thread", out _mainThreadRecorder);
        _support.renderThread = TryStartCounter(ProfilerCategory.Internal, "Render Thread", out _renderThreadRecorder);
        _support.gpu = TryStartCounter(ProfilerCategory.Render, "GPU Frame Time", out _gpuRecorder);
        _support.gcAllocatedInFrame = TryStartCounter(ProfilerCategory.Memory, "GC Allocated In Frame", out _gcAllocatedRecorder);
        _support.systemUsedMemory = TryStartCounter(ProfilerCategory.Memory, "System Used Memory", out _systemMemoryRecorder);
        _support.totalUsedMemory = TryStartCounter(ProfilerCategory.Memory, "Total Used Memory", out _totalMemoryRecorder);
        _support.graphicsUsedMemory = TryStartCounter(ProfilerCategory.Memory, "Gfx Used Memory", out _graphicsUsedMemoryRecorder);
        _support.textureMemory = TryStartCounter(ProfilerCategory.Memory, "Texture Memory", out _textureMemoryRecorder);
        _support.batches = TryStartCounter(ProfilerCategory.Render, "Batches Count", out _batchesRecorder);
        _support.setPassCalls = TryStartCounter(ProfilerCategory.Render, "SetPass Calls Count", out _setPassCallsRecorder);
        _support.drawCalls = TryStartCounter(ProfilerCategory.Render, "Draw Calls Count", out _drawCallsRecorder);
        _availableSupport = _support;
        _countersActive = true;
    }

    /// <summary>停止全部计数器并把当前 support 清空；只在阶段切换或 Dispose 时调用。</summary>
    private void StopCounters()
    {
        DisposeRecorder(ref _mainThreadRecorder);
        DisposeRecorder(ref _renderThreadRecorder);
        DisposeRecorder(ref _gpuRecorder);
        DisposeRecorder(ref _gcAllocatedRecorder);
        DisposeRecorder(ref _systemMemoryRecorder);
        DisposeRecorder(ref _totalMemoryRecorder);
        DisposeRecorder(ref _graphicsUsedMemoryRecorder);
        DisposeRecorder(ref _textureMemoryRecorder);
        DisposeRecorder(ref _batchesRecorder);
        DisposeRecorder(ref _setPassCallsRecorder);
        DisposeRecorder(ref _drawCallsRecorder);
        _support = default(MainWorldPerformanceCounterSupport);
        _countersActive = false;
    }

    /// <summary>尝试启动单个计数器；平台没有该计数器时安全返回 false。</summary>
    private static bool TryStartCounter(ProfilerCategory category, string name, out ProfilerRecorder recorder)
    {
        recorder = default(ProfilerRecorder);
        try
        {
            recorder = ProfilerRecorder.StartNew(category, name, 15);
            return recorder.Valid;
        }
        catch (Exception)
        {
            recorder = default(ProfilerRecorder);
            return false;
        }
    }

    /// <summary>读取纳秒单位的线程或 GPU 计数并转换为毫秒。</summary>
    private static float ReadMilliseconds(ProfilerRecorder recorder, bool supported)
    {
        if (!supported || !recorder.Valid)
        {
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        if (recorder.Count <= 0) return MainWorldPerformanceCounterSupport.UnavailableValue;
        return recorder.LastValue / 1000000f;
    }

    /// <summary>拒绝明显不可能的 GPU 时间，避免异常计数器污染帧时和容量证据。</summary>
    private float ValidateGpuMilliseconds(float rawMilliseconds, bool allowSyntheticWithoutCounter = false)
    {
        if (rawMilliseconds == MainWorldPerformanceCounterSupport.UnavailableValue)
        {
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        if (rawMilliseconds < 0f)
        {
            _gpuCounterAnomalyCount++;
            _gpuCounterInvalidated = true;
            _gpuCounterInvalidReason = "gpu-counter-negative-value";
            _support.gpu = false;
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        if ((!_availableSupport.gpu && !allowSyntheticWithoutCounter))
        {
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        if (float.IsNaN(rawMilliseconds) || float.IsInfinity(rawMilliseconds) ||
            rawMilliseconds > MaxReasonableGpuMilliseconds)
        {
            _gpuCounterAnomalyCount++;
            _gpuCounterInvalidated = true;
            _gpuCounterInvalidReason = rawMilliseconds > MaxReasonableGpuMilliseconds
                ? "gpu-counter-outlier-over-60000ms"
                : "gpu-counter-invalid-number";
            _support.gpu = false;
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        return rawMilliseconds;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>仅供 EditMode 回归测试注入 GPU 样本，验证异常值隔离后下一阶段可重新采样。</summary>
    public void InjectGpuSampleForTests(float gpuMilliseconds)
    {
        if (_disposed || _frameCount >= _gpuMilliseconds.Length)
        {
            return;
        }

        int index = _frameCount++;
        _frameMilliseconds[index] = 1f;
        _elapsedMilliseconds[index] = ++_stageElapsed * 1000f;
        bool originalSupport = _support.gpu;
        if (!_availableSupport.gpu)
        {
            // 让测试覆盖验证逻辑，即使当前 EditMode 没有真实 GPU recorder。
            _support.gpu = true;
        }

        _rawGpuMilliseconds[index] = gpuMilliseconds;
        _gpuMilliseconds[index] = ValidateGpuMilliseconds(gpuMilliseconds, true);
        _support.gpu = _gpuCounterInvalidated ? false : originalSupport;
    }
#endif

    /// <summary>把有限浮点数写为 JSON 数字，非有限原始证据用字符串保持 JSON 合法。</summary>
    private static void AppendJsonFloat(StringBuilder builder, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            builder.Append('"').Append(value.ToString(CultureInfo.InvariantCulture)).Append('"');
            return;
        }

        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>读取计数器原始单位；不可用时返回 -1 而不是零。</summary>
    private static float ReadValue(ProfilerRecorder recorder, bool supported)
    {
        if (!supported || !recorder.Valid)
        {
            return MainWorldPerformanceCounterSupport.UnavailableValue;
        }

        if (recorder.Count <= 0) return MainWorldPerformanceCounterSupport.UnavailableValue;
        return recorder.LastValue;
    }

    /// <summary>计算整数数组均值，用于对象数量聚合而不产生装箱或临时集合。</summary>
    private static float Average(int[] values, int count)
    {
        if (count <= 0) return 0f;
        long total = 0L;
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++) total += values[index];
        return safeCount > 0 ? (float)total / safeCount : 0f;
    }

    /// <summary>计算支持计数器的均值。</summary>
    private static float Average(float[] values, int count, bool ignoreUnavailable)
    {
        if (count <= 0)
        {
            return ignoreUnavailable ? MainWorldPerformanceCounterSupport.UnavailableValue : 0f;
        }

        double total = 0d;
        int validCount = 0;
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++)
        {
            if (ignoreUnavailable && values[index] < 0f)
            {
                continue;
            }

            total += values[index];
            validCount++;
        }

        return validCount > 0 ? (float)(total / validCount) :
            (ignoreUnavailable ? MainWorldPerformanceCounterSupport.UnavailableValue : 0f);
    }

    /// <summary>计算浮点数组最大值；支持 -1 表示整个计数器不可用。</summary>
    private static float Maximum(float[] values, int count, bool ignoreUnavailable)
    {
        if (count <= 0)
        {
            return ignoreUnavailable ? MainWorldPerformanceCounterSupport.UnavailableValue : 0f;
        }

        float maximum = ignoreUnavailable ? MainWorldPerformanceCounterSupport.UnavailableValue : float.MinValue;
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++)
        {
            if (ignoreUnavailable && values[index] < 0f) continue;
            maximum = Mathf.Max(maximum, values[index]);
        }

        return maximum == float.MinValue ? MainWorldPerformanceCounterSupport.UnavailableValue : maximum;
    }

    /// <summary>计算整数数组最大值。</summary>
    private static int Maximum(int[] values, int count)
    {
        int maximum = 0;
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++) maximum = Mathf.Max(maximum, values[index]);
        return maximum;
    }

    /// <summary>计算大于指定阈值的帧比例。</summary>
    private static float RatioAbove(float[] values, int count, float threshold)
    {
        if (count <= 0) return 0f;
        int above = 0;
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++) if (values[index] > threshold) above++;
        return (float)above / safeCount;
    }

    /// <summary>计算支持计数器的首个有效样本。</summary>
    private static float First(float[] values, int count, bool ignoreUnavailable)
    {
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = 0; index < safeCount; index++)
        {
            if (!ignoreUnavailable || values[index] >= 0f) return values[index];
        }

        return MainWorldPerformanceCounterSupport.UnavailableValue;
    }

    /// <summary>计算支持计数器的最后一个有效样本。</summary>
    private static float Last(float[] values, int count, bool ignoreUnavailable)
    {
        int safeCount = Mathf.Min(count, values.Length);
        for (int index = safeCount - 1; index >= 0; index--)
        {
            if (!ignoreUnavailable || values[index] >= 0f) return values[index];
        }

        return MainWorldPerformanceCounterSupport.UnavailableValue;
    }

    /// <summary>在阶段边界复制有效帧时间并计算 nearest-rank 分位数。</summary>
    private float Percentile(float[] values, int count, float percentile)
    {
        if (count <= 0) return 0f;
        int safeCount = Mathf.Min(count, values.Length);
        Array.Copy(values, _sortScratch, safeCount);
        Array.Sort(_sortScratch, 0, safeCount);
        int index = Mathf.Clamp(Mathf.CeilToInt((safeCount - 1) * Mathf.Clamp01(percentile)), 0, safeCount - 1);
        return _sortScratch[index];
    }

    /// <summary>释放一个 ProfilerRecorder 的原生资源。</summary>
    private static void DisposeRecorder(ref ProfilerRecorder recorder)
    {
        if (recorder.Valid)
        {
            recorder.Dispose();
        }

        recorder = default(ProfilerRecorder);
    }
}
