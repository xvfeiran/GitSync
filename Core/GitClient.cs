using System.Text;

namespace VendorSync.Core;

/// <summary>定位并封装本机 git.exe 的调用。</summary>
public sealed class GitClient
{
    private static readonly string[] CandidatePaths =
    {
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files\Git\bin\git.exe",
        @"C:\Program Files (x86)\Git\cmd\git.exe",
        @"%LOCALAPPDATA%\Programs\Git\cmd\git.exe",
    };

    public string GitPath { get; }

    /// <summary>
    /// 凭据辅助程序的修正参数（-c ...）。默认空；
    /// 由 MainForm 在检测到本机 helper 配置损坏后注入，
    /// 使所有 git 子进程都使用可用的 helper，而不改用户全局配置。
    /// </summary>
    public IReadOnlyList<string> CredentialOverride { get; set; } = Array.Empty<string>();

    private GitClient(string gitPath) => GitPath = gitPath;

    /// <summary>按已知路径 + PATH 查找 git。找不到返回 null。</summary>
    public static GitClient? Locate()
    {
        foreach (var raw in CandidatePaths)
        {
            var p = Environment.ExpandEnvironmentVariables(raw);
            if (File.Exists(p)) return new GitClient(p);
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "git.exe");
                if (File.Exists(candidate)) return new GitClient(candidate);
            }
            catch (ArgumentException)
            {
                // PATH 里有非法字符的条目，跳过。
            }
        }
        return null;
    }

    /// <summary>git 的环境变量：禁掉交互提示、分页器和颜色，保证不挂死。</summary>
    public static IReadOnlyDictionary<string, string?> BuildEnvironment()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // 绝不等待终端输入；缺凭据就立刻失败，由界面引导用户处理。
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ASKPASS"] = "",
            ["SSH_ASKPASS"] = "",
            ["GIT_PAGER"] = "cat",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_CONFIG_PARAMETERS"] = "",
            ["LC_ALL"] = "C.UTF-8",
            ["LANG"] = "C.UTF-8",
        };
    }

    public ProcessResult Run(
        IEnumerable<string> args,
        string? workDir = null,
        string? stdin = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        var env = new Dictionary<string, string?>(BuildEnvironment(), StringComparer.OrdinalIgnoreCase);
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv) env[kv.Key] = kv.Value;
        }

        // -c 参数必须位于子命令之前，这里通过环境变量注入更稳妥。
        var argsWithConfig = new List<string>
        {
            "-c", "core.quotepath=false",
            "-c", "color.ui=false",
            "-c", "credential.interactive=false",
        };

        // 凭据 helper 修正要排在用户配置之前生效，因此放在 -c 列表里靠前的位置。
        if (CredentialOverride.Count > 0)
        {
            argsWithConfig.AddRange(CredentialOverride);
        }

        argsWithConfig.AddRange(args);

        return ProcessRunner.Run(GitPath, argsWithConfig, workDir, stdin, timeout ?? TimeSpan.FromMinutes(10), env);
    }

    /// <summary>该目录是否是 git 仓库根或其子目录。</summary>
    public bool IsRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        var r = Run(new[] { "rev-parse", "--git-dir" }, path, timeout: TimeSpan.FromSeconds(20));
        return r.ExitCode == 0;
    }

    public string? GetToplevel(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;
        var r = Run(new[] { "rev-parse", "--show-toplevel" }, path, timeout: TimeSpan.FromSeconds(20));
        if (r.ExitCode != 0) return null;
        var text = r.StdOut.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace('/', '\\');
    }

    public IReadOnlyList<string> GetRemotes(string repo)
    {
        var r = Run(new[] { "remote" }, repo, timeout: TimeSpan.FromSeconds(20));
        if (r.ExitCode != 0) return Array.Empty<string>();
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public string? GetRemoteUrl(string repo, string remote)
    {
        var r = Run(new[] { "remote", "get-url", remote }, repo, timeout: TimeSpan.FromSeconds(20));
        if (r.ExitCode != 0) return null;
        var text = r.StdOut.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public string? ResolveRef(string repo, string @ref)
    {
        var r = Run(new[] { "rev-parse", "--verify", "--quiet", @ref }, repo, timeout: TimeSpan.FromSeconds(30));
        if (r.ExitCode != 0) return null;
        var text = r.StdOut.Trim();
        return text.Length == 40 ? text : null;
    }

    /// <summary>列出某远程的本地跟踪分支（不含 HEAD 符号引用）。</summary>
    public IReadOnlyList<string> GetRemoteBranches(string repo, string remote)
    {
        var r = Run(new[] { "for-each-ref", "--format=%(refname)", "refs/remotes/" + remote }, repo, timeout: TimeSpan.FromSeconds(60));
        if (r.ExitCode != 0) return Array.Empty<string>();

        var prefix = "refs/remotes/" + remote + "/";
        var list = new List<string>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = line[prefix.Length..];
            if (name == "HEAD") continue;
            list.Add(name);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public (int Ahead, int Behind) GetAheadBehind(string repo, string left, string right)
    {
        var r = Run(new[] { "rev-list", "--left-right", "--count", left + "..." + right }, repo, timeout: TimeSpan.FromSeconds(120));
        if (r.ExitCode != 0) return (-1, -1);
        var parts = r.StdOut.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (-1, -1);
        return (int.TryParse(parts[0], out var a) ? a : -1, int.TryParse(parts[1], out var b) ? b : -1);
    }

    public bool IsAncestor(string repo, string ancestor, string descendant)
    {
        var r = Run(new[] { "merge-base", "--is-ancestor", ancestor, descendant }, repo, timeout: TimeSpan.FromSeconds(60));
        return r.ExitCode == 0;
    }

    public IReadOnlyList<string> GetCommits(string repo, string range, int limit = 10)
    {
        var r = Run(new[] { "log", "--max-count=" + limit, "--format=%h %ad %an  %s", "--date=short", range }, repo, timeout: TimeSpan.FromSeconds(60));
        if (r.ExitCode != 0) return Array.Empty<string>();
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<string> GetDirtyFiles(string repo)
    {
        var r = Run(new[] { "status", "--porcelain" }, repo, timeout: TimeSpan.FromSeconds(30));
        if (r.ExitCode != 0) return Array.Empty<string>();
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>读远端当前分支哈希，用于推送后的独立校验。</summary>
    public Dictionary<string, string> ListRemoteHeads(string repo, string remote)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var r = Run(new[] { "ls-remote", "--heads", remote }, repo, timeout: TimeSpan.FromMinutes(3));
        if (r.ExitCode != 0) return result;

        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var sha = parts[0].Trim();
            var @ref = parts[1].Trim();
            const string prefix = "refs/heads/";
            if (sha.Length != 40 || !@ref.StartsWith(prefix, StringComparison.Ordinal)) continue;
            result[@ref[prefix.Length..]] = sha;
        }
        return result;
    }

    /// <summary>列出远端全部标签名（不含 ^{} 解引用行）。</summary>
    public IReadOnlyList<string> ListRemoteTags(string repo, string remote)
    {
        var r = Run(new[] { "ls-remote", "--tags", remote }, repo, timeout: TimeSpan.FromMinutes(3));
        if (r.ExitCode != 0) return Array.Empty<string>();

        var tags = new List<string>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var @ref = parts[1].Trim();
            const string prefix = "refs/tags/";
            if (!@ref.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = @ref[prefix.Length..];
            if (name.EndsWith("^{}", StringComparison.Ordinal)) continue;
            tags.Add(name);
        }
        return tags;
    }

    /// <summary>在本地创建一个轻量标签，指向指定提交。</summary>
    public bool CreateTag(string repo, string name, string sha, string? message = null)
    {
        var args = string.IsNullOrWhiteSpace(message)
            ? new[] { "tag", "--", name, sha }
            : new[] { "tag", "-m", message, "--", name, sha };
        return Run(args, repo, timeout: TimeSpan.FromSeconds(30)).ExitCode == 0;
    }

    /// <summary>只推送单个标签到远端（绝不覆盖远端已有标签）。</summary>
    public bool PushTag(string repo, string remote, string tag)
    {
        var r = Run(new[] { "push", remote, $"refs/tags/{tag}:refs/tags/{tag}" },
            repo, timeout: TimeSpan.FromMinutes(5));
        return r.ExitCode == 0;
    }

    /// <summary>把源仓库克隆到目标目录（目录必须为空或不存在）。</summary>
    public ProcessResult Clone(string repoUrl, string destDir, bool tags = true)
    {
        var args = new List<string> { "clone", "--progress" };
        if (tags) args.Add("--tags");
        args.Add("--");
        args.Add(repoUrl);
        args.Add(destDir);
        var r = Run(args, timeout: TimeSpan.FromMinutes(30));
        return r;
    }

    /// <summary>给克隆好的仓库补挂第二个远程（已存在则只更新地址）。</summary>
    public bool EnsureRemote(string repo, string name, string url)
    {
        var exists = GetRemotes(repo).Contains(name, StringComparer.Ordinal);
        var r = exists
            ? Run(new[] { "remote", "set-url", name, url }, repo, timeout: TimeSpan.FromSeconds(30))
            : Run(new[] { "remote", "add", name, url }, repo, timeout: TimeSpan.FromSeconds(30));
        return r.ExitCode == 0;
    }

    public ProcessResult Fetch(string repo, string remote, bool tags = true, Action<string>? onLine = null)    {
        var args = new List<string> { "fetch", remote, "--prune", "--no-write-fetch-head", "--progress" };
        if (tags) args.Add("--tags");
        var r = Run(args, repo, timeout: TimeSpan.FromMinutes(20));
        onLine?.Invoke(SanitizeGitNoise(r.Combined));
        return r;
    }

    /// <summary>
    /// 去掉与本工具无关、只会让用户困惑的噪声：
    /// 凭据管理器缺失、代理提示、ssh 的已知无害警告。
    /// </summary>
    public static string SanitizeGitNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var keep = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (line.Contains("git-credential-manager.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("missing OAuth configuration", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("CRLF will be replaced", StringComparison.OrdinalIgnoreCase)) continue;
            keep.Add(line);
        }
        return string.Join('\n', keep);
    }
}

/// <summary>从 git 的错误文本推断问题类型，给出人能看懂的建议。</summary>
public enum GitProblemKind
{
    None,
    SshKey,
    HostKey,
    HttpsCredential,
    Dns,
    Network,
    RepoMissing,
    Ownership,
    NotARepository,
    AuthFailed,
    Unknown
}

public static class GitProblem
{
    public static (GitProblemKind Kind, string Problem, string Hint) Explain(ProcessResult r, string remote)
    {
        var text = (r.StdOut + "\n" + r.StdErr).ToLowerInvariant();

        if (r.TimedOut)
        {
            return (GitProblemKind.Network,
                $"连接 {remote} 超时。",
                "多半是网络没通。连上后重试；仍超时请联系服务器管理员确认端口。");
        }

        if (text.Contains("permission denied (publickey"))
        {
            return (GitProblemKind.SshKey,
                "服务器不认识你这台机器的 SSH 密钥。",
                "点「凭据与密钥…」→「SSH 密钥」生成密钥并把公钥登记到 Git 服务器，然后点「测试连接」验证。");
        }
        if (text.Contains("host key verification failed") || text.Contains("remote host identification has changed"))
        {
            return (GitProblemKind.HostKey,
                "服务器指纹校验没通过。",
                "如果是第一次连接，请先确认指纹无误；如果以前能连现在变了，请联系 IT —— 这可能是中间人攻击。");
        }
        if (text.Contains("could not read username") || text.Contains("could not read password") ||
            text.Contains("unable to get password") || text.Contains("unable to get username") ||
            text.Contains("authentication failed") || text.Contains("invalid username or password") ||
            text.Contains("terminal prompts disabled"))
        {
            return (GitProblemKind.HttpsCredential,
                "HTTPS 地址需要账号或令牌，但当前拿不到。",
                "请把该站点的账号 / 个人访问令牌（PAT）保存进你本机的 Git 凭据管理器（Windows 凭据管理器），保存后重试即可。");
        }
        if (text.Contains("could not resolve host") || text.Contains("name or service not known") ||
            text.Contains("no such host"))
        {
            return (GitProblemKind.Dns,
                "域名解析不出来。",
                "通常是没连公司网络或 VPN。连上后重试。");
        }
        if (text.Contains("connection refused") || text.Contains("connection timed out") ||
            text.Contains("network is unreachable") || text.Contains("connection reset") ||
            text.Contains("failed to connect"))
        {
            return (GitProblemKind.Network,
                $"连不上服务器 {remote}。",
                "检查网络、VPN、端口和防火墙。");
        }
        if (text.Contains("repository not found") || text.Contains("does not appear to be a git repository") ||
            text.Contains("no such repository") || text.Contains("repository '") )
        {
            return (GitProblemKind.RepoMissing,
                "仓库不存在或当前账号没有权限。",
                "先核对地址拼写，再确认该账号在这个仓库上有读/写权限。");
        }
        if (text.Contains("dubious ownership") || text.Contains("safe.directory"))
        {
            return (GitProblemKind.Ownership,
                "这个目录属于别的 Windows 用户，git 拒绝操作。",
                "在确认目录可信后执行：git config --global --add safe.directory <该目录路径>；或把目录权限改回当前用户。");
        }
        if (text.Contains("not a git repository"))
        {
            return (GitProblemKind.NotARepository,
                "这个目录不是 git 仓库。",
                "确认路径指向仓库根目录，或者先克隆一次。");
        }

        return (GitProblemKind.Unknown, "操作失败。", string.IsNullOrWhiteSpace(r.Combined) ? "没有更多信息。" : r.Combined.Trim());
    }
}