using System.Text;

namespace VendorSync.Core;

public enum BranchState
{
    UpToDate,
    Behind,
    New,
    InternalAhead,
    Diverged,
    VendorGone,
    Unknown
}

public enum SyncStrategy
{
    /// <summary>分叉时生成合并提交，双方提交都保留。</summary>
    Merge,
    /// <summary>分叉时用源仓库版本覆盖目标仓库（--force-with-lease）。</summary>
    Force
}

public enum SyncActionKind
{
    None,
    Skip,
    CreateBranch,
    FastForward,
    MergeAndPush,
    ForcePush,
    Failed
}

public sealed class BranchStatus
{
    public string Name { get; init; } = string.Empty;
    public string? VendorSha { get; init; }
    public string? InternalSha { get; init; }
    public BranchState State { get; init; }
    public int InternalOnly { get; init; }
    public int VendorOnly { get; init; }
    public List<string> InternalOnlyCommits { get; init; } = new();
    public List<string> VendorOnlyCommits { get; init; } = new();

    public string VendorShort => Short(VendorSha);
    public string InternalShort => Short(InternalSha);

    public static string Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? "--------" : sha[..Math.Min(8, sha.Length)];

    public string StateLabel => State switch
    {
        BranchState.UpToDate => "一致",
        BranchState.Behind => "落后",
        BranchState.New => "新分支",
        BranchState.InternalAhead => "目标领先",
        BranchState.Diverged => "分叉",
        BranchState.VendorGone => "源已删",
        _ => "未知"
    };

    public bool NeedsPush => State is BranchState.Behind or BranchState.New or BranchState.Diverged;
}

public sealed class SyncOutcome
{
    public string Branch { get; init; } = string.Empty;
    public BranchState State { get; init; }
    public SyncActionKind Action { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ResultSha { get; init; }
}

public sealed class SyncOptions
{
    public required string RepoPath { get; init; }
    public string VendorRemote { get; init; } = "origin";
    public string InternalRemote { get; init; } = "bitbucket";
    public SyncStrategy Strategy { get; init; } = SyncStrategy.Merge;
    public IReadOnlyList<string>? OnlyBranches { get; init; }
    public bool IncludeTags { get; init; }
    public bool FetchFirst { get; init; } = true;
}

/// <summary>
/// 同步核心。主工作区永远不做 checkout/reset/clean：
/// 需要合并时打开一个临时 worktree，完成后无论成败都清理掉。
/// </summary>
public sealed class SyncEngine
{
    private readonly GitClient _git;
    private readonly Action<string> _log;

    public SyncEngine(GitClient git, Action<string> log)
    {
        _git = git;
        _log = log;
    }

    private void Info(string message) => _log(message);

    // ------------------------------------------------------------------ 拉取

    public bool FetchAll(SyncOptions options, out string error)
    {
        error = string.Empty;

        // 两个远程互不依赖，并行拉取，快进检查启动速度。
        Info($"并行拉取源仓库 {options.VendorRemote} 与目标仓库 {options.InternalRemote}…");
        var vendorTask = Task.Run(() => _git.Fetch(options.RepoPath, options.VendorRemote, tags: options.IncludeTags));
        var internalTask = Task.Run(() => _git.Run(
            new[] { "fetch", options.InternalRemote, "--prune", "--no-write-fetch-head" },
            options.RepoPath, timeout: TimeSpan.FromMinutes(20)));

        ProcessResult vendorFetch, internalFetch;
        try { vendorFetch = vendorTask.GetAwaiter().GetResult(); }
        catch (Exception ex) { error = $"拉取源仓库失败：{ex.Message}"; Info(error); return false; }
        foreach (var line in GitClient.SanitizeGitNoise(vendorFetch.Combined).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Info("  " + line);
        }
        if (vendorFetch.ExitCode != 0)
        {
            var (_, problem, hint) = GitProblem.Explain(vendorFetch, options.VendorRemote);
            error = $"拉取源仓库失败：{problem}\n{hint}";
            Info(error);
            return false;
        }
        Info("  源仓库已更新。");

        try { internalFetch = internalTask.GetAwaiter().GetResult(); }
        catch (Exception ex) { error = $"读取目标仓库失败：{ex.Message}"; Info(error); return false; }
        if (internalFetch.ExitCode != 0)
        {
            var (_, problem, hint) = GitProblem.Explain(internalFetch, options.InternalRemote);
            error = $"读取目标仓库失败：{problem}\n{hint}";
            Info(error);
            return false;
        }
        Info("  目标仓库已更新。");
        return true;
    }

