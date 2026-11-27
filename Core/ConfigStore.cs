using System.Text.Json;
using System.Text.Json.Serialization;

namespace VendorSync.Core;

/// <summary>
/// 应用配置。刻意只保存路径与远程名 —— 不含任何账号、密码、令牌或密钥内容。
/// 因此配置文件可以随便携版一起被复制、讨论甚至在群里发截图而不泄密。
/// </summary>
/// <summary>项目在首页卡片上显示的健康状态（持久化的是「上次已知」状态）。</summary>
public enum ProjectHealth
{
    Unknown,
    UpToDate,
    Pending,
    Failed
}

/// <summary>
/// 一个同步项目 = 一个本地仓库 + 源仓库远程 + 目标仓库远程。
/// 历史原因类名仍叫 RepoProfile（旧配置里的 RecentRepos 就是项目列表）。
/// </summary>
public sealed class RepoProfile
{
    public string Path { get; set; } = string.Empty;
    public string VendorRemote { get; set; } = "origin";
    public string InternalRemote { get; set; } = "bitbucket";

    /// <summary>显示名；空时用目录名。</summary>
    public string? Name { get; set; }
    /// <summary>分叉策略：Force / Merge；空时继承全局。</summary>
    public string? Strategy { get; set; }
    /// <summary>是否同步标签；空时继承全局。</summary>
    public bool? IncludeTags { get; set; }

    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public ProjectHealth LastHealth { get; set; } = ProjectHealth.Unknown;
    public int PendingCount { get; set; }
    public string? LastMessage { get; set; }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name!
        : System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path;

    public RepoProfile Copy() => (RepoProfile)MemberwiseClone();
}

public sealed class AppConfig
{
    public string? RepoPath { get; set; }
    public string VendorRemote { get; set; } = "origin";
    public string InternalRemote { get; set; } = "bitbucket";
    /// <summary>分叉时的默认策略：以源仓库为准，直接覆盖目标仓库。</summary>
    public string Strategy { get; set; } = "Force";
    public bool IncludeTags { get; set; } = true;
    public bool ConfirmBeforePush { get; set; } = true;

    /// <summary>最近用过的仓库（含各自的远程配置），启动时填进下拉框。</summary>
    public List<RepoProfile> RecentRepos { get; set; } = new();

