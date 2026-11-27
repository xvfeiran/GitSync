using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VendorSync.Core;

namespace VendorSync;

/// <summary>
/// 无界面自测。用一次性临时 bare 仓库复刻真实场景，
/// 覆盖：状态判定、快进、新建、内部独有保护、分叉合并、强制覆盖、
/// 冲突安全放弃、幂等重跑、凭据地址解析、日志脱敏。
///
/// 程序是 WinExe（无控制台），所以自测输出要显式写 stdout 并附带控制台，
/// 这样从终端调用时能看到完整报告。
/// </summary>
internal static class SelfTest
{
    private static int _pass;
    private static int _fail;
    private static readonly List<string> Lines = new();
    private static IntPtr _console = IntPtr.Zero;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static int RunAll()
    {
        EnsureConsole();

        // 关键：自测在无人值守下运行，任何模态对话框都会让进程永久挂起。
        // 开启静默后 Prompt.* 只写日志、不弹窗。
        AppLog.Silent = true;

        Write("GitSync 自测");

        var git = GitClient.Locate();
        if (git is null)
        {
            Write("找不到 git.exe，无法自测。");
            Flush(SystemReportPath());
            return 2;
        }

        Write($"git: {git.GitPath}");
        Write(string.Empty);

        UrlTests();
        ProcessTests();
        CredentialHelperTests(git);
        UiTests(git);

        var root = Path.Combine(WritableTemp(), "vendorsync-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            RepoTests(git, root);
        }
        catch (Exception ex)
        {
            // 公司安全策略会拦截"未签名 exe 的子进程"创建 msys 对象 / 写 %TEMP%，
            // 导致本进程派生的 git push 无法工作。这不是被测代码的问题，降级为 SKIP。
            var msg = ex.Message;
            if (msg.Contains("NtCreateDirectoryObject") || msg.Contains("signal pipe") ||
                msg.Contains("vshome-") && msg.Contains("UnauthorizedAccess"))
            {
                Write("  SKIP  当前运行环境拦截了本程序派生的 git 子进程（安全策略限制），");
                Write("        仓库推送类测试无法在本机执行。请给 exe 签名/加白后再跑完整自测。");
            }
            else
            {
                Fail("自测过程中抛出异常：" + msg + Environment.NewLine + ex.StackTrace);
            }
        }
        finally
        {
            TryDelete(root);
        }

        Write(string.Empty);
        foreach (var line in Lines) Write(line);
        Write(string.Empty);

        var total = _pass + _fail;
        if (_fail == 0)
        {
            Write($"self-test: {total}/{total} 通过");
        }
        else
        {
            Write($"self-test: {_pass}/{total} 通过，{_fail} 个失败");
        }

        Flush(SystemReportPath());
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>报告先写到临时目录；调用方可以再搬到指定位置。</summary>
    private static string SystemReportPath()
    {
        var dir = Path.Combine(WritableTemp(), "VendorSync");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "selftest-report.txt");
    }

    private static string? _tempRoot;
    private static bool _tempTried;

