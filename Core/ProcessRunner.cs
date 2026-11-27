using System.Diagnostics;
using System.Text;

namespace VendorSync.Core;

/// <summary>一次外部命令的执行结果。stdout/stderr 已按 UTF-8 解码。</summary>
public sealed class ProcessResult
{
    public int ExitCode { get; init; } = -1;
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }

    public bool Ok => ExitCode == 0 && !TimedOut;

    /// <summary>stdout 与 stderr 合并后的文本，便于统一展示。</summary>
    public string Combined
    {
        get
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(StdOut)) sb.Append(StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(StdErr))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(StdErr.TrimEnd());
            }
            return sb.ToString();
        }
    }

    public bool Contains(string text) =>
        StdOut.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        StdErr.Contains(text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 静默启动外部程序并收集输出。所有 git / ssh 调用都经过这里，保证：
/// 不弹黑窗、不因等待输入而挂死、超时可杀、编码统一为 UTF-8。
/// </summary>
public static class ProcessRunner
{
    public static ProcessResult Run(
        string fileName,
        IEnumerable<string> args,
        string? workDir = null,
        string? stdin = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir ?? Environment.CurrentDirectory,
        };

        foreach (var a in args) psi.ArgumentList.Add(a);

        if (env != null)
        {
            foreach (var kv in env)
            {
                if (kv.Value is null) psi.Environment.Remove(kv.Key);
                else psi.Environment[kv.Key] = kv.Value;
            }
        }

        Process? process = null;
        try
        {
            process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return new ProcessResult { ExitCode = -1, StdErr = "无法启动进程：" + fileName };
            }

            // 必须并发读取，否则大量输出会填满管道导致双方死锁。
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                if (stdin is not null) process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // 进程已经不接受输入，忽略即可。
            }

            var limit = timeout ?? TimeSpan.FromMinutes(10);
            var exited = process.WaitForExit((int)Math.Min(limit.TotalMilliseconds, int.MaxValue));

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
                try { process.WaitForExit(5000); } catch { }
                return new ProcessResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    StdErr = $"命令超时（{limit.TotalMinutes:0.#} 分钟）已终止：{fileName} {string.Join(' ', args)}"
                };
            }

            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 10_000);
            var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout,
                StdErr = stderr
            };
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = -1, StdErr = ex.Message };
        }
        finally
        {
            process?.Dispose();
        }
    }
}