    // ------------------------------------------------------------ 分支比对

    public IReadOnlyList<BranchStatus> Inspect(SyncOptions options)
    {
        var vendorBranches = _git.GetRemoteBranches(options.RepoPath, options.VendorRemote);
        if (vendorBranches.Count == 0)
        {
            throw new InvalidOperationException($"源仓库远程 {options.VendorRemote} 上没有找到任何分支。");
        }

        if (options.OnlyBranches is { Count: > 0 })
        {
            vendorBranches = vendorBranches
                .Where(b => options.OnlyBranches.Contains(b, StringComparer.Ordinal))
                .ToList();
        }

        var list = new List<BranchStatus>();
        foreach (var branch in vendorBranches)
        {
            list.Add(InspectBranch(options, branch));
        }
        return list;
    }

    private BranchStatus InspectBranch(SyncOptions options, string branch)
    {
        var vendorRef = $"refs/remotes/{options.VendorRemote}/{branch}";
        var internalRef = $"refs/remotes/{options.InternalRemote}/{branch}";
        var vendorSha = _git.ResolveRef(options.RepoPath, vendorRef);
        var internalSha = _git.ResolveRef(options.RepoPath, internalRef);

        if (vendorSha is null)
        {
            return new BranchStatus { Name = branch, InternalSha = internalSha, State = BranchState.VendorGone };
        }
        if (internalSha is null)
        {
            return new BranchStatus { Name = branch, VendorSha = vendorSha, State = BranchState.New };
        }
        if (vendorSha == internalSha)
        {
            return new BranchStatus { Name = branch, VendorSha = vendorSha, InternalSha = internalSha, State = BranchState.UpToDate };
        }

        var (internalOnly, vendorOnly) = _git.GetAheadBehind(options.RepoPath, internalRef, vendorRef);
        var state = internalOnly == 0 ? BranchState.Behind
            : vendorOnly == 0 ? BranchState.InternalAhead
            : BranchState.Diverged;

        var status = new BranchStatus
        {
            Name = branch,
            VendorSha = vendorSha,
            InternalSha = internalSha,
            State = state,
            InternalOnly = Math.Max(0, internalOnly),
            VendorOnly = Math.Max(0, vendorOnly)
        };

        if (state == BranchState.Diverged)
        {
            status.InternalOnlyCommits.AddRange(_git.GetCommits(options.RepoPath, $"{vendorRef}..{internalRef}"));
            status.VendorOnlyCommits.AddRange(_git.GetCommits(options.RepoPath, $"{internalRef}..{vendorRef}"));
        }
        return status;
    }

    public IReadOnlyList<string> GetInternalOnlyBranches(SyncOptions options)
    {
        var vendor = _git.GetRemoteBranches(options.RepoPath, options.VendorRemote);
        var vendorSet = new HashSet<string>(vendor, StringComparer.Ordinal);
        return _git.GetRemoteBranches(options.RepoPath, options.InternalRemote)
            .Where(b => !vendorSet.Contains(b))
            .ToList();
    }

    // ---------------------------------------------------------------- 执行

    public SyncOutcome Execute(SyncOptions options, BranchStatus status)
    {
        try
        {
            return status.State switch
            {
                BranchState.UpToDate => Skip(status, "两边一致，无需上传"),
                BranchState.InternalAhead => options.Strategy == SyncStrategy.Force
                    ? ForcePush(options, status)
                    : Skip(status, $"目标仓库领先 {status.InternalOnly} 个提交，不需要上传"),
                BranchState.VendorGone => Skip(status, "源仓库端已删除该分支，未做处理（不自动删除目标仓库对应分支）"),
                BranchState.New => PushDirect(options, status, SyncActionKind.CreateBranch),
                BranchState.Behind => PushDirect(options, status, SyncActionKind.FastForward),
                BranchState.Diverged => options.Strategy == SyncStrategy.Force
                    ? ForcePush(options, status)
                    : MergeAndPush(options, status),
                _ => Fail(status, "无法判断分支状态")
            };
        }
        catch (Exception ex)
        {
            return Fail(status, ex.Message);
        }
    }

    private SyncOutcome Skip(BranchStatus s, string message)
    {
        Info($"[跳过] {s.Name}：{message}");
        return new SyncOutcome { Branch = s.Name, State = s.State, Action = SyncActionKind.Skip, Success = true, Message = message };
    }

    private SyncOutcome Fail(BranchStatus s, string message)
    {
        Info($"[失败] {s.Name}：{message}");
        return new SyncOutcome { Branch = s.Name, State = s.State, Action = SyncActionKind.Failed, Success = false, Message = message };
    }

