namespace VendorSync.Core;

/// <summary>
/// 找一个当前进程真的可写的临时目录。
/// 公司安全策略会拦截未签名 exe 在 %TEMP% 下创建文件，
/// 所以所有需要临时文件/目录的地方都要经过这里，被拒时回退到 exe 目录。
/// </summary>
public static class TempRoot
{
    private static string? _resolved;
    private static bool _tried;

    public static string Dir()
    {
        if (_tried) return _resolved ?? AppContext.BaseDirectory;
        _tried = true;

        foreach (var candidate in new[] { Path.GetTempPath(), AppContext.BaseDirectory })
        {
            try
            {
                var probe = Path.Combine(candidate, "vs-probe-" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
                _resolved = candidate;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return _resolved ?? AppContext.BaseDirectory;
    }
}
