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
            Emit("配置里没有仓库路径。请先在界面里选好仓库，或手工编辑 " + store.FilePath);
            SaveReport(lines);
            return 2;
        }

        var git = GitClient.Locate();
        if (git is null)
        {
            Emit("找不到 git.exe。");
            SaveReport(lines);
            return 2;
        }

        if (!git.IsRepository(config.RepoPath))
        {
            Emit("不是 git 仓库：" + config.RepoPath);
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
        Emit($"仓库：{config.RepoPath}");
        Emit($"源仓库远程：{config.VendorRemote}    目标仓库远程：{config.InternalRemote}");
        Emit(string.Empty);

        if (!engine.FetchAll(options, out var fetchError))
        {
            Emit("拉取失败：" + fetchError);
            SaveReport(lines);
            return 1;
        }

        var statuses = engine.Inspect(options);
        Emit(string.Empty);
        Emit($"{"分支",-44}{"源",-10}{"目标",-10}{"状态",-8}{"差异"}");
        foreach (var s in statuses)
        {
            var delta = s.State switch
            {
                BranchState.Behind => $"源 +{s.VendorOnly}",
                BranchState.InternalAhead => $"目标 +{s.InternalOnly}",
                BranchState.Diverged => $"目标 +{s.InternalOnly} / 源 +{s.VendorOnly}",
                BranchState.New => "全部新增",
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
            Emit("目标仓库独有分支（不会删除）：" + string.Join(", ", internalOnly));
        }

        if (options.IncludeTags)
        {
            var (vendorTotal, newCount, _) = engine.CountTags(options);
            if (vendorTotal > 0)
            {
                Emit(string.Empty);
                Emit($"标签：源仓库 {vendorTotal} 个，其中 {newCount} 个是目标仓库没有的新标签。");
            }
        }

        var needPush = statuses.Count(s => s.NeedsPush);
        Emit(string.Empty);
        Emit(needPush == 0
            ? "结论：全部分支已包含源仓库最新代码，无需同步。"
            : $"结论：{needPush} 个分支需要上传（其中 {statuses.Count(s => s.State == BranchState.Diverged)} 个分叉）。");

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
