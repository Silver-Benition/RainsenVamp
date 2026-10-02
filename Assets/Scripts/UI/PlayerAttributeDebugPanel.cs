#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 编辑器与 Development Build 专用的玩家属性调试面板。
/// F9 可在运行或暂停时开关；所有修改使用独立稳定来源，不写回 CharacterDataSO。
/// </summary>
public sealed class PlayerAttributeDebugPanel : MonoBehaviour
{
    private const string DebugSourceId = "debug.player_attributes";
    private const KeyCode ToggleKey = KeyCode.F9;
    private const float WindowWidth = 780f;
    private const float WindowHeight = 780f;

    private readonly PlayerStatModifierMode[] _modifierModes =
        new PlayerStatModifierMode[BrotatoStatRules.StatCount];
    private readonly float[] _modifierValues = new float[BrotatoStatRules.StatCount];
    private readonly string[] _modifierInputs = new string[BrotatoStatRules.StatCount];
    private readonly bool[] _modifierActive = new bool[BrotatoStatRules.StatCount];

    private PlayerStats _playerStats;
    private Rect _windowRect = new Rect(20f, 20f, WindowWidth, WindowHeight);
    private Vector2 _scrollPosition;
    private bool _visible;
    private bool _editorStateInitialized;
    private string _statusMessage = "F9 toggles this panel.";

    /// <summary>自动创建跨场景调试面板；正式非 Development Build 不会编译此类型。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimePanel()
    {
        if (FindObjectOfType<PlayerAttributeDebugPanel>() != null)
        {
            return;
        }

        GameObject panelObject = new GameObject(nameof(PlayerAttributeDebugPanel));
        panelObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(panelObject);
        panelObject.AddComponent<PlayerAttributeDebugPanel>();
    }

    /// <summary>初始化调试输入并解析当前玩家。</summary>
    private void Awake()
    {
        InitializeEditorState();
        ResolvePlayerStats(false);
    }

    /// <summary>监听 F9；时间缩放为零时 Update 仍会执行，因此暂停菜单内同样可用。</summary>
    private void Update()
    {
        if (!Input.GetKeyDown(ToggleKey))
        {
            return;
        }

        _visible = !_visible;
        if (_visible)
        {
            ResolvePlayerStats(true);
        }
    }

