using System.Text;

namespace VendorSync.Core;

public sealed class SshKeyInfo
{
    public string Path { get; init; } = string.Empty;
    public string PublicPath { get; init; } = string.Empty;
    public string KeyType { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>私钥有密码保护时 ssh-keygen -y 会要求输入；无密码则能直接导出公钥。</summary>
    public bool PassphraseProtected { get; init; }
}

public sealed class SshTestResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = string.Empty;
    public string RawOutput { get; init; } = string.Empty;
}

/// <summary>
/// SSH 密钥管理。只做检测、生成、导出公钥和连通性测试；
/// 私钥从不读取内容、从不外传、从不打印。公钥是设计上公开的，可以安全复制。
/// </summary>
public sealed class SshKeyManager
{
    private readonly Action<string> _log;

    public SshKeyManager(Action<string> log) => _log = log;

    public static string SshDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public static string? LocateSsh()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe"),
            @"C:\Program Files\Git\usr\bin\ssh.exe",
            @"C:\Windows\System32\OpenSSH\ssh.exe",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        // 退回 PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "ssh.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static string? LocateSshKeygen()
    {
        var ssh = LocateSsh();
        if (ssh is not null)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ssh)!, "ssh-keygen.exe");
            if (File.Exists(sibling)) return sibling;
        }
        var candidates = new[]
        {
            @"C:\Program Files\Git\usr\bin\ssh-keygen.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>列出 ~/.ssh 下现有的密钥对（含 .pub 与私钥配对的情况）。</summary>
    public IReadOnlyList<SshKeyInfo> FindKeys()
    {
        var list = new List<SshKeyInfo>();
        var dir = SshDirectory;
        if (!Directory.Exists(dir)) return list;

        string[] privateNames;
        try
        {
            privateNames = Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n is not null
                            && !n.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)
                            && !n.Equals("known_hosts", StringComparison.OrdinalIgnoreCase)
                            && !n.Equals("config", StringComparison.OrdinalIgnoreCase)
                            && !n.Equals("authorized_keys", StringComparison.OrdinalIgnoreCase)
                            && !n.StartsWith("known_hosts", StringComparison.OrdinalIgnoreCase))
                .Select(n => n!)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return list;
        }

        foreach (var name in privateNames)
        {
            var priv = Path.Combine(dir, name);
            var pub = priv + ".pub";
            if (!File.Exists(pub)) continue; // 只有私钥没有公钥，不算可用密钥对

            var info = DescribeKey(priv, pub);
            if (info is not null) list.Add(info);
        }
        return list;
    }

    private SshKeyInfo? DescribeKey(string privatePath, string publicPath)
    {
        string publicKey;
        try
        {
            publicKey = File.ReadAllText(publicPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(publicKey)) return null;

        // 公钥行格式：<type> <base64> [comment]
        var parts = publicKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keyType = parts.Length > 0 ? parts[0] : "unknown";
        var comment = parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : string.Empty;

        return new SshKeyInfo
        {
            Path = privatePath,
            PublicPath = publicPath,
            KeyType = keyType,
            Comment = comment,
            PublicKey = publicKey,
            Fingerprint = GetFingerprint(publicPath),
            PassphraseProtected = IsEncrypted(privatePath)
        };
    }

    private string GetFingerprint(string publicPath)
    {
        var keygen = LocateSshKeygen();
        if (keygen is null) return string.Empty;
        var r = ProcessRunner.Run(keygen, new[] { "-lf", publicPath }, timeout: TimeSpan.FromSeconds(20));
        if (r.ExitCode != 0) return string.Empty;
        // 输出形如 "256 SHA256:xxx comment (ED25519)"
        var t = r.StdOut.Trim();
        var idx = t.IndexOf("SHA256:", StringComparison.Ordinal);
        if (idx < 0) return t;
        var end = t.IndexOf(' ', idx);
        return end < 0 ? t[idx..] : t[idx..end];
    }

    /// <summary>
    /// 判断私钥是否有口令保护。只读文件头部特征，不解密、不尝试口令：
    /// OpenSSH 加密私钥的 PEM 头包含 "Proc-Type: 4,ENCRYPTED"，新格式则是 "BEGIN OPENSSH PRIVATE KEY" 且
    /// 无法用 -y 无口令导出。这里用保守判断：只识别传统加密标记。
    /// </summary>
    private static bool IsEncrypted(string privatePath)
    {
        try
        {
            using var fs = new FileStream(privatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.ASCII);
            for (int i = 0; i < 8; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                if (line.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>
    /// 生成新的 ed25519 密钥。空口令（-N ""）是刻意的：
    /// 带口令的密钥在无人值守的同步工具里必然卡住，且密钥本身受 NTFS 用户目录权限保护。
    /// </summary>
    public (bool Ok, string Message, SshKeyInfo? Key) Generate(string comment, string? fileName = null)
    {
        var keygen = LocateSshKeygen();
        if (keygen is null)
        {
            return (false, Loc.T("Err.NoSshKeygen"), null);
        }

        Directory.CreateDirectory(SshDirectory);

        var name = string.IsNullOrWhiteSpace(fileName) ? "id_ed25519" : fileName.Trim();
        if (name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return (false, Loc.T("Err.BadFileName"), null);
        }

        var target = Path.Combine(SshDirectory, name);
        if (File.Exists(target))
        {
            return (false, Loc.F("Err.KeyExists", target), null);
        }

        var safeComment = string.IsNullOrWhiteSpace(comment) ? $"{Environment.UserName}@{Environment.MachineName}" : comment.Trim();
        var r = ProcessRunner.Run(keygen,
            new[] { "-t", "ed25519", "-C", safeComment, "-f", target, "-N", "", "-q" },
            timeout: TimeSpan.FromMinutes(2));

        if (r.ExitCode != 0 || !File.Exists(target))
        {
            return (false, Loc.F("Err.KeygenFail", GitClient.SanitizeGitNoise(r.Combined)), null);
        }

        TryRestrictAcl(target);

        var info = DescribeKey(target, target + ".pub");
        if (info is null) return (false, Loc.T("Err.KeyReadFail"), null);

        _log(Loc.F("Log.KeyGenerated", target, info.KeyType, info.Fingerprint));
        return (true, Loc.T("Msg.KeyGenerated"), info);
    }

    /// <summary>收紧私钥文件权限：去掉继承，只保留当前用户。失败不阻断主流程。</summary>
    private static void TryRestrictAcl(string path)
    {
        try
        {
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            ProcessRunner.Run("icacls",
                new[] { path, "/inheritance:r", "/grant:r", user + ":F" },
                timeout: TimeSpan.FromSeconds(20));
        }
        catch { /* 权限收紧是尽力而为 */ }
    }

    /// <summary>用 ssh -T 测试能否通过密钥连上服务器并拿到认证结果。</summary>
    public SshTestResult TestConnection(string host, int port = 22)
    {
        var ssh = LocateSsh();
        if (ssh is null)
        {
            return new SshTestResult { Ok = false, Message = Loc.T("Err.NoSsh") };
        }
        if (string.IsNullOrWhiteSpace(host))
        {
            return new SshTestResult { Ok = false, Message = Loc.T("Err.NoHost") };
        }

        var args = new List<string>
        {
            "-T",
            "-o", "BatchMode=yes",              // 禁止任何交互提问
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "ConnectTimeout=15",
            "-o", "PasswordAuthentication=no",
            "-o", "NumberOfPasswordPrompts=0",
        };
        if (port != 22) { args.Add("-p"); args.Add(port.ToString()); }
        args.Add($"git@{host}");

        var r = ProcessRunner.Run(ssh, args, timeout: TimeSpan.FromSeconds(40));
        var output = (r.StdOut + "\n" + r.StdErr).Trim();

        // Bitbucket / GitLab 的 ssh -T 常以非 0 退出并返回 "logged in as" / "authenticated" 之类文本。
        var lowered = output.ToLowerInvariant();
        if (lowered.Contains("successfully authenticated") ||
            lowered.Contains("logged in as") ||
            lowered.Contains("welcome to gitlab") ||
            lowered.Contains("authenticated"))
        {
            return new SshTestResult { Ok = true, Message = Loc.T("Ssh.AuthOk"), RawOutput = output };
        }
        if (lowered.Contains("permission denied"))
        {
            return new SshTestResult
            {
                Ok = false,
                Message = Loc.T("Ssh.PermDenied"),
                RawOutput = output
            };
        }
        if (lowered.Contains("host key verification failed"))
        {
            return new SshTestResult { Ok = false, Message = Loc.T("Ssh.HostKeyFail"), RawOutput = output };
        }
        if (lowered.Contains("connection timed out") || lowered.Contains("connection refused") ||
            lowered.Contains("could not resolve"))
        {
            return new SshTestResult
            {
                Ok = false,
                Message = Loc.T("Ssh.CantConnect"),
                RawOutput = output
            };
        }

        return new SshTestResult
        {
            Ok = false,
            Message = string.IsNullOrWhiteSpace(output) ? Loc.T("Ssh.NoResult") : output,
            RawOutput = output
        };
    }

    /// <summary>确认 known_hosts 里有没有该主机的指纹记录。</summary>
    public bool HasKnownHost(string host, int port)
    {
        var knownHosts = Path.Combine(SshDirectory, "known_hosts");
        if (!File.Exists(knownHosts) || string.IsNullOrWhiteSpace(host)) return false;

        try
        {
            var entry = port == 22 ? host : $"[{host}]:{port}";
            foreach (var line in File.ReadLines(knownHosts))
            {
                if (line.StartsWith('#')) continue;
                var firstField = line.Split(' ')[0];
                foreach (var candidate in firstField.Split(','))
                {
                    if (candidate.Equals(entry, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }
}