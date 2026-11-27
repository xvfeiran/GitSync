using System.Text;

namespace VendorSync.Core;

/// <summary>凭据检查结果，用于界面上的绿色/红色状态。</summary>
public sealed class CredentialStatus
{
    public bool Ok { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// HTTPS 凭据。核心原则：本工具**不保存任何密码**。
///
/// 做法是走 git 自己的凭据协议（git credential approve/fill/reject）：
/// 用户在这里输入的账号与 PAT 通过 approve 交给 Git Credential Manager，
/// 由它加密写入 Windows 凭据管理器（DPAPI 保护）。之后所有 git 操作自动取用，
/// 本工具既不落盘也不在内存里长期持有密码。
///
/// 因此：程序可以自由分发给同事，每个人用的是自己机器上的凭据。
/// </summary>
public sealed class HttpsCredentialManager
{
    private readonly GitClient _git;
    private readonly Action<string> _log;

    public HttpsCredentialManager(GitClient git, Action<string> log)
    {
        _git = git;
        _log = log;
    }

    /// <summary>构造 git credential 协议需要的键值块。</summary>
    private static string BuildRequest(string host, string? username, string? password, string? protocol = "https")
    {
        var sb = new StringBuilder();
        sb.Append("protocol=").Append(protocol).Append('\n');
        sb.Append("host=").Append(host).Append('\n');
        if (!string.IsNullOrEmpty(username)) sb.Append("username=").Append(username).Append('\n');
        if (!string.IsNullOrEmpty(password)) sb.Append("password=").Append(password).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>强制指定某个 helper 的 -c 参数（清空列表 + 指定一个）。</summary>
    private static IReadOnlyList<string> CredentialArgs(string helperPath, string subcommand) =>
    [
        "-c", "credential.helper=",
        "-c", "credential.helper=" + helperPath.Replace('\\', '/').Replace(" ", "\\ "),
        "credential", subcommand
    ];

    /// <summary>找 Windows 自带的 wincred helper（不依赖 GCM 配置，系统 API 直存）。</summary>
    private string? FindWincred()
    {
        var candidates = CredentialHelperResolver.FindAvailable(_git);
        return candidates.FirstOrDefault(c => c.Contains("wincred", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取合并后的输出，用于判断 GCM 的静默失败。</summary>
    private static string CombindNoise(ProcessResult r) => r.Combined ?? string.Empty;

    /// <summary>
    /// 检查该地址当前是否已有可用凭据。用 credential fill 探测：
    /// 成功返回凭据（这里只用来判断「有没有」，不显示密码）。
    /// </summary>
    public CredentialStatus Probe(string remoteUrl)
    {
        var target = RemoteUrl.GetHttpsTarget(remoteUrl);
        if (target is null)
        {
            return new CredentialStatus
            {
                Ok = false,
                Summary = "不是 HTTPS 地址",
                Detail = "这个远程用的是 SSH 或本地路径，不需要 HTTPS 账号。"
            };
        }

        var (host, username) = target.Value;
        var request = BuildRequest(host, username, null);

        // credential fill 需要 stdin；GIT_TERMINAL_PROMPT=0 保证它不会弹窗等待。
        var r = _git.Run(new[] { "credential", "fill" }, workDir: Environment.CurrentDirectory,
            stdin: request, timeout: TimeSpan.FromSeconds(45));

        if (r.ExitCode != 0 || CombindNoise(r).Contains("missing OAuth configuration"))
        {
            // GCM 本身坏了（例如缺 OAuth 配置）时换成 wincred 再试一次。
            var wincred = FindWincred();
            if (wincred is not null)
            {
                r = _git.Run(CredentialArgs(wincred, "fill"), workDir: Environment.CurrentDirectory,
                    stdin: request, timeout: TimeSpan.FromSeconds(45));
            }
        }

        if (r.ExitCode != 0)
        {
            return new CredentialStatus
            {
                Ok = false,
                Summary = "还没有保存的凭据",
                Detail = $"Git 凭据管理器里没有 {host} 的可用账号。点「保存凭据」填入你的账号和 PAT。"
            };
        }

        // 解析返回，只取 username 用于展示；password 一律不显示。
        string? foundUser = null;
        var hasPassword = false;
        foreach (var line in r.StdOut.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("username=", StringComparison.OrdinalIgnoreCase)) foundUser = t["username=".Length..];
            else if (t.StartsWith("password=", StringComparison.OrdinalIgnoreCase) && t.Length > "password=".Length) hasPassword = true;
        }

        if (!hasPassword)
        {
            return new CredentialStatus
            {
                Ok = false,
                Summary = "凭据不完整",
                Detail = "找到了用户名但没有密码或令牌，请重新保存一次。"
            };
        }

        return new CredentialStatus
        {
            Ok = true,
            Summary = $"已保存（用户：{foundUser ?? "未知"}）",
            Detail = $"凭据由 Git Credential Manager 保管在 Windows 凭据管理器，本工具不保存密码。"
        };
    }

    /// <summary>
    /// 把账号与 PAT 交给 Git Credential Manager 保存。
    /// password 参数只在本次调用内存在，用完即弃，不写入任何文件。
    /// </summary>
    public CredentialStatus Save(string remoteUrl, string username, string password)
    {
        var target = RemoteUrl.GetHttpsTarget(remoteUrl);
        if (target is null)
        {
            return new CredentialStatus { Ok = false, Summary = "地址不是 HTTPS", Detail = "SSH 地址不需要账号密码，请改用 SSH 密钥配置。" };
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            return new CredentialStatus { Ok = false, Summary = "用户名为空", Detail = "请填写你的账号或用户名。" };
        }
        if (string.IsNullOrEmpty(password))
        {
            return new CredentialStatus { Ok = false, Summary = "密码或令牌为空", Detail = "请填写密码或个人访问令牌（PAT）。" };
        }

        var (host, _) = target.Value;
        var payload = BuildRequest(host, username, password);

        var r = _git.Run(new[] { "credential", "approve" }, workDir: Environment.CurrentDirectory,
            stdin: payload, timeout: TimeSpan.FromSeconds(60));

        // GCM 可能自身损坏（例如缺 OAuth 配置）导致静默保存失败：
        // approve 虽然 exit 0，但随后 fill 探测不到。此时强制改用 wincred 再存一次。
        var verify = Probe(remoteUrl);
        if (!verify.Ok)
        {
            var wincred = FindWincred();
            if (wincred is not null)
            {
                _log($"GCM 保存未生效，改用 wincred 保存 {host} 的凭据…");
                _git.Run(CredentialArgs(wincred, "approve"), workDir: Environment.CurrentDirectory,
                    stdin: payload, timeout: TimeSpan.FromSeconds(60));
                verify = Probe(remoteUrl);
            }
        }

        if (!verify.Ok)
        {
            var detail = GitClient.SanitizeGitNoise(r.Combined);
            _log("保存凭据失败：" + detail);
            return new CredentialStatus
            {
                Ok = false,
                Summary = "保存失败",
                Detail = "Git 没能写入凭据管理器：" + (string.IsNullOrWhiteSpace(detail) ? "未知原因" : detail)
            };
        }

        _log($"已把 {host} 的凭据保存到 Windows 凭据管理器（本工具不保存密码）。");
        return verify;
    }

    /// <summary>删除该地址已保存的凭据，用于密码失效后重置。</summary>
    public CredentialStatus Erase(string remoteUrl)
    {
        var target = RemoteUrl.GetHttpsTarget(remoteUrl);
        if (target is null)
        {
            return new CredentialStatus { Ok = false, Summary = "地址不是 HTTPS", Detail = "没有可删除的 HTTPS 凭据。" };
        }

        var (host, username) = target.Value;
        var payload = BuildRequest(host, username, null);
        var r = _git.Run(new[] { "credential", "reject" }, workDir: Environment.CurrentDirectory,
            stdin: payload, timeout: TimeSpan.FromSeconds(45));

        if (r.ExitCode != 0)
        {
            return new CredentialStatus
            {
                Ok = false,
                Summary = "删除失败",
                Detail = GitClient.SanitizeGitNoise(r.Combined)
            };
        }

        _log($"已删除 {host} 的已保存凭据。");
        return new CredentialStatus { Ok = true, Summary = "已删除", Detail = "下次操作会要求重新输入。" };
    }
}