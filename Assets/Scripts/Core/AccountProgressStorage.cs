using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>账号进度存储抽象，允许自动化测试使用内存后端而不接触真实用户目录。</summary>
public interface IAccountProgressStorage
{
    /// <summary>读取账号数据、恢复状态和是否允许继续写入。</summary>
    AccountProgressLoadResult Load();

    /// <summary>保存一份已经迁移和归一化的当前版本数据。</summary>
    bool Save(AccountProgressData data);
}

/// <summary>描述一次账号读取的结果及后续写入策略。</summary>
public sealed class AccountProgressLoadResult
{
    /// <summary>创建一份不可变语义的读取结果。</summary>
    public AccountProgressLoadResult(
        AccountProgressData data,
        bool shouldPersist,
        bool isReadOnly,
        string message)
    {
        Data = data;
        ShouldPersist = shouldPersist;
        IsReadOnly = isReadOnly;
        Message = message;
    }

    public AccountProgressData Data { get; }
    public bool ShouldPersist { get; }
    public bool IsReadOnly { get; }
    public string Message { get; }
}

/// <summary>
/// 使用 Application.persistentDataPath 下的 JSON、临时文件和上一份有效备份保存账号进度。
/// 所有调用均位于菜单操作或局结算等低频路径，不进入战斗热循环。
/// </summary>
public sealed class JsonAccountProgressStorage : IAccountProgressStorage
{
    private const string SaveFileName = "account-progress.json";
    private readonly string _directoryPath;
    private readonly string _savePath;
    private readonly string _temporaryPath;
    private readonly string _backupPath;

    /// <summary>使用指定目录建立可测试的账号 JSON 存储。</summary>
    public JsonAccountProgressStorage(string directoryPath)
    {
        _directoryPath = directoryPath;
        _savePath = Path.Combine(directoryPath, SaveFileName);
        _temporaryPath = _savePath + ".tmp";
        _backupPath = _savePath + ".bak";
    }

    /// <summary>优先读取主档；损坏时回退备份，新版本档则只读保护。</summary>
    public AccountProgressLoadResult Load()
    {
        if (TryRead(_savePath, out AccountProgressData primary, out string primaryError))
        {
            if (primary.saveVersion > AccountProgressData.CurrentVersion)
            {
                return new AccountProgressLoadResult(
                    AccountProgressData.CreateDefault(),
                    false,
                    true,
                    $"账号存档版本 {primary.saveVersion} 高于当前支持版本，已保留原档并进入只读安全模式。");
            }

            bool migrated = primary.saveVersion != AccountProgressData.CurrentVersion;
            return new AccountProgressLoadResult(
                AccountProgressMigrator.MigrateToCurrent(primary),
                migrated,
                false,
                migrated ? "账号存档已迁移到当前版本。" : string.Empty);
        }

        if (TryRead(_backupPath, out AccountProgressData backup, out string backupError) &&
            backup.saveVersion <= AccountProgressData.CurrentVersion)
        {
            PreserveCorruptPrimary();
            return new AccountProgressLoadResult(
                AccountProgressMigrator.MigrateToCurrent(backup),
                true,
                false,
                $"账号主档读取失败，已恢复上一份有效备份。{primaryError}");
        }

        PreserveCorruptPrimary();
        return new AccountProgressLoadResult(
            AccountProgressData.CreateDefault(),
            true,
            false,
            File.Exists(_savePath) || File.Exists(_backupPath)
                ? $"账号主档与备份均不可用，已建立安全新档。{primaryError} {backupError}"
                : string.Empty);
    }

    /// <summary>验证临时 JSON 后替换主档，并只用可解析的旧主档更新备份。</summary>
    public bool Save(AccountProgressData data)
    {
        if (data == null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_directoryPath);
            AccountProgressMigrator.Normalize(data);
            data.saveVersion = AccountProgressData.CurrentVersion;
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(_temporaryPath, json, new UTF8Encoding(false));

            if (!TryRead(_temporaryPath, out AccountProgressData verified, out _) ||
                verified.saveVersion != AccountProgressData.CurrentVersion)
            {
                SafeDeleteTemporary();
                return false;
            }

            if (TryRead(_savePath, out _, out _))
            {
                File.Copy(_savePath, _backupPath, true);
            }

            File.Copy(_temporaryPath, _savePath, true);
            SafeDeleteTemporary();
            return true;
        }
        catch (Exception exception)
        {
            SafeDeleteTemporary();
            Debug.LogError($"[AccountProgressStorage] 保存账号进度失败：{exception.Message}");
            return false;
        }
    }

    /// <summary>读取并解析指定 JSON；空文件、语法错误或空对象均视为失败。</summary>
    private static bool TryRead(
        string path,
        out AccountProgressData data,
        out string error)
    {
        data = null;
        error = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "文件为空。";
                return false;
            }

            data = JsonUtility.FromJson<AccountProgressData>(json);
            if (data == null)
            {
                error = "JSON 未生成有效账号对象。";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>在主档损坏时保留带时间戳的副本，便于人工诊断或恢复。</summary>
    private void PreserveCorruptPrimary()
    {
        if (!File.Exists(_savePath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_directoryPath);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string corruptPath = Path.Combine(
                _directoryPath,
                $"account-progress.corrupt-{timestamp}.json");
            File.Copy(_savePath, corruptPath, false);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AccountProgressStorage] 无法保留损坏存档副本：{exception.Message}");
        }
    }

    /// <summary>尽力删除未完成的临时文件；失败时不掩盖原始保存结果。</summary>
    private void SafeDeleteTemporary()
    {
        try
        {
            if (File.Exists(_temporaryPath))
            {
                File.Delete(_temporaryPath);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AccountProgressStorage] 无法清理临时文件：{exception.Message}");
        }
    }
}

/// <summary>批处理和轻量测试使用的内存存储，避免污染真实 persistentDataPath。</summary>
public sealed class InMemoryAccountProgressStorage : IAccountProgressStorage
{
    private AccountProgressData _data;

    /// <summary>读取当前内存快照；尚未保存时返回默认账号。</summary>
    public AccountProgressLoadResult Load()
    {
        return new AccountProgressLoadResult(
            _data != null ? Clone(_data) : AccountProgressData.CreateDefault(),
            _data == null,
            false,
            string.Empty);
    }

    /// <summary>把账号 JSON 往返复制到内存，避免调用方继续修改同一对象。</summary>
    public bool Save(AccountProgressData data)
    {
        _data = data != null ? Clone(data) : null;
        return _data != null;
    }

    /// <summary>通过 JsonUtility 建立深复制，行为与正式 JSON 存储保持一致。</summary>
    private static AccountProgressData Clone(AccountProgressData source)
    {
        return JsonUtility.FromJson<AccountProgressData>(JsonUtility.ToJson(source));
    }
}
