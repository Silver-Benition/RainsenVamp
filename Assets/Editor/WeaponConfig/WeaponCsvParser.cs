using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// CSV 中单个等级行的解析结果。
/// </summary>
internal sealed class WeaponCsvRow
{
    public string WeaponId;
    public string AssetName;
    public string DisplayName;
    public string Description;
    public WeaponRuntimeType RuntimeType;
    public int Level;
    public string PrefabPath;
    public string IconPath;
    public WeaponLevelData Stats;
}

/// <summary>
/// 同一武器的全部 CSV 等级行。
/// </summary>
internal sealed class WeaponCsvGroup
{
    public WeaponCsvRow Header;
    public readonly List<WeaponCsvRow> Rows = new List<WeaponCsvRow>();
}

/// <summary>
/// 负责把可由 Excel 导出的标准 CSV 转换为武器配置分组，不执行任何资产写入。
/// </summary>
internal static class WeaponCsvParser
{
    /// <summary>
    /// 解析完整 CSV，并返回按武器 ID 分组且按等级排序的预览数据。
    /// </summary>
    internal static List<WeaponCsvGroup> Parse(string csvText, List<string> errors)
    {
        var groups = new Dictionary<string, WeaponCsvGroup>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WeaponCsvGroup>();
        if (string.IsNullOrWhiteSpace(csvText))
        {
            errors.Add("CSV 内容为空。");
            return result;
        }

        string[] lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 2)
        {
            errors.Add("CSV 至少需要表头和一行数据。");
            return result;
        }

