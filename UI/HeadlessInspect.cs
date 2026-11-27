using VendorSync.Core;

namespace VendorSync.UI;

/// <summary>
/// 只读诊断模式（--inspect）：用配置里的仓库执行「拉取 + 比对」，把结果打到控制台，
/// 不执行任何推送。这是界面「检查差异」的等价命令行入口。
/// </summary>
public static class HeadlessInspect
{
    public static int Run()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }

        var lines = new List<string>();
        void Emit(string text)
        {
            lines.Add(text);
            Console.WriteLine(text);
            AppLog.Write(text);
        }

        var store = new ConfigStore();
        var config = store.Load();

        if (string.IsNullOrWhiteSpace(config.RepoPath))
        {
            Emit(Loc.F("HI.NoRepo", store.FilePath));
            SaveReport(lines);
            return 2;
        }

        var git = GitClient.Locate();
        if (git is null)
        {
            Emit(Loc.T("HI.NoGit"));
            SaveReport(lines);
            return 2;
        }

        if (!git.IsRepository(config.RepoPath))
        {
            Emit(Loc.F("HI.NotRepo", config.RepoPath));
            SaveReport(lines);
            return 2;
        }

        var options = new SyncOptions
        {
            RepoPath = config.RepoPath,
            VendorRemote = config.VendorRemote,
            InternalRemote = config.InternalRemote,
            Strategy = config.StrategyValue,
            IncludeTags = config.IncludeTags
        };

        var engine = new SyncEngine(git, message => Emit("  " + message));
        Emit(Loc.F("HI.Repo", config.RepoPath));
        Emit(Loc.F("HI.Remotes", config.VendorRemote, config.InternalRemote));
        Emit(string.Empty);

        if (!engine.FetchAll(options, out var fetchError))
        {
            Emit(Loc.F("HI.FetchFail", fetchError));
            SaveReport(lines);
            return 1;
        }

        var statuses = engine.Inspect(options);
        Emit(string.Empty);
        Emit(string.Format("{0,-44}{1,-10}{2,-10}{3,-8}{4}",
            Loc.T("HI.ColBranch"), Loc.T("HI.ColSource"), Loc.T("HI.ColTarget"),
            Loc.T("HI.ColState"), Loc.T("HI.ColDelta")));
        foreach (var s in statuses)
        {
            var delta = s.State switch
            {
                BranchState.Behind => Loc.F("HI.Delta.Behind", s.VendorOnly),
                BranchState.InternalAhead => Loc.F("HI.Delta.InternalAhead", s.InternalOnly),
                BranchState.Diverged => Loc.F("HI.Delta.Diverged", s.InternalOnly, s.VendorOnly),
                BranchState.New => Loc.T("HI.Delta.New"),
                _ => "—"
            };
            var pad = 44 - s.Name.Length;
            Emit(s.Name + new string(' ', Math.Max(1, pad)) + s.VendorShort.PadRight(10) +
                 s.InternalShort.PadRight(10) + s.StateLabel.PadRight(8) + delta);
        }

        var internalOnly = engine.GetInternalOnlyBranches(options);
        if (internalOnly.Count > 0)
        {
            Emit(string.Empty);
            Emit(Loc.F("HI.InternalOnly", string.Join(", ", internalOnly)));
        }

        if (options.IncludeTags)
        {
            var (vendorTotal, newCount, _) = engine.CountTags(options);
            if (vendorTotal > 0)
            {
                Emit(string.Empty);
                Emit(Loc.F("HI.Tags", vendorTotal, newCount));
            }
        }

        var needPush = statuses.Count(s => s.NeedsPush);
        Emit(string.Empty);
        Emit(needPush == 0
            ? Loc.T("HI.ConclusionOk")
            : Loc.F("HI.ConclusionPending", needPush, statuses.Count(s => s.State == BranchState.Diverged)));

        SaveReport(lines);
        return 0;
    }

    /// <summary>把只读诊断结果另存为 UTF-8 文件，避免控制台代码页导致中文乱码。</summary>
    private static void SaveReport(List<string> lines)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "VendorSync");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "inspect-report.txt"),
                string.Join(Environment.NewLine, lines) + Environment.NewLine,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
