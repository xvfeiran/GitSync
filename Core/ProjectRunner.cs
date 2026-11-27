namespace VendorSync.Core;

public enum SyncStage
{
    Connect,
    Check,
    Push,
    Verify,
    Done
}

/// <summary>进度报告：阶段 + 已完成/总数 + 一句给人看的话。</summary>
public sealed record SyncProgress(SyncStage Stage, int Done, int Total, string Text, SyncOutcome? Outcome = null)
{
    /// <summary>整体进度 0..1：连接 0–10%，检查 10–20%，推送 20–90%，校验 90–100%。</summary>
    public double Fraction => Stage switch
    {
        SyncStage.Connect => 0.05,
        SyncStage.Check => 0.15,
        SyncStage.Push => 0.2 + 0.7 * (Total == 0 ? 1 : (double)Done / Total),
        SyncStage.Verify => 0.95,
        _ => 1
    };
}

/// <summary>一次检查得到的快照。</summary>
public sealed class ProjectSnapshot
{
    public IReadOnlyList<BranchStatus> Statuses { get; init; } = Array.Empty<BranchStatus>();
    public IReadOnlyList<string> InternalOnlyBranches { get; init; } = Array.Empty<string>();
    public int TagTotal { get; init; }
    public List<string> NewTags { get; init; } = new();
    public bool IncludeTags { get; init; }
    public SyncStrategy Strategy { get; init; }

    /// <summary>这个分支本次同步是否会被推送（覆盖策略下目标领先也会被覆盖成与源一致）。</summary>
    public bool NeedsAction(BranchStatus s) => s.NeedsPush || Strategy == SyncStrategy.Force && s.State == BranchState.InternalAhead;

    public int PendingBranches => Statuses.Count(NeedsAction);
    public int NewTagCount => IncludeTags ? NewTags.Count : 0;
    public int PendingCount => PendingBranches + (NewTagCount > 0 ? 1 : 0);
    public bool UpToDate => PendingCount == 0;

    /// <summary>需要人工确认的分支：分叉（以及覆盖策略下的目标领先）。</summary>
    public IReadOnlyList<BranchStatus> Risky(SyncStrategy strategy) => Statuses
        .Where(s => s.State == BranchState.Diverged || strategy == SyncStrategy.Force && s.State == BranchState.InternalAhead)
        .ToList();
}

public sealed class SyncRunResult
{
    public ProjectSnapshot? Snapshot { get; init; }
    public List<SyncOutcome> Outcomes { get; init; } = new();
    public IReadOnlyList<(string Branch, string VendorSha, string? ActualSha, bool Covered)> Verify { get; init; }
        = Array.Empty<(string, string, string?, bool)>();
    public int TagsPushed { get; init; }
    public int TagsFailed { get; init; }
    public bool Cancelled { get; init; }
    public string? Error { get; init; }

    public int Failed => Outcomes.Count(o => !o.Success) + TagsFailed;
    public int Pushed => Outcomes.Count(o => o.Success && o.Action is not (SyncActionKind.Skip or SyncActionKind.None));
    public bool AllCovered => Verify.All(v => v.Covered);
    public bool Succeeded => Error is null && !Cancelled && Failed == 0 && AllCovered;
}

/// <summary>
/// 一个项目的「检查 / 同步」编排。与界面无关：
/// 进度通过 <see cref="IProgress{T}"/> 报告，风险确认通过回调交给调用方。
/// </summary>
public sealed class ProjectRunner
{
    private readonly GitClient _git;
    private readonly Action<string> _log;

    public ProjectRunner(GitClient git, Action<string> log)
    {
        _git = git;
        _log = log;
    }

