/// <summary>
/// 保存主菜单本次确认的角色，供 MainLevel 的 PlayerStats 在 Awake 时消费。
/// 仅保存 ScriptableObject 引用，不复制或修改角色资产。
/// </summary>
public static class CharacterSelectionSession
{
    /// <summary>最近一次由角色选择页确认的角色；尚未选择时为 null。</summary>
    public static CharacterDataSO SelectedCharacter { get; private set; }

    /// <summary>记录一个有效角色；空引用不会覆盖已有选择。</summary>
    public static bool Select(CharacterDataSO character)
    {
        if (character == null)
        {
            return false;
        }

        SelectedCharacter = character;
        return true;
    }

    /// <summary>离开一轮菜单流程或自动化测试后清除跨场景选择。</summary>
    public static void Clear()
    {
        SelectedCharacter = null;
    }
}
