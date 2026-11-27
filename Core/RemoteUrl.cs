namespace VendorSync.Core;

public enum RemoteScheme
{
    Https,
    Ssh,
    Local,
    Unknown
}

/// <summary>解析远程地址，决定该用哪套凭据方式。</summary>
public static class RemoteUrl
{
    public static RemoteScheme Detect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return RemoteScheme.Unknown;
        var u = url.Trim();

        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) return RemoteScheme.Ssh;
        if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return RemoteScheme.Https;
        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return RemoteScheme.Https;
        if (u.StartsWith("git://", StringComparison.OrdinalIgnoreCase)) return RemoteScheme.Ssh;
        if (u.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return RemoteScheme.Local;

        // scp 风格：git@host:path.git
        var at = u.IndexOf('@');
        var colon = u.IndexOf(':');
        if (at >= 0 && colon > at) return RemoteScheme.Ssh;

        if (Directory.Exists(u) || u.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(u))
        {
            return RemoteScheme.Local;
        }
        return RemoteScheme.Unknown;
    }

    /// <summary>从 SSH 地址里取主机名，用于 ssh -T 测试与 ssh-keyscan。</summary>
    public static string? GetSshHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();

        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return null;
            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        var at = u.IndexOf('@');
        var colon = u.IndexOf(':');
        if (at >= 0 && colon > at)
        {
            var hostPart = u[(at + 1)..colon];
            return string.IsNullOrWhiteSpace(hostPart) ? null : hostPart;
        }
        return null;
    }

    /// <summary>取 SSH 端口：ssh:// 用地址里的，scp 风格默认 22。</summary>
    public static int GetSshPort(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 22;
        var u = url.Trim();
        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(u, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? 22 : uri.Port;
        }
        return 22;
    }

    /// <summary>从 HTTPS 地址取 host，供 git credential 匹配使用。</summary>
    public static (string Host, string? Username)? GetHttpsTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;

        var username = string.IsNullOrEmpty(uri.UserInfo)
            ? null
            : Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]);
        return (uri.Host, username);
    }

    /// <summary>把地址里的用户名/密码抹掉，用于日志和安全展示。</summary>
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var u = url.Trim();
        if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            // 连主机名一起隐藏：公司服务器地址本身也算敏感信息，展示时只留协议与尾段。
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return "（已隐藏）";
            var tail = uri.AbsolutePath.TrimEnd('/');
            var lastSeg = tail.Contains('/') ? tail[(tail.LastIndexOf('/') + 1)..] : tail;
            return $"{uri.Scheme}://{uri.Host}/…/{lastSeg}";
        }
        return u;
    }
}