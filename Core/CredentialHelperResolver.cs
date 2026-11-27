using System.Text;

namespace VendorSync.Core;

/// <summary>一个已配置的凭据辅助程序，及其是否可用。</summary>
public sealed class HelperEntry
{
    public string Value { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string? ResolvedPath { get; init; }
    public bool Exists { get; init; }
    public string Explain => Loc.T(Exists ? "Helper.Available" : "Helper.Missing");
}

/// <summary>凭据环境的诊断结果。</summary>
public sealed class HelperDiagnosis
{
    public bool NeedsRepair { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <summary>修好之后应该使用的 helper 路径；null 表示不需要或找不到。</summary>
    public string? ReplacementPath { get; init; }

    /// <summary>已配置的 helper，用于在界面上展示问题出在哪一层。</summary>
    public IReadOnlyList<HelperEntry> Configured { get; init; } = Array.Empty<HelperEntry>();

    /// <summary>可用的备用 helper（GCM 优先，wincred 兜底）。</summary>
    public IReadOnlyList<string> Available { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 凭据辅助程序（Git Credential Manager）的定位与自愈。
///
/// 为什么需要这个：公司镜像装出来的 Git 可能把 credential.helper 指向一个
/// 并不存在的路径（例如装的是 ucrt64 版，配置却写 mingw64 版）。
/// 这种配置会让每次 git 操作都往 stderr 刷一行 "No such file or directory"，
/// 并且 HTTPS 凭据读取失败。
///
/// 处理原则：**不擅自改用户的全局 git 配置**。
/// 默认只对本程序启动的子进程注入修正（-c 参数），用户的其它工具不受影响；
/// 需要永久修好时，由用户在界面上显式点按钮确认。
/// </summary>
public static class CredentialHelperResolver
{
    /// <summary>按 Git 安装目录找可用的 helper，GCM 优先。</summary>
    public static IReadOnlyList<string> FindAvailable(GitClient git)
    {
        var found = new List<string>();
        var gitRoot = TryGetGitRoot(git);

        var relativeCandidates = new[]
        {
            @"ucrt64\bin\git-credential-manager.exe",
            @"mingw64\bin\git-credential-manager.exe",
            @"mingw64\libexec\git-core\git-credential-manager.exe",
            @"ucrt64\libexec\git-core\git-credential-manager.exe",
            @"mingw64\bin\git-credential-manager-core.exe",
            @"ucrt64\bin\git-credential-helper-selector.exe",
        };

        if (gitRoot is not null)
        {
            foreach (var rel in relativeCandidates)
            {
                var p = Path.Combine(gitRoot, rel);
                if (File.Exists(p) && IsRealCredentialHelper(p)) found.Add(p);
            }
        }

        // wincred 是 Windows 自带兜底方案，纯系统 API、不依赖 GCM 的任何配置；
        // 公司镜像的 GCM 常见损坏（缺 OAuth 配置），所以探测顺序上 wincred 优先。
        var execPath = git.Run(new[] { "--exec-path" }).StdOut.Trim();
        if (!string.IsNullOrWhiteSpace(execPath))
        {
            var wincred = Path.Combine(execPath.Replace('/', '\\'), "git-credential-wincred.exe");
            if (File.Exists(wincred)) found.Insert(0, wincred);
        }

        return found;
    }

    /// <summary>helper-selector 只是选择器，不能直接当 helper 用。</summary>
    private static bool IsRealCredentialHelper(string path) =>
        !Path.GetFileName(path).Contains("selector", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetGitRoot(GitClient git)
    {
        // git.exe 通常在 <root>\cmd\git.exe 或 <root>\bin\git.exe
        var dir = Path.GetDirectoryName(git.GitPath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "git-bash.exe")) ||
                Directory.Exists(Path.Combine(dir, "mingw64")) ||
                Directory.Exists(Path.Combine(dir, "ucrt64")))
            {
                return dir;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    /// <summary>读取所有层级配置的 credential.helper，并判断每个是否真实存在。</summary>
    public static IReadOnlyList<HelperEntry> ReadConfigured(GitClient git)
    {
        var r = git.Run(new[] { "config", "--show-origin", "--get-all", "credential.helper" });
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut)) return Array.Empty<HelperEntry>();

        var execPath = git.Run(new[] { "--exec-path" }).StdOut.Trim().Replace('/', '\\');
        var entries = new List<HelperEntry>();

        foreach (var raw in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd();
            var tab = line.IndexOf('\t');
            var origin = tab > 0 ? line[..tab] : string.Empty;
            var value = (tab > 0 ? line[(tab + 1)..] : line).Trim();
            if (value.Length == 0) continue;

            // git 配置里路径的空格可能写成 "\ "，比较前要还原。
            var usable = value.Replace("\\ ", " ");
            string? resolved = null;

            if (usable.Contains('/') || usable.Contains('\\'))
            {
                resolved = usable.Replace('/', '\\');
                if (!Path.IsPathRooted(resolved))
                {
                    resolved = Path.Combine(execPath, resolved);
                }
            }
            else
            {
                // 裸名字（例如 manager / wincred）→ git-credential-<name>
                var exeName = "git-credential-" + usable + ".exe";
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(execPath)) candidates.Add(Path.Combine(execPath, exeName));
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try { candidates.Add(Path.Combine(dir.Trim(), exeName)); } catch (ArgumentException) { }
                }
                resolved = candidates.FirstOrDefault(File.Exists);
            }

            entries.Add(new HelperEntry
            {
                Value = value,
                Origin = origin,
                ResolvedPath = resolved,
                Exists = resolved is not null && File.Exists(resolved)
            });
        }
        return entries;
    }