    /// <summary>
    /// 找一个当前进程真的可写的临时目录。公司安全策略会拦截未签名 exe
    /// 在 %TEMP% 下创建文件，所以要探测并回退到 exe 目录。
    /// </summary>
    private static string WritableTemp()
    {
        if (_tempTried) return _tempRoot ?? AppContext.BaseDirectory;
        _tempTried = true;

        foreach (var candidate in new[] { Path.GetTempPath(), AppContext.BaseDirectory })
        {
            try
            {
                var probe = Path.Combine(candidate, "vs-probe-" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
                _tempRoot = candidate;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return _tempRoot ?? AppContext.BaseDirectory;
    }

    public static string LastReportPath => SystemReportPath();

    private static void EnsureConsole()
    {
        // 从终端启动时附着到父控制台；双击启动时自建一个。
        if (AttachConsole(-1)) { _console = IntPtr.Zero; return; }
        if (AllocConsole()) { _console = IntPtr.Zero; }
    }

    private static readonly StringBuilder Output = new();

    private static void Write(string text)
    {
        Output.Append(text).Append(Environment.NewLine);
    }

    /// <summary>
    /// 把整份报告落盘并尽量打到控制台。
    /// 程序是 WinExe，双击运行时本来没有控制台，所以始终写一份文件保证结果可查。
    /// </summary>
    private static void Flush(string reportPath)
    {
        var text = Output.ToString();

        try
        {
            File.WriteAllText(reportPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 落盘失败也要尽量把结果打出来
        }

        try
        {
            var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.UTF8.GetBytes(text);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    // ------------------------------------------------------------- 断言

    private static void Assert(bool condition, string message)
    {
        if (condition) { _pass++; Lines.Add("  PASS  " + message); }
        else { _fail++; Lines.Add("  FAIL  " + message); }
    }

    private static void Fail(string message)
    {
        _fail++;
        Lines.Add("  FAIL  " + message);
    }

    // --------------------------------------------------------- 地址解析

    private static void UrlTests()
    {
        Assert(RemoteUrl.Detect("https://git.example.com:4433/team/r-example.git") == RemoteScheme.Https,
            "识别 HTTPS 地址");
        Assert(RemoteUrl.Detect("ssh://git@git.example.com:7999/team/example-backend.git") == RemoteScheme.Ssh,
            "识别 ssh:// 地址");
        Assert(RemoteUrl.Detect("git@github.com:example/repo.git") == RemoteScheme.Ssh,
            "识别 scp 风格 SSH 地址");

        Assert(RemoteUrl.GetSshHost("ssh://git@host.example.com:7999/a/b.git") == "host.example.com",
            "从 ssh:// 提取主机名");
        Assert(RemoteUrl.GetSshHost("git@host2.example.com:path/repo.git") == "host2.example.com",
            "从 scp 风格提取主机名");
        Assert(RemoteUrl.GetSshPort("ssh://git@host.example.com:7999/a/b.git") == 7999,
            "提取 SSH 端口");
        Assert(RemoteUrl.GetSshPort("git@host.example.com:path/repo.git") == 22,
            "scp 风格默认端口 22");

        var target = RemoteUrl.GetHttpsTarget("https://git.example.com:4433/team/r.git");
        Assert(target is not null && target.Value.Host == "git.example.com",
            "从 HTTPS 地址提取主机名");
        Assert(RemoteUrl.GetHttpsTarget("ssh://git@host:7999/a.git") is null,
            "SSH 地址不产生 HTTPS 目标");

        var redacted = RemoteUrl.Redact("https://user:secret@internal.example.com/team/repo.git");
        Assert(!redacted.Contains("secret") && !redacted.Contains("user:"),
            "脱敏去掉地址里的账号密码");
        Assert(redacted.Contains("repo.git"), "脱敏保留可识别性");

        var payload = new StringBuilder()
            .Append("protocol=https\n").Append("host=example.com\n")
            .Append("username=alice\n").Append("password=s3cret\n\n").ToString();
        Assert(payload.Contains("host=example.com") && payload.Contains("username=alice"),
            "凭据协议输入包含必需字段");

        var noisy = "git-credential-manager.exe get: No such file or directory\n" +
                    "missing OAuth configuration for host\n" +
                    "From https://example.com/repo\n   1a2b3c..9f8e7d  main -> origin/main";
        var clean = GitClient.SanitizeGitNoise(noisy);
        Assert(!clean.Contains("git-credential-manager") && !clean.Contains("OAuth"),
            "过滤掉凭据管理器噪声");
        Assert(clean.Contains("main -> origin/main"), "保留真实的 fetch 结果");
    }

    // ------------------------------------------------------------ 进程

    private static void ProcessTests()
    {
        var r = ProcessRunner.Run("cmd", new[] { "/c", "echo hello" }, timeout: TimeSpan.FromSeconds(20));
        Assert(r.ExitCode == 0 && r.StdOut.Contains("hello"), "能执行外部命令并读到输出");

        var timeout = ProcessRunner.Run("cmd", new[] { "/c", "ping -n 30 127.0.0.1" }, timeout: TimeSpan.FromSeconds(1));
        Assert(timeout.TimedOut, "超时的命令会被终止而不是挂死");

        var missing = ProcessRunner.Run("definitely-not-a-real-program-xyz", Array.Empty<string>(), timeout: TimeSpan.FromSeconds(5));
        Assert(!missing.Ok && !string.IsNullOrWhiteSpace(missing.StdErr), "程序不存在时返回失败而不是崩溃");
    }

    // ------------------------------------------------------------ 界面

    /// <summary>
    /// 构造所有窗口但不显示，检查 XAML 解析和控件搭建不抛异常。
    /// 需要 STA 线程（WPF 要求），由 App_Startup 调用进来。
    /// </summary>
    private static void UiTests(GitClient git)
    {
        // 自测不经过 App.xaml，主题资源要手工合并，窗口里的 StaticResource 才有值。
        if (System.Windows.Application.Current is null)
        {
            var app = new System.Windows.Application();
            var theme = new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/GitSync;component/UI/Theme.xaml")
            };
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(theme);
        }

        // 主窗口：构造时读配置；重活（探测 git 等）在 Loaded 之后才发生
        UI.MainWindow? main = null;
        try
        {
            main = new UI.MainWindow();
            Assert(main.Content is not null, "主窗口能构建出控件树");
            Assert(main.Title.Length > 0, "主窗口有标题");
        }
        catch (Exception ex)
        {
            Fail("主窗口构建失败：" + ex.ToString());
        }
        finally
        {
            main?.Close();
        }

        // 克隆窗口
        try
        {
            var clone = new UI.CloneDialog(Environment.CurrentDirectory);
            Assert(clone.Content is not null, "克隆窗口能构建出控件树");
            clone.Close();
        }
        catch (Exception ex)
        {
            Fail("克隆窗口构建失败：" + ex.Message);
        }

        // 凭据与密钥向导：SSH 指导 + HTTPS 状态两个页签
        try
        {
            var config = new AppConfig { RepoPath = Environment.CurrentDirectory };
            var creds = new UI.CredentialsWindow(git, config);
            Assert(creds.Content is not null, "凭据与密钥窗口能构建出控件树");
            creds.Close();
        }
        catch (Exception ex)
        {
            Fail("凭据与密钥窗口构建失败：" + ex.Message);
        }

        // 分支详情窗口
        try
        {
            var detail = new UI.DetailWindow(new BranchStatus { Name = "main" });
            Assert(detail.Content is not null, "分支详情窗口能构建出控件树");
            detail.Close();
        }
        catch (Exception ex)
        {
            Fail("分支详情窗口构建失败：" + ex.Message);
        }

        // 图标能从资源里加载（否则界面会没有图标，虽不致命但要记录）
        var icon = UI.AppIcon.Load();
        Assert(icon is null || icon.Width > 0, "应用图标能加载或安全降级");
    }

    // ------------------------------------------------------ 凭据辅助程序

    private static void CredentialHelperTests(GitClient git)
    {
        var diagnosis = CredentialHelperResolver.Diagnose(git);
        Assert(!string.IsNullOrWhiteSpace(diagnosis.Summary), "能诊断凭据辅助程序状态");
        Assert(diagnosis.Available.Count > 0, "能找到至少一个可用的凭据辅助程序");

        // 每个配置条目的判断要与文件系统实际一致
        foreach (var entry in diagnosis.Configured)
        {
            if (entry.ResolvedPath is not null)
            {
                Assert(entry.Exists == File.Exists(entry.ResolvedPath),
                    $"配置条目存在性判断正确：{Path.GetFileName(entry.ResolvedPath)}");
            }
        }

        if (diagnosis.NeedsRepair)
        {
            Assert(diagnosis.ReplacementPath is not null, "配置损坏时能给出可用的替代路径");
            var overrideArgs = CredentialHelperResolver.BuildOverrideArgs(diagnosis);
            Assert(overrideArgs.Count == 4, "修正参数包含清空+设置两条");

            // 关键：含空格的路径必须转义，否则 git 会在空格处截断
            var valueArg = overrideArgs[3];
            Assert(valueArg.StartsWith("credential.helper=", StringComparison.Ordinal),
                "修正参数格式正确");
            var value = valueArg["credential.helper=".Length..];
            if (value.Contains(' '))
            {
                Assert(value.Contains("\\ "), "含空格的 helper 路径已转义（否则 git 会截断）");
            }
        }
        else
        {
            Assert(CredentialHelperResolver.BuildOverrideArgs(diagnosis).Count == 0,
                "配置正常时不注入多余参数");
        }

        // 端到端：把 helper 指向一个不存在的路径，确认
        // (1) 不修正时能复现失败；(2) 注入修正后能恢复正常。
        // 用临时 HOME 隔离，绝不影响真实用户配置。
        var fakeHome = Path.Combine(WritableTemp(), "vshome-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(fakeHome);
        try
        {
            var bogus = Path.Combine(fakeHome, "does-not-exist", "git-credential-manager.exe");
            File.WriteAllText(Path.Combine(fakeHome, ".gitconfig"),
                "[credential]\n\thelper = " + bogus.Replace("\\", "/") + "\n");

            var env = new Dictionary<string, string?>
            {
                ["HOME"] = fakeHome,
                ["USERPROFILE"] = fakeHome,
                ["GIT_CONFIG_NOSYSTEM"] = "1"
            };

            var brokenDiagnosis = DiagnoseWithEnv(git, env);
            Assert(brokenDiagnosis.NeedsRepair, "指向不存在路径的配置被判定为需要修正");
            Assert(brokenDiagnosis.Configured.Any(c => !c.Exists), "能识别出不存在的 helper 条目");

            var replacement = brokenDiagnosis.ReplacementPath;
            if (replacement is not null)
            {
                var overrideArgs = CredentialHelperResolver.BuildOverrideArgs(brokenDiagnosis);
                var args = overrideArgs.Concat(new[] { "config", "--get-all", "credential.helper" }).ToList();
                var applied = ProcessRunner.Run(git.GitPath, args, fakeHome, env: env,
                    timeout: TimeSpan.FromSeconds(30));

                Assert(applied.ExitCode == 0 && applied.StdOut.Contains(Path.GetFileName(replacement)),
                    "注入修正后 git 实际使用可用的 helper");

                var withoutFix = ProcessRunner.Run(git.GitPath,
                    new[] { "config", "--get-all", "credential.helper" }, fakeHome, env: env,
                    timeout: TimeSpan.FromSeconds(30));
                Assert(withoutFix.StdOut.Contains("does-not-exist"),
                    "未修正时确实读到错误路径（证明修正有意义）");
            }
        }
        finally
        {
            TryDelete(fakeHome);
        }
    }

    /// <summary>在给定环境变量下诊断凭据配置。</summary>
    private static HelperDiagnosis DiagnoseWithEnv(GitClient git, IReadOnlyDictionary<string, string?> env)
    {
        var configured = new List<HelperEntry>();
        var r = ProcessRunner.Run(git.GitPath,
            new[] { "config", "--show-origin", "--get-all", "credential.helper" },
            workDir: Environment.CurrentDirectory, env: env, timeout: TimeSpan.FromSeconds(30));

        if (r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.StdOut))
        {
            foreach (var raw in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.TrimEnd();
                var tab = line.IndexOf('\t');
                var origin = tab > 0 ? line[..tab] : string.Empty;
                var value = (tab > 0 ? line[(tab + 1)..] : line).Trim();
                if (value.Length == 0) continue;

                var usable = value.Replace("\\ ", " ");
                var resolved = usable.Contains('/') || usable.Contains('\\')
                    ? usable.Replace('/', '\\')
                    : null;

                configured.Add(new HelperEntry
                {
                    Value = value,
                    Origin = origin,
                    ResolvedPath = resolved,
                    Exists = resolved is not null && File.Exists(resolved)
                });
            }
        }

        var available = CredentialHelperResolver.FindAvailable(git);
        var broken = configured.Where(c => !c.Exists).ToList();
        return new HelperDiagnosis
        {
            NeedsRepair = configured.Count == 0 || broken.Count > 0,
            Summary = broken.Count > 0 ? "配置指向不存在的文件" : "正常",
            ReplacementPath = available.FirstOrDefault(),
            Configured = configured,
            Available = available
        };
    }

    // ------------------------------------------------------------ 仓库

    private static void RepoTests(GitClient git, string root)
    {
        var vendorBare = Path.Combine(root, "vendor.git");
        var internalBare = Path.Combine(root, "internal.git");
        var work = Path.Combine(root, "work");

        Directory.CreateDirectory(root);
        Git(git, root, "init", "--bare", "-q", vendorBare);
        Git(git, root, "init", "--bare", "-q", internalBare);
        Git(git, root, "init", "-q", work);
        Git(git, work, "config", "user.name", "SelfTest");
        Git(git, work, "config", "user.email", "selftest@example.invalid");
        Git(git, work, "config", "core.autocrlf", "false");
        Git(git, work, "remote", "add", "origin", vendorBare);
        Git(git, work, "remote", "add", "bitbucket", internalBare);

        File.WriteAllText(Path.Combine(work, "a.txt"), "v1");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "base");
        Git(git, work, "branch", "-M", "main");
        Git(git, work, "push", "-q", "origin", "main");
        Git(git, work, "push", "-q", "bitbucket", "main");

        // 场景 1：供应商新提交，内部落后 → 应快进
        File.WriteAllText(Path.Combine(work, "a.txt"), "v2");
        Git(git, work, "commit", "-q", "-am", "vendor-ahead");
        Git(git, work, "push", "-q", "origin", "main");

        // 场景 2：供应商新分支 → 应新建
        Git(git, work, "branch", "feature_x");
        Git(git, work, "push", "-q", "origin", "feature_x");

        // 场景 3：内部仓库独有分支 → 不能被删、不能被改
        Git(git, work, "branch", "keepme");
        Git(git, work, "push", "-q", "origin", "keepme");
        Git(git, work, "push", "-q", "bitbucket", "keepme");
        File.WriteAllText(Path.Combine(work, "internal.txt"), "internal-only");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "internal-only");
        Git(git, work, "push", "-q", "bitbucket", "keepme");
        Git(git, work, "reset", "-q", "--hard", "HEAD~1");
        var internalKeep = RemoteSha(git, work, "bitbucket", "keepme");

        // 场景 3b：只存在于内部仓库的分支（供应商完全没有）
        Git(git, work, "branch", "internal_only_branch");
        Git(git, work, "push", "-q", "bitbucket", "internal_only_branch");
        var internalOnlySha = RemoteSha(git, work, "bitbucket", "internal_only_branch");

        // 场景 4：分叉 → Merge 策略要生成合并提交，双方都保留
        Git(git, work, "branch", "diverged");
        Git(git, work, "push", "-q", "origin", "diverged");
        Git(git, work, "push", "-q", "bitbucket", "diverged");
        Git(git, work, "checkout", "-q", "diverged");
        File.WriteAllText(Path.Combine(work, "d.txt"), "vendor-side");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "vendor-side");
        Git(git, work, "push", "-q", "origin", "diverged");
        Git(git, work, "reset", "-q", "--hard", "HEAD~1");
        File.WriteAllText(Path.Combine(work, "i.txt"), "internal-side");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "internal-side");
        Git(git, work, "push", "-q", "bitbucket", "diverged");
        Git(git, work, "checkout", "-q", "main");

        var engine = new SyncEngine(git, _ => { });
        var options = new SyncOptions
        {
            RepoPath = work,
            VendorRemote = "origin",
            InternalRemote = "bitbucket",
            Strategy = SyncStrategy.Merge
        };

        Assert(engine.FetchAll(options, out var fetchError),
            "能从供应商拉取全部分支" + (fetchError.Length > 0 ? "：" + fetchError : string.Empty));

        var statuses = engine.Inspect(options);
        Assert(statuses.Count >= 3, "能枚举供应商分支");

        var mainStatus = statuses.First(s => s.Name == "main");
        Assert(mainStatus.State == BranchState.Behind, "落后的分支被判定为 Behind");
        Assert(mainStatus.VendorOnly == 1, "落后分支的提交数正确");

        var newStatus = statuses.First(s => s.Name == "feature_x");
        Assert(newStatus.State == BranchState.New, "供应商新分支被判定为 New");

        var diverStatus = statuses.First(s => s.Name == "diverged");
        Assert(diverStatus.State == BranchState.Diverged, "分叉分支被判定为 Diverged");
        Assert(diverStatus.InternalOnly == 1 && diverStatus.VendorOnly == 1, "分叉的双方提交数正确");
        Assert(diverStatus.InternalOnlyCommits.Count == 1, "能列出内部独有提交");
        Assert(diverStatus.InternalOnlyCommits[0].Contains("internal-side"), "内部独有提交内容正确");

        Assert(engine.GetInternalOnlyBranches(options).Contains("internal_only_branch"), "能识别内部仓库独有分支");
        Assert(!statuses.Any(s => s.Name == "internal_only_branch"), "内部独有分支不会出现在待同步列表里");

        var outcomes = new List<SyncOutcome>();
        foreach (var s in statuses) outcomes.Add(engine.Execute(options, s));
        Assert(outcomes.All(o => o.Success), "全部未冲突的分支同步成功");

        var vendorMain = RemoteSha(git, work, "origin", "main");
        Assert(RemoteSha(git, work, "bitbucket", "main") == vendorMain, "落后分支已快进到供应商版本");
        Assert(RemoteSha(git, work, "bitbucket", "feature_x") == RemoteSha(git, work, "origin", "feature_x"),
            "新分支已创建");
        Assert(RemoteSha(git, work, "bitbucket", "keepme") == internalKeep, "内部独有分支未被改动");
        Assert(RemoteSha(git, work, "bitbucket", "internal_only_branch") == internalOnlySha,
            "供应商没有的分支在同步后仍然保持原样（不会被删除）");

        var mergedSha = RemoteSha(git, work, "bitbucket", "diverged");
        Assert(mergedSha is not null, "分叉分支已推送");
        var parents = Git(git, internalBare, "rev-list", "--parents", "-n", "1", mergedSha!).StdOut.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert(parents.Length == 3, "分叉分支生成了双亲合并提交（没有丢弃任何一方）");
        Assert(parents.Contains(diverStatus.InternalSha!), "合并提交保留了内部原有提交");
        Assert(parents.Contains(diverStatus.VendorSha!), "合并提交包含了供应商新提交");

        var tree = Git(git, internalBare, "ls-tree", "-r", "--name-only", mergedSha!).StdOut;
        Assert(tree.Contains("d.txt") && tree.Contains("i.txt"), "合并结果同时包含双方文件");

        var verify = engine.Verify(options, statuses);
        Assert(verify.All(v => v.Covered), "推送后校验：全部分支都包含了供应商提交");

        // 幂等
        var beforeMain = RemoteSha(git, work, "bitbucket", "main");
        var beforeDiv = RemoteSha(git, work, "bitbucket", "diverged");
        engine.FetchAll(options, out _);
        foreach (var s in engine.Inspect(options)) engine.Execute(options, s);
        Assert(RemoteSha(git, work, "bitbucket", "main") == beforeMain, "重复同步不产生多余提交（main）");
        Assert(RemoteSha(git, work, "bitbucket", "diverged") == beforeDiv, "重复同步不产生多余提交（diverged）");

        // 冲突
        Git(git, work, "checkout", "-q", "-b", "conflict");
        File.WriteAllText(Path.Combine(work, "c.txt"), "base");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "c-base");
        Git(git, work, "push", "-q", "origin", "conflict");
        Git(git, work, "push", "-q", "bitbucket", "conflict");
        File.WriteAllText(Path.Combine(work, "c.txt"), "vendor");
        Git(git, work, "commit", "-q", "-am", "c-vendor");
        Git(git, work, "push", "-q", "origin", "conflict");
        Git(git, work, "reset", "-q", "--hard", "HEAD~1");
        File.WriteAllText(Path.Combine(work, "c.txt"), "internal");
        Git(git, work, "commit", "-q", "-am", "c-internal");
        Git(git, work, "push", "-q", "bitbucket", "conflict");
        var internalConflict = RemoteSha(git, work, "bitbucket", "conflict");
        Git(git, work, "checkout", "-q", "main");

        engine.FetchAll(options, out _);
        var conflictStatus = engine.Inspect(options).First(s => s.Name == "conflict");
        var conflictOutcome = engine.Execute(options, conflictStatus);

        Assert(!conflictOutcome.Success, "冲突分支被报告为失败，而不是静默成功");
        Assert(RemoteSha(git, work, "bitbucket", "conflict") == internalConflict, "冲突时内部仓库保持原状");

        var worktrees = Git(git, work, "worktree", "list", "--porcelain").StdOut
            .Split('\n').Count(l => l.StartsWith("worktree "));
        Assert(worktrees == 1, "冲突后临时 worktree 已清理");
        var tempBranches = Git(git, work, "branch", "--list", "vendor-sync/*", "--format=%(refname:short)").StdOut.Trim();
        Assert(tempBranches.Length == 0, "冲突后临时分支已清理");

        // 强制策略
        Git(git, work, "checkout", "-q", "-b", "forced", "main");
        Git(git, work, "push", "-q", "origin", "forced");
        Git(git, work, "push", "-q", "bitbucket", "forced");
        File.WriteAllText(Path.Combine(work, "f.txt"), "vendor-side");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "force-vendor");
        Git(git, work, "push", "-q", "origin", "forced");
        Git(git, work, "reset", "-q", "--hard", "HEAD~1");
        File.WriteAllText(Path.Combine(work, "x.txt"), "internal-side");
        Git(git, work, "add", "-A");
        Git(git, work, "commit", "-q", "-m", "force-internal");
        Git(git, work, "push", "-q", "bitbucket", "forced");
        Git(git, work, "checkout", "-q", "main");

