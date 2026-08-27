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
        new PlayerStatModifierMode[PlayerStatPresentation.StatCount];
    private readonly float[] _modifierValues = new float[PlayerStatPresentation.StatCount];
    private readonly string[] _modifierInputs = new string[PlayerStatPresentation.StatCount];
    private readonly bool[] _modifierActive = new bool[PlayerStatPresentation.StatCount];

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

    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        _windowRect = GUI.Window(
            GetInstanceID(),
            _windowRect,
            DrawWindow,
            "Player Attribute Debug (F9)");
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
            float.IsNaN(value) || float.IsInfinity(value))
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

    private void DrawWindow(int windowId)
    {
        GUILayout.Space(4f);
        GUILayout.Label(
            "Raw modifier values: Add % uses 0.25 for +25%; Multiply uses 1.25 for x1.25.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Player", GUILayout.Width(120f)))
        {
            ResolvePlayerStats(true);
        }

        bool previousEnabled = GUI.enabled;
        GUI.enabled = _playerStats != null;
        if (GUILayout.Button("Apply All", GUILayout.Width(100f)))
        {
            TryApplyAllInputs();
        }

        if (GUILayout.Button("Clear All", GUILayout.Width(100f)))
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
        for (int index = 0; index < PlayerStatPresentation.StatCount; index++)
        {
            DrawStatRow(index);
        }
        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
    }

    private void DrawStatRow(int index)
    {
        PlayerStatType statType = PlayerStatPresentation.GetStatAt(index);
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

        if (GUILayout.Button("Apply", GUILayout.Width(70f)))
        {
            TryApplyRow(index);
        }

        bool rowButtonEnabled = GUI.enabled;
        GUI.enabled = _modifierActive[index];
        if (GUILayout.Button("Clear", GUILayout.Width(65f)))
        {
            ClearRow(index);
        }
        GUI.enabled = rowButtonEnabled;
        GUILayout.EndHorizontal();
    }

    private void TryApplyRow(int index)
    {
        if (!TryParseModifierValue(_modifierInputs[index], out float value))
        {
            _statusMessage = $"Invalid value for {PlayerStatPresentation.GetStatAt(index)}.";
            return;
        }

        _modifierValues[index] = value;
        _modifierInputs[index] = PlayerStatPresentation.FormatRawValue(value);
        _modifierActive[index] = true;
        ApplyEditorState();
        _statusMessage = $"Applied {PlayerStatPresentation.GetStatAt(index)}.";
    }

    private void TryApplyAllInputs()
    {
        for (int index = 0; index < _modifierActive.Length; index++)
        {
            if (!_modifierActive[index])
            {
                continue;
            }

            if (!TryParseModifierValue(_modifierInputs[index], out float value))
            {
                _statusMessage = $"Invalid value for {PlayerStatPresentation.GetStatAt(index)}.";
                return;
            }

            _modifierValues[index] = value;
            _modifierInputs[index] = PlayerStatPresentation.FormatRawValue(value);
        }

        ApplyEditorState();
        _statusMessage = "Applied all active debug modifiers.";
    }

    private void ClearRow(int index)
    {
        _modifierActive[index] = false;
        _modifierValues[index] = GetNeutralValue(_modifierModes[index]);
        _modifierInputs[index] = GetNeutralInput(_modifierModes[index]);
        ApplyEditorState();
        _statusMessage = $"Cleared {PlayerStatPresentation.GetStatAt(index)}.";
    }

    /// <summary>把全部活动调试项作为一个来源提交，确保升级或清除不会遗留旧加成。</summary>
    private void ApplyEditorState()
    {
        if (_playerStats == null)
        {
            return;
        }

        var modifiers = new List<PlayerStatModifier>(PlayerStatPresentation.StatCount);
        for (int index = 0; index < _modifierActive.Length; index++)
        {
            if (!_modifierActive[index])
            {
                continue;
            }

            modifiers.Add(new PlayerStatModifier(
                PlayerStatPresentation.GetStatAt(index),
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
        if (resetWhenTargetChanges)
        {
            ResetLocalModifiers();
        }

        _statusMessage = _playerStats != null
            ? $"Player resolved: {_playerStats.gameObject.name}"
            : "PlayerStats was not found.";
    }

    private void InitializeEditorState()
    {
        if (_editorStateInitialized)
        {
            return;
        }

        _editorStateInitialized = true;
        ResetLocalModifiers();
    }

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

    private static bool TryParseModifierValue(string input, out float value)
    {
        bool parsed = float.TryParse(
            input,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) || float.TryParse(input, out value);
        return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
    }

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

    private static string GetModeLabel(PlayerStatModifierMode mode)
    {
        switch (mode)
        {
            case PlayerStatModifierMode.Flat: return "Flat";
            case PlayerStatModifierMode.AdditivePercent: return "Add %";
            default: return "Multiply";
        }
    }

    private static float GetNeutralValue(PlayerStatModifierMode mode)
    {
        return mode == PlayerStatModifierMode.Multiplicative ? 1f : 0f;
    }

    private static string GetNeutralInput(PlayerStatModifierMode mode)
    {
        return mode == PlayerStatModifierMode.Multiplicative ? "1" : "0";
    }
}
#endif