    /// <summary>诊断本机凭据环境，给出是否需要修正。</summary>
    public static HelperDiagnosis Diagnose(GitClient git)
    {
        var configured = ReadConfigured(git);
        var available = FindAvailable(git);

        if (configured.Count == 0)
        {
            return new HelperDiagnosis
            {
                NeedsRepair = true,
                Summary = Loc.T("Diag.NoHelperSummary"),
                Detail = Loc.T("Diag.NoHelperDetailHead") +
                         (available.Count > 0
                             ? Loc.T("Diag.NoHelperDetailHasHelper")
                             : Loc.T("Diag.NoHelperDetailNoHelper")),
                ReplacementPath = available.FirstOrDefault(),
                Configured = configured,
                Available = available
            };
        }

        var broken = configured.Where(c => !c.Exists).ToList();
        if (broken.Count == 0)
        {
            return new HelperDiagnosis
            {
                NeedsRepair = false,
                Summary = Loc.T("Diag.OkSummary"),
                Detail = string.Join("\n", configured.Select(c => $"  {c.Value}  ← {c.Origin}")),
                Configured = configured,
                Available = available
            };
        }

        var replacement = available.FirstOrDefault();
        var sb = new StringBuilder();
        foreach (var b in broken)
        {
            sb.AppendLine(Loc.F("Diag.BrokenLine1", b.Value, b.Origin));
            sb.AppendLine(Loc.F("Diag.BrokenLine2", b.ResolvedPath ?? b.Value));
        }
        if (replacement is not null)
        {
            sb.AppendLine(Loc.F("Diag.ReplacementLine", replacement));
        }

        return new HelperDiagnosis
        {
            NeedsRepair = true,
            Summary = Loc.T("Diag.BrokenSummary"),
            Detail = sb.ToString(),

            // 找不到替代时也必须标记需要修正，否则无法保存 HTTPS 凭据。
            ReplacementPath = replacement,
            Configured = configured,
            Available = available
        };
    }

    /// <summary>
    /// 生成用于修正的 -c 参数：先清空 helper 列表，再设置可用的那个。
    /// 只影响本程序启动的子进程，不改用户配置。
    /// </summary>
    public static IReadOnlyList<string> BuildOverrideArgs(HelperDiagnosis diagnosis)
    {
        if (!diagnosis.NeedsRepair || diagnosis.ReplacementPath is null) return Array.Empty<string>();

        var escaped = EscapeForGitConfig(diagnosis.ReplacementPath.Replace('\\', '/'));
        return new[]
        {
            "-c", "credential.helper=",
            "-c", "credential.helper=" + escaped
        };
    }

    /// <summary>
    /// git 配置值里含空格时需要写成 "\ "（反斜杠+空格），
    /// 否则 git 会把路径在空格处截断，报 "No such file or directory"。
    /// </summary>
    private static string EscapeForGitConfig(string path) => path.Replace(" ", "\\ ");

    /// <summary>
    /// 把修正写入用户级 ~/.gitconfig，用于永久修好本机。
    /// 会先清空列表再设置，避免旧的错误条目继续执行并刷噪声。
    /// 只在用户显式确认后调用。
    /// </summary>
    public static (bool Ok, string Message) PersistToUserConfig(GitClient git, string helperPath)
    {
        if (!File.Exists(helperPath))
        {
            return (false, Loc.F("Err.PersistFileMissing", helperPath));
        }

        var escaped = EscapeForGitConfig(helperPath.Replace('\\', '/'));

        // 先清空列表，再设正确的。两条一起写才不会被旧的错误条目干扰。
        var reset = git.Run(new[] { "config", "--global", "--replace-all", "credential.helper", string.Empty });
        if (reset.ExitCode != 0)
        {
            return (false, Loc.F("Err.PersistFail", reset.Combined));
        }

        var add = git.Run(new[] { "config", "--global", "--add", "credential.helper", escaped });
        if (add.ExitCode != 0)
        {
            return (false, Loc.F("Err.PersistFail", add.Combined));
        }

        var verify = Diagnose(git);
        return verify.NeedsRepair
            ? (false, Loc.F("Err.PersistStillBroken", verify.Summary))
            : (true, Loc.T("Msg.PersistOk"));
    }
}