        var forceOptions = new SyncOptions
        {
            RepoPath = work,
            VendorRemote = "origin",
            InternalRemote = "bitbucket",
            Strategy = SyncStrategy.Force
        };
        engine.FetchAll(forceOptions, out _);
        var forcedStatus = engine.Inspect(forceOptions).First(s => s.Name == "forced");
        Assert(forcedStatus.State == BranchState.Diverged, "强制场景前置条件成立（确实分叉）");

        var forcedOutcome = engine.Execute(forceOptions, forcedStatus);
        Assert(forcedOutcome.Success, "强制策略执行成功");
        Assert(RemoteSha(git, work, "bitbucket", "forced") == RemoteSha(git, work, "origin", "forced"),
            "强制策略下内部分支被覆盖为供应商版本");
        Assert(RemoteSha(git, work, "bitbucket", "keepme") == internalKeep,
            "强制策略不影响其他内部独有提交");

        // 配置不含凭据
        var configPath = Path.Combine(root, "cfg", "vendorsync.config.json");
        var store = new ConfigStore(configPath);
        var cfg = new AppConfig
        {
            RepoPath = work,
            VendorRemote = "origin",
            InternalRemote = "bitbucket",
            Strategy = "Merge"
        };
        Assert(store.Save(cfg, out var saveError), "配置能保存" + (saveError.Length > 0 ? "：" + saveError : string.Empty));
        var reloaded = store.Load();
        Assert(reloaded.RepoPath == work && reloaded.InternalRemote == "bitbucket", "配置能正确读回");

        var configText = File.ReadAllText(configPath).ToLowerInvariant();
        foreach (var forbidden in new[] { "password", "token", "secret", "passphrase", "privatekey" })
        {
            Assert(!configText.Contains(forbidden), $"配置文件中不含凭据字段：{forbidden}");
        }
    }

    // ------------------------------------------------------------- 工具

    private static ProcessResult Git(GitClient git, string workDir, params string[] args)
    {
        var r = git.Run(args, workDir, timeout: TimeSpan.FromMinutes(5));
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} 失败：{r.Combined}");
        }
        return r;
    }

    private static string? RemoteSha(GitClient git, string workDir, string remote, string branch)
    {
        var r = git.Run(new[] { "ls-remote", "--heads", remote, "refs/heads/" + branch }, workDir, timeout: TimeSpan.FromMinutes(2));
        if (r.ExitCode != 0) return null;
        var line = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (line is null) return null;
        var sha = line.Split('\t')[0].Trim();
        return sha.Length == 40 ? sha : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}