    /// <summary>兼容旧版配置文件（没有 RecentRepos 字段时反序列化会得到 null）。</summary>
    public void EnsureLists()
    {
        RecentRepos ??= new List<RepoProfile>();
        RecentRepos.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Path));

        // 旧版只有 RepoPath、没有历史列表：把它迁移成第一个项目。
        if (!string.IsNullOrWhiteSpace(RepoPath) && FindProfile(RepoPath) is null)
        {
            RecentRepos.Insert(0, new RepoProfile { Path = RepoPath!, VendorRemote = VendorRemote, InternalRemote = InternalRemote });
        }
    }

    /// <summary>高级：额外的 ssh 私钥路径（多身份场景）。空表示用默认。</summary>
    public string? SshIdentityFile { get; set; }

    [JsonIgnore]
    public SyncStrategy StrategyValue =>
        string.Equals(Strategy, "Force", StringComparison.OrdinalIgnoreCase) ? SyncStrategy.Force : SyncStrategy.Merge;

    private const int MaxRecent = 50;

    /// <summary>项目列表（与 RecentRepos 是同一份数据）。</summary>
    [JsonIgnore]
    public List<RepoProfile> Projects => RecentRepos;

    public SyncStrategy StrategyFor(RepoProfile p) =>
        string.Equals(p.Strategy ?? Strategy, "Force", StringComparison.OrdinalIgnoreCase) ? SyncStrategy.Force : SyncStrategy.Merge;

    public bool IncludeTagsFor(RepoProfile p) => p.IncludeTags ?? IncludeTags;

    /// <summary>新增项目（同路径已存在则返回已有的），不打乱已有顺序。</summary>
    public RepoProfile AddProject(string path, string vendorRemote, string internalRemote, string? name = null)
    {
        var existing = FindProfile(path);
        if (existing is not null) return existing;
        var p = new RepoProfile { Path = path, VendorRemote = vendorRemote, InternalRemote = internalRemote, Name = name };
        RecentRepos.Add(p);
        return p;
    }

    public void RemoveProject(RepoProfile p)
    {
        RecentRepos.Remove(p);
        if (string.Equals(RepoPath, p.Path, StringComparison.OrdinalIgnoreCase))
        {
            RepoPath = RecentRepos.FirstOrDefault()?.Path;
        }
    }

    /// <summary>
    /// 记住「当前项目」：命令行 --inspect 和旧字段 RepoPath/VendorRemote/InternalRemote 依赖它。
    /// </summary>
    public void SetCurrent(RepoProfile p)
    {
        RepoPath = p.Path;
        VendorRemote = p.VendorRemote;
        InternalRemote = p.InternalRemote;
    }

    /// <summary>按路径（不区分大小写）找历史记录。</summary>
    public RepoProfile? FindProfile(string? path) =>
        RecentRepos.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>把一个仓库提到历史最前面，最多保留 MaxRecent 条。</summary>
    public void RememberRepo(string? path, string vendorRemote, string internalRemote)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var existing = FindProfile(path);
        if (existing is not null)
        {
            existing.VendorRemote = vendorRemote;
            existing.InternalRemote = internalRemote;
            RecentRepos.Remove(existing);
            RecentRepos.Insert(0, existing);
            return;
        }

        RecentRepos.Insert(0, new RepoProfile
        {
            Path = path,
            VendorRemote = vendorRemote,
            InternalRemote = internalRemote
        });
        while (RecentRepos.Count > MaxRecent) RecentRepos.RemoveAt(RecentRepos.Count - 1);
    }

    public AppConfig Clone() => new()
    {
        RepoPath = RepoPath,
        VendorRemote = VendorRemote,
        InternalRemote = InternalRemote,
        Strategy = Strategy,
        IncludeTags = IncludeTags,
        ConfirmBeforePush = ConfirmBeforePush,
        SshIdentityFile = SshIdentityFile,
        RecentRepos = RecentRepos.Select(p => p.Copy()).ToList()
    };
}

/// <summary>
/// 配置读写。优先放在 exe 同目录（便携：拷走即可带走配置），
/// 目录不可写时退回 %LOCALAPPDATA%。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }

    public ConfigStore(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            FilePath = overridePath;
            return;
        }

        var exeDir = AppContext.BaseDirectory;
        var portable = Path.Combine(exeDir, "gitsync.config.json");
        FilePath = CanWriteDirectory(exeDir)
            ? MigrateFromLegacy(exeDir, portable, Path.Combine(exeDir, "vendorsync.config.json"))
            : MigrateFromLegacy(
                exeDir,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GitSync", "gitsync.config.json"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VendorSync", "vendorsync.config.json"));
    }

    /// <summary>工具更名（VendorSync → GitSync）后，首次运行把旧配置文件搬过来。</summary>
    private static string MigrateFromLegacy(string dir, string newPath, string legacyPath)
    {
        try
        {
            if (!File.Exists(newPath) && File.Exists(legacyPath))
            {
                Directory.CreateDirectory(dir);
                File.Copy(legacyPath, newPath);
            }
        }
        catch { /* 迁移失败就当没有旧配置 */ }
        return newPath;
    }

    private static bool CanWriteDirectory(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N")[..6]);
            using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppConfig();
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return new AppConfig();
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            config.EnsureLists();
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppConfig();
        }
    }

    /// <summary>保存前兜底：即使调用方传进来的是旧配置对象也安全。</summary>
    public bool Save(AppConfig config, out string error)
    {
        config.EnsureLists();
        error = string.Empty;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 先写临时文件再替换，避免半途断电留下损坏的配置。
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }
}