    private SyncOutcome PushDirect(SyncOptions options, BranchStatus s, SyncActionKind kind)
    {
        var refspec = $"refs/remotes/{options.VendorRemote}/{s.Name}:refs/heads/{s.Name}";
        Info($"[上传] {s.Name}：{s.StateLabel}，推送 {s.VendorShort} …");

        var r = _git.Run(new[] { "push", options.InternalRemote, refspec }, options.RepoPath, timeout: TimeSpan.FromMinutes(10));
        if (r.ExitCode != 0)
        {
            var (_, problem, hint) = GitProblem.Explain(r, options.InternalRemote);
            Info("  " + GitClient.SanitizeGitNoise(r.Combined));
            return Fail(s, $"{problem}\n{hint}");
        }

        Info($"  [成功] {s.Name} → {s.VendorShort}");
        return new SyncOutcome
        {
            Branch = s.Name, State = s.State, Action = kind, Success = true,
            Message = kind == SyncActionKind.CreateBranch ? "已新建目标分支" : $"已快进 {s.VendorOnly} 个提交",
            ResultSha = s.VendorSha
        };
    }

    private SyncOutcome ForcePush(SyncOptions options, BranchStatus s)
    {
        var internalRef = $"refs/remotes/{options.InternalRemote}/{s.Name}";
        var expected = _git.ResolveRef(options.RepoPath, internalRef);
        if (expected is null) return Fail(s, "读不到目标分支当前哈希，放弃强制推送。");

        // 不做备份：目标仓库独有的提交直接丢弃。原哈希写进日志，便于必要时人工找回。
        Info($"[强制] {s.Name}：用源仓库 {s.VendorShort} 覆盖目标仓库（原指向 {BranchStatus.Short(expected)}，保护性 lease）…");
        if (s.InternalOnlyCommits.Count > 0)
        {
            Info("  将被丢弃的目标仓库提交（不备份）：");
            foreach (var c in s.InternalOnlyCommits) Info("    " + c);
        }

        var refspec = $"refs/remotes/{options.VendorRemote}/{s.Name}:refs/heads/{s.Name}";
        var lease = $"--force-with-lease=refs/heads/{s.Name}:{expected}";
        var r = _git.Run(new[] { "push", lease, options.InternalRemote, refspec }, options.RepoPath, timeout: TimeSpan.FromMinutes(10));

        if (r.ExitCode != 0)
        {
            var (_, problem, hint) = GitProblem.Explain(r, options.InternalRemote);
            Info("  " + GitClient.SanitizeGitNoise(r.Combined));
            return Fail(s, $"{problem}\n{hint}");
        }

        Info($"  [成功] {s.Name} 已覆盖为 {s.VendorShort}");
        return new SyncOutcome
        {
            Branch = s.Name, State = s.State, Action = SyncActionKind.ForcePush, Success = true,
            Message = "已用源仓库版本覆盖", ResultSha = s.VendorSha
        };
    }