    /// <summary>仅面板打开时绘制，暂停状态仍可操作。</summary>
    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        _windowRect.width = Mathf.Min(WindowWidth, Mathf.Max(1f, Screen.width - 20f));
        _windowRect.height = Mathf.Min(WindowHeight, Mathf.Max(1f, Screen.height - 20f));
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
        _windowRect = GUI.Window(
            GetInstanceID(),
            _windowRect,
            DrawWindow,
            Text("title", "玩家属性调试 (F9)"));
    }

    /// <summary>
    /// 供自动化或其他调试代码立即设置单项修改；多次调用会保留此前调试项并整体替换同一来源。
    /// </summary>
    public bool DebugSetModifier(
        PlayerStatType statType,
        PlayerStatModifierMode mode,
        float value)
    {
        InitializeEditorState();
        ResolvePlayerStats(false);
        int index = (int)statType;
        if (_playerStats == null || index < 0 || index >= _modifierActive.Length ||
            float.IsNaN(value) || float.IsInfinity(value) || !IsEditableStat(statType))
        {
            return false;
        }

        _modifierModes[index] = mode;
        _modifierValues[index] = value;
        _modifierInputs[index] = PlayerStatPresentation.FormatRawValue(value);
        _modifierActive[index] = true;
        ApplyEditorState();
        _statusMessage = $"Applied {statType}: {mode} {value:0.###}";
        return true;
    }

    /// <summary>移除全部调试属性并恢复能力、角色等正式来源决定的最终值。</summary>
    public bool DebugClearModifiers()
    {
        InitializeEditorState();
        ResolvePlayerStats(false);
        if (_playerStats == null)
        {
            return false;
        }

        ResetLocalModifiers();
        _playerStats.RemoveModifiers(DebugSourceId);
        _statusMessage = "Cleared all debug modifiers.";
        return true;
    }

    /// <summary>绘制当前规则对应的属性分组和操作栏。</summary>
    private void DrawWindow(int windowId)
    {
        GUILayout.Space(4f);
        GUILayout.Label(
            Text("units", "平加按属性点输入：伤害/攻速输入 25 即 +25%。比例加成输入 0.25 即增加当前值的 25%；乘算输入 1.25。"));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Text("refresh", "刷新玩家"), GUILayout.Width(120f)))
        {
            ResolvePlayerStats(true);
        }

        bool previousEnabled = GUI.enabled;
        GUI.enabled = _playerStats != null;
        if (GUILayout.Button(Text("apply_all", "应用全部"), GUILayout.Width(100f)))
        {
            TryApplyAllInputs();
        }

        if (GUILayout.Button(Text("clear_all", "清除全部"), GUILayout.Width(100f)))
        {
            DebugClearModifiers();
        }
        GUI.enabled = previousEnabled;

        GUILayout.Label(_statusMessage);
        GUILayout.EndHorizontal();

        if (_playerStats == null)
        {
            GUILayout.Space(12f);
            GUILayout.Label("PlayerStats was not found in the active scene.");
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
            return;
        }

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        if (_playerStats.UsesBrotatoStats)
        {
            DrawGroup("primary", "核心属性", BrotatoStatRules.Primary);
            DrawGroup("secondary", "次要属性", BrotatoStatRules.Secondary);
            DrawGroup("resources", "特殊资源", BrotatoStatRules.Resources);
        }
        else
        {
            for (int index = 0; index < PlayerStatPresentation.StatCount; index++) DrawStatRow(index);
        }
        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
    }

    /// <summary>按正式属性表分组绘制，数组以枚举编号索引，不把显示行号当作属性编号。</summary>
    private void DrawGroup(string key, string fallback, PlayerStatType[] stats)
    {
        GUILayout.Label(Text(key, fallback));
        foreach (PlayerStatType stat in stats) DrawStatRow((int)stat);
    }

    /// <summary>新模式只允许正式属性；旧角色调试仍保留原有表的语义。</summary>
    public bool IsEditableStat(PlayerStatType stat)
    {
        return _playerStats != null && (_playerStats.UsesBrotatoStats
            ? BrotatoStatRules.IsAvailable(stat)
            : (int)stat >= 0 && (int)stat < PlayerStatPresentation.StatCount);
    }

    /// <summary>通过项目统一解析入口获取调试文案，缺少译文时使用中文。</summary>
    private static string Text(string key, string fallback)
    {
        string result = PlayerStatPresentation.ResolveText?.Invoke("debug.attributes." + key, fallback);
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    /// <summary>绘制最终值与独立调试修改器，不写回共享角色配置。</summary>
    private void DrawStatRow(int index)
    {
        PlayerStatType statType = (PlayerStatType)index;
        GUILayout.BeginHorizontal("box");
        GUILayout.Label(
            $"{PlayerStatPresentation.GetDisplayName(statType)} ({statType})",
            GUILayout.Width(215f));
        GUILayout.Label(
            PlayerStatPresentation.FormatFinalValue(
                statType,
                _playerStats.GetFinalStat(statType)),
            GUILayout.Width(80f));

        if (GUILayout.Button(GetModeLabel(_modifierModes[index]), GUILayout.Width(82f)))
        {
            _modifierModes[index] = GetNextMode(_modifierModes[index]);
            _modifierInputs[index] = GetNeutralInput(_modifierModes[index]);
            _modifierValues[index] = GetNeutralValue(_modifierModes[index]);
            _modifierActive[index] = false;
            ApplyEditorState();
        }

        string editedInput = GUILayout.TextField(_modifierInputs[index], GUILayout.Width(105f));
        if (editedInput != _modifierInputs[index])
        {
            _modifierInputs[index] = editedInput;
            _modifierActive[index] = true;
        }

        if (GUILayout.Button(Text("apply", "应用"), GUILayout.Width(70f)))
        {
            TryApplyRow(index);
        }

        bool rowButtonEnabled = GUI.enabled;
        GUI.enabled = _modifierActive[index];
        if (GUILayout.Button(Text("clear", "清除"), GUILayout.Width(65f)))
        {
            ClearRow(index);
        }
        GUI.enabled = rowButtonEnabled;
        GUILayout.EndHorizontal();
    }

    /// <summary>验证并应用单项有限数值。</summary>
    private void TryApplyRow(int index)
    {
        if (!TryParseModifierValue(_modifierInputs[index], out float value))
        {
            _statusMessage = $"Invalid value for {(PlayerStatType)index}.";
            return;
        }

        _modifierValues[index] = value;
        _modifierInputs[index] = PlayerStatPresentation.FormatRawValue(value);
        _modifierActive[index] = true;
        ApplyEditorState();
        _statusMessage = $"Applied {(PlayerStatType)index}.";
    }

    /// <summary>验证活动输入后提交当前修改器。</summary>
    private void TryApplyAllInputs()
    {
        for (int index = 0; index < _modifierActive.Length; index++)
        {
            if (!_modifierActive[index] || !IsEditableStat((PlayerStatType)index))
            {
                continue;
            }

            if (!TryParseModifierValue(_modifierInputs[index], out float value))
            {
                _statusMessage = $"Invalid value for {(PlayerStatType)index}.";
                return;
            }

            _modifierValues[index] = value;
            _modifierInputs[index] = PlayerStatPresentation.FormatRawValue(value);
        }

        ApplyEditorState();
        _statusMessage = "Applied all active debug modifiers.";
    }

    /// <summary>仅清除指定调试项。</summary>
    private void ClearRow(int index)
    {
        _modifierActive[index] = false;
        _modifierValues[index] = GetNeutralValue(_modifierModes[index]);
        _modifierInputs[index] = GetNeutralInput(_modifierModes[index]);
        ApplyEditorState();
        _statusMessage = $"Cleared {(PlayerStatType)index}.";
    }

    /// <summary>把全部活动调试项作为一个来源提交，确保升级或清除不会遗留旧加成。</summary>
    private void ApplyEditorState()
    {
        if (_playerStats == null)
        {
            return;
        }

        var modifiers = new List<PlayerStatModifier>(BrotatoStatRules.StatCount);
        for (int index = 0; index < _modifierActive.Length; index++)
        {
            if (!_modifierActive[index] || !IsEditableStat((PlayerStatType)index))
            {
                continue;
            }

            modifiers.Add(new PlayerStatModifier(
                (PlayerStatType)index,
                _modifierModes[index],
                _modifierValues[index]));
        }

        if (modifiers.Count == 0)
        {
            _playerStats.RemoveModifiers(DebugSourceId);
            return;
        }

        _playerStats.SetModifiers(DebugSourceId, modifiers);
    }

    /// <summary>重连玩家时清空旧输入，防止跨场景带入另一玩家。</summary>
    private void ResolvePlayerStats(bool resetWhenTargetChanges)
    {
        if (!resetWhenTargetChanges && _playerStats != null)
        {
            return;
        }

        PlayerStats resolvedStats = null;
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            resolvedStats = player.GetComponent<PlayerStats>();
        }

        if (resolvedStats == null)
        {
            resolvedStats = FindObjectOfType<PlayerStats>();
        }

        if (resolvedStats == _playerStats)
        {
            return;
        }

        _playerStats = resolvedStats;
        ResetLocalModifiers();

        _statusMessage = _playerStats != null
            ? $"Player resolved: {_playerStats.gameObject.name}"
            : "PlayerStats was not found.";
    }

    /// <summary>仅初始化一次输入缓存。</summary>
    private void InitializeEditorState()
    {
        if (_editorStateInitialized)
        {
            return;
        }

        _editorStateInitialized = true;
        ResetLocalModifiers();
    }

    /// <summary>恢复全部调试项的中性输入。</summary>
    private void ResetLocalModifiers()
    {
        for (int index = 0; index < _modifierActive.Length; index++)
        {
            _modifierModes[index] = PlayerStatModifierMode.Flat;
            _modifierValues[index] = 0f;
            _modifierInputs[index] = "0";
            _modifierActive[index] = false;
        }
    }

    /// <summary>兼容本地与固定小数格式，拒绝非有限数值。</summary>
    private static bool TryParseModifierValue(string input, out float value)
    {
        bool parsed = float.TryParse(
            input,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) || float.TryParse(input, out value);
        return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>循环选择三种修改方式。</summary>
    private static PlayerStatModifierMode GetNextMode(PlayerStatModifierMode mode)
    {
        switch (mode)
        {
            case PlayerStatModifierMode.Flat:
                return PlayerStatModifierMode.AdditivePercent;
            case PlayerStatModifierMode.AdditivePercent:
                return PlayerStatModifierMode.Multiplicative;
            default:
                return PlayerStatModifierMode.Flat;
        }
    }

    /// <summary>获取修改方式的本地化名称。</summary>
    private static string GetModeLabel(PlayerStatModifierMode mode)
    {
        switch (mode)
        {
            case PlayerStatModifierMode.Flat: return Text("flat", "平加");
            case PlayerStatModifierMode.AdditivePercent: return Text("add_percent", "比例加成");
            default: return Text("multiply", "乘算");
        }
    }

    /// <summary>返回修改方式对应的中性值。</summary>
    private static float GetNeutralValue(PlayerStatModifierMode mode)
    {
        return mode == PlayerStatModifierMode.Multiplicative ? 1f : 0f;
    }

    /// <summary>返回修改方式对应的中性输入。</summary>
    private static string GetNeutralInput(PlayerStatModifierMode mode)
    {
        return mode == PlayerStatModifierMode.Multiplicative ? "1" : "0";
    }
}
#endif