    /// <summary>只读检查：拉取两个远程并比对。失败时返回 null 并给出大白话错误。</summary>
    public ProjectSnapshot? Inspect(SyncOptions options, out string error, IProgress<SyncProgress>? progress = null)
    {
        var engine = new SyncEngine(_git, _log);
        progress?.Report(new SyncProgress(SyncStage.Connect, 0, 0, Loc.T("Progress.Connect")));
        if (!engine.FetchAll(options, out error)) return null;

        progress?.Report(new SyncProgress(SyncStage.Check, 0, 0, Loc.T("Progress.Check")));
        IReadOnlyList<BranchStatus> statuses;
        IReadOnlyList<string> internalOnly;
        (int, int, List<string>) tags;
        try
        {
            statuses = engine.Inspect(options);
            internalOnly = engine.GetInternalOnlyBranches(options);
            tags = options.IncludeTags ? engine.CountTags(options) : (0, 0, new List<string>());
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            _log(error);
            return null;
        }
        foreach (var b in internalOnly) _log(Loc.F("Log.InternalOnlyBranch", b));

        return new ProjectSnapshot
        {
            Statuses = statuses,
            InternalOnlyBranches = internalOnly,
            TagTotal = tags.Item1,
            NewTags = tags.Item3,
            IncludeTags = options.IncludeTags,
            Strategy = options.Strategy
        };
    }

    /// <summary>
    /// 完整同步：重新检查 → 风险确认 → 推送 → 标签 → 独立校验。
    /// <paramref name="confirmRisky"/> 返回 false 时不做任何推送。
    /// </summary>
    public SyncRunResult Sync(
        SyncOptions options,
        Func<IReadOnlyList<BranchStatus>, bool> confirmRisky,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var snapshot = Inspect(options, out var error, progress);
        if (snapshot is null) return new SyncRunResult { Error = error };

        if (snapshot.UpToDate)
        {
            _log(Loc.T("Log.AllUpToDate"));
            progress?.Report(new SyncProgress(SyncStage.Done, 0, 0, Loc.T("Progress.UpToDate")));
            return new SyncRunResult { Snapshot = snapshot };
        }

        var risky = snapshot.Risky(options.Strategy);
        if (risky.Count > 0 && !confirmRisky(risky))
        {
            _log(Loc.T("Log.UserCancelled"));
            return new SyncRunResult { Snapshot = snapshot, Cancelled = true };
        }

        var engine = new SyncEngine(_git, _log);
        var work = snapshot.Statuses.Where(s => s.State != BranchState.UpToDate).ToList();
        var total = work.Count(snapshot.NeedsAction);
        var outcomes = new List<SyncOutcome>();
        var done = 0;
        progress?.Report(new SyncProgress(SyncStage.Push, 0, total, Loc.T("Progress.PushStart")));

        foreach (var s in work)
        {
            if (ct.IsCancellationRequested)
            {
                _log(Loc.T("Log.Cancelled"));
                return new SyncRunResult { Snapshot = snapshot, Outcomes = outcomes, Cancelled = true };
            }
            var outcome = engine.Execute(options, s);
            outcomes.Add(outcome);
            if (outcome.Action is SyncActionKind.Skip or SyncActionKind.None) continue;
            done++;
            progress?.Report(new SyncProgress(SyncStage.Push, done, Math.Max(total, done), Loc.F("Progress.Pushing", s.Name), outcome));
        }

        int tagsPushed = 0, tagsFailed = 0;
        if (options.IncludeTags && snapshot.NewTagCount > 0 && !ct.IsCancellationRequested)
        {
            progress?.Report(new SyncProgress(SyncStage.Push, done, Math.Max(total, done), Loc.F("Progress.PushTags", snapshot.NewTagCount)));
            (tagsPushed, tagsFailed) = engine.SyncTags(options);
            _log(tagsFailed == 0 ? Loc.F("Log.TagsDone", tagsPushed) : Loc.F("Log.TagsPartial", tagsPushed, tagsFailed));
        }

        progress?.Report(new SyncProgress(SyncStage.Verify, 0, 0, Loc.T("Progress.Verify")));
        var verify = engine.Verify(options, snapshot.Statuses);
        foreach (var v in verify.Where(v => !v.Covered)) _log(Loc.F("Log.VerifyFail", v.Branch));
        progress?.Report(new SyncProgress(SyncStage.Done, done, total, Loc.T("Progress.Done")));

        return new SyncRunResult
        {
            Snapshot = snapshot,
            Outcomes = outcomes,
            Verify = verify,
            TagsPushed = tagsPushed,
            TagsFailed = tagsFailed
        };
    }
}