        List<string> headers = ParseLine(lines[0]);
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++)
        {
            string header = headers[index].Trim();
            if (!string.IsNullOrEmpty(header))
            {
                columns[header] = index;
            }
        }

        ValidateRequiredColumns(columns, errors);
        if (errors.Count > 0)
        {
            return result;
        }

        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                continue;
            }

            List<string> values = ParseLine(lines[lineIndex]);
            WeaponCsvRow row = ParseRow(values, columns, lineIndex + 1, errors);
            if (row == null)
            {
                continue;
            }

            if (!groups.TryGetValue(row.WeaponId, out var group))
            {
                group = new WeaponCsvGroup { Header = row };
                groups.Add(row.WeaponId, group);
                result.Add(group);
            }
            else
            {
                ValidateMetadata(group.Header, row, lineIndex + 1, errors);
            }

            group.Rows.Add(row);
        }

        for (int index = 0; index < result.Count; index++)
        {
            WeaponCsvGroup group = result[index];
            group.Rows.Sort((left, right) => left.Level.CompareTo(right.Level));
            ValidateLevels(group, errors);
        }

        result.Sort((left, right) => string.Compare(
            left.Header.AssetName,
            right.Header.AssetName,
            StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// 检查工具运行所需的最小表头集合。
    /// </summary>
    private static void ValidateRequiredColumns(
        Dictionary<string, int> columns,
        List<string> errors)
    {
        string[] required =
        {
            "weaponId", "assetName", "displayName", "description",
            "runtimeType", "level", "damage", "cooldown",
            "projectileCount", "prefabPath", "iconPath"
        };

        for (int index = 0; index < required.Length; index++)
        {
            if (!columns.ContainsKey(required[index]))
            {
                errors.Add($"缺少必需列：{required[index]}");
            }
        }
    }

    /// <summary>
    /// 把一行文本转换为强类型武器等级数据，并记录格式错误。
    /// </summary>
    private static WeaponCsvRow ParseRow(
        List<string> values,
        Dictionary<string, int> columns,
        int lineNumber,
        List<string> errors)
    {
        string weaponId = Read(values, columns, "weaponId").Trim();
        string assetName = Read(values, columns, "assetName").Trim();
        if (string.IsNullOrEmpty(weaponId) || string.IsNullOrEmpty(assetName))
        {
            errors.Add($"第 {lineNumber} 行：weaponId 和 assetName 不能为空。");
            return null;
        }

        if (!Enum.TryParse(
                Read(values, columns, "runtimeType"),
                true,
                out WeaponRuntimeType runtimeType))
        {
            errors.Add($"第 {lineNumber} 行：runtimeType 无效。");
            return null;
        }

        // 新表使用 lobGravity；仍接受旧版 arcHeight 表头，避免历史配置在首次迁移时静默丢值。
        float lobGravity = columns.ContainsKey("lobGravity")
            ? ParseFloat(values, columns, "lobGravity", 3f, lineNumber, errors)
            : ParseFloat(values, columns, "arcHeight", 3f, lineNumber, errors);

        var stats = new WeaponLevelData
        {
            damage = ParseFloat(values, columns, "damage", 0f, lineNumber, errors),
            cooldown = ParseFloat(values, columns, "cooldown", 1f, lineNumber, errors),
            projectileCount = ParseInt(values, columns, "projectileCount", 1, lineNumber, errors),
            projectileSpeed = ParseFloat(values, columns, "projectileSpeed", 0f, lineNumber, errors),
            pierceCount = ParseInt(values, columns, "pierceCount", 0, lineNumber, errors),
            lifeTime = ParseFloat(values, columns, "lifeTime", 1f, lineNumber, errors),
            spreadAngle = ParseFloat(values, columns, "spreadAngle", 0f, lineNumber, errors),
            bounceCount = ParseInt(values, columns, "bounceCount", 0, lineNumber, errors),
            auraRadius = ParseFloat(values, columns, "auraRadius", 1f, lineNumber, errors),
            tickInterval = ParseFloat(values, columns, "tickInterval", 0.5f, lineNumber, errors),
            orbitRadius = ParseFloat(values, columns, "orbitRadius", 1.7f, lineNumber, errors),
            orbitAngularSpeed = ParseFloat(values, columns, "orbitAngularSpeed", 180f, lineNumber, errors),
            lobGravity = lobGravity,
            spinSpeed = ParseFloat(values, columns, "spinSpeed", 0f, lineNumber, errors),
            meleeRange = ParseFloat(values, columns, "meleeRange", 1f, lineNumber, errors),
            meleeArc = ParseFloat(values, columns, "meleeArc", 90f, lineNumber, errors),
            activeDuration = ParseFloat(values, columns, "activeDuration", 0.18f, lineNumber, errors)
        };

        string bounceText = Read(values, columns, "bounceMode");
        if (!string.IsNullOrWhiteSpace(bounceText)
            && Enum.TryParse(bounceText, true, out BounceMode bounceMode))
        {
            stats.bounceMode = bounceMode;
        }

        return new WeaponCsvRow
        {
            WeaponId = weaponId,
            AssetName = assetName,
            DisplayName = Read(values, columns, "displayName").Trim(),
            Description = Read(values, columns, "description").Trim(),
            RuntimeType = runtimeType,
            Level = ParseInt(values, columns, "level", 0, lineNumber, errors),
            PrefabPath = Read(values, columns, "prefabPath").Trim(),
            IconPath = Read(values, columns, "iconPath").Trim(),
            Stats = stats
        };
    }

    /// <summary>
    /// 读取指定列；不存在的可选列返回空字符串。
    /// </summary>
    private static string Read(
        List<string> values,
        Dictionary<string, int> columns,
        string column)
    {
        return columns.TryGetValue(column, out int index) && index < values.Count
            ? values[index]
            : string.Empty;
    }

    /// <summary>
    /// 使用固定文化解析浮点数，保证不同系统区域设置得到同一结果。
    /// </summary>
    private static float ParseFloat(
        List<string> values,
        Dictionary<string, int> columns,
        string column,
        float fallback,
        int lineNumber,
        List<string> errors)
    {
        string text = Read(values, columns, column).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            return value;
        }

        errors.Add($"第 {lineNumber} 行：{column} 不是有效数字。");
        return fallback;
    }

    /// <summary>
    /// 使用固定文化解析整数，解析失败时记录行号和列名。
    /// </summary>
    private static int ParseInt(
        List<string> values,
        Dictionary<string, int> columns,
        string column,
        int fallback,
        int lineNumber,
        List<string> errors)
    {
        string text = Read(values, columns, column).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        errors.Add($"第 {lineNumber} 行：{column} 不是有效整数。");
        return fallback;
    }

    /// <summary>
    /// 确保同一武器的重复元数据保持一致，避免后续行静默覆盖首行。
    /// </summary>
    private static void ValidateMetadata(
        WeaponCsvRow header,
        WeaponCsvRow row,
        int lineNumber,
        List<string> errors)
    {
        if (!string.Equals(header.AssetName, row.AssetName, StringComparison.OrdinalIgnoreCase)
            || header.RuntimeType != row.RuntimeType
            || !string.Equals(header.PrefabPath, row.PrefabPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(header.IconPath, row.IconPath, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"第 {lineNumber} 行：同一 weaponId 的资产名、类型或资源路径不一致。");
        }
    }

    /// <summary>
    /// 校验等级必须从 1 开始并连续，防止列表索引与游戏等级错位。
    /// </summary>
    private static void ValidateLevels(WeaponCsvGroup group, List<string> errors)
    {
        for (int index = 0; index < group.Rows.Count; index++)
        {
            int expectedLevel = index + 1;
            if (group.Rows[index].Level != expectedLevel)
            {
                errors.Add(
                    $"{group.Header.WeaponId}：等级必须连续，期望 Lv.{expectedLevel}，实际为 Lv.{group.Rows[index].Level}。");
            }
        }
    }

    /// <summary>
    /// 解析一行标准 CSV，支持双引号包裹、逗号内容与两个双引号转义。
    /// </summary>
    private static List<string> ParseLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        bool insideQuotes = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
                continue;
            }

            if (character == ',' && !insideQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        values.Add(builder.ToString());
        return values;
    }
}