    private SyncOutcome MergeAndPush(SyncOptions options, BranchStatus s)
    {
        var tempBranch = $"vendor-sync/{s.Name}";
        var tempDir = Path.Combine(TempRoot.Dir(), "vendor-sync-" + Guid.NewGuid().ToString("N")[..8]);
        var internalRef = $"refs/remotes/{options.InternalRemote}/{s.Name}";

        Info($"[合并] {s.Name}：目标仓库独有 {s.InternalOnly} 个 / 源仓库独有 {s.VendorOnly} 个，生成合并提交…");
        foreach (var c in s.InternalOnlyCommits) Info("    目标仓库提交: " + c);

        try
        {
            // 清理上次异常退出可能留下的同名资源
            _git.Run(new[] { "branch", "-D", tempBranch }, options.RepoPath, timeout: TimeSpan.FromSeconds(30));
            _git.Run(new[] { "worktree", "prune" }, options.RepoPath, timeout: TimeSpan.FromSeconds(30));

            var add = _git.Run(new[] { "worktree", "add", "-b", tempBranch, tempDir, internalRef },
                options.RepoPath, timeout: TimeSpan.FromMinutes(5));
            if (add.ExitCode != 0)
            {
                return Fail(s, "无法创建临时工作区：" + GitClient.SanitizeGitNoise(add.Combined));
            }

            var message = $"merge: 同步 {options.VendorRemote}/{s.Name} → {options.InternalRemote}/{s.Name}（源 {s.VendorShort}）";
            var merge = _git.Run(new[] { "merge", "--no-ff", $"{options.VendorRemote}/{s.Name}", "-m", message },
                tempDir, timeout: TimeSpan.FromMinutes(10));

            if (merge.ExitCode != 0)
            {
                var conflicted = _git.Run(new[] { "diff", "--name-only", "--diff-filter=U" }, tempDir, timeout: TimeSpan.FromMinutes(2));
                _git.Run(new[] { "merge", "--abort" }, tempDir, timeout: TimeSpan.FromMinutes(2));

                var files = conflicted.StdOut
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var detail = files.Length > 0
                    ? "冲突文件：" + string.Join(", ", files)
                    : GitClient.SanitizeGitNoise(merge.Combined);
                return Fail(s, "合并冲突，已放弃本次合并（目标仓库保持不变）。" + detail);
            }

            var head = _git.ResolveRef(tempDir, "HEAD");
            var push = _git.Run(new[] { "push", options.InternalRemote, $"HEAD:refs/heads/{s.Name}" },
                tempDir, timeout: TimeSpan.FromMinutes(10));
            if (push.ExitCode != 0)
            {
                var (_, problem, hint) = GitProblem.Explain(push, options.InternalRemote);
                return Fail(s, $"合并提交已生成但推送被拒：{problem}\n{hint}");
            }

            Info($"  [成功] {s.Name} 已合并并上传 {BranchStatus.Short(head)}");
            return new SyncOutcome
            {
                Branch = s.Name, State = s.State, Action = SyncActionKind.MergeAndPush, Success = true,
                Message = "已合并双方提交并上传", ResultSha = head
            };
        }
        finally
        {
            _git.Run(new[] { "worktree", "remove", "--force", tempDir }, options.RepoPath, timeout: TimeSpan.FromMinutes(5));
            _git.Run(new[] { "branch", "-D", tempBranch }, options.RepoPath, timeout: TimeSpan.FromMinutes(2));
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { /* 交给系统临时目录清理 */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ---------------------------------------------------------------- 标签

    /// <summary>
    /// 标签统计：源仓库共多少个标签、其中多少个是目标仓库没有的新标签。
    /// 只读，不推送。
    /// </summary>
    public (int VendorTotal, int NewCount, List<string> NewTags) CountTags(SyncOptions options)
    {
        var vendorTags = _git.ListRemoteTags(options.RepoPath, options.VendorRemote);
        var internalTags = new HashSet<string>(
            _git.ListRemoteTags(options.RepoPath, options.InternalRemote), StringComparer.Ordinal);

        var newTags = vendorTags.Where(t => !internalTags.Contains(t)).ToList();
        return (vendorTags.Count, newTags.Count, newTags);
    }

    /// <summary>
    /// 把目标仓库缺少的标签补推过去。只推新标签，
    /// 绝不覆盖目标仓库已有的同名标签。
    /// </summary>
    public (int Pushed, int Failed) SyncTags(SyncOptions options)
    {
        var (_, _, newTags) = CountTags(options);
        int pushed = 0, failed = 0;
        foreach (var tag in newTags)
        {
            if (_git.PushTag(options.RepoPath, options.InternalRemote, tag))
            {
                Info($"  [标签] 新增 {tag}");
                pushed++;
            }
            else
            {
                Info($"  ⚠ 标签 {tag} 推送失败");
                failed++;
            }
        }
        return (pushed, failed);
    }

    // ---------------------------------------------------------------- 校验

    /// <summary>
    /// 推送后独立核对服务器真实状态。判据是「源仓库提交是否已被目标仓库包含」，
    /// 所以目标仓库领先（例如已合并过）也算达标。
    /// </summary>
    public IReadOnlyList<(string Branch, string VendorSha, string? ActualSha, bool Covered)> Verify(SyncOptions options, IReadOnlyList<BranchStatus> statuses)
    {
        var heads = _git.ListRemoteHeads(options.RepoPath, options.InternalRemote);
        var results = new List<(string, string, string?, bool)>();

        foreach (var s in statuses)
        {
            heads.TryGetValue(s.Name, out var actual);
            var vendor = _git.ResolveRef(options.RepoPath, $"refs/remotes/{options.VendorRemote}/{s.Name}");
            bool covered = false;

            if (vendor is not null && actual is not null)
            {
                covered = actual == vendor || _git.IsAncestor(options.RepoPath, vendor, actual);
            }
            results.Add((s.Name, vendor ?? string.Empty, actual, covered));
        }
        return results;
    }
}