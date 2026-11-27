using System.ComponentModel;
using System.Globalization;

namespace VendorSync.Core;

/// <summary>
/// 极简双语文案表：中文为基准文案（与既有界面逐字一致），英文给地道翻译。
/// key 用稳定的英文标识；T() 缺 key 时回退中文，再回退 key 本身。
/// 语言切换通过 LanguageChanged + INotifyPropertyChanged("Item[]") 广播，
/// 由 UI 的 LocExtension 绑定自动刷新。
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> Zh = new();
    private static readonly Dictionary<string, string> En = new();

    /// <summary>MainWindow 注入的保存回调（写进 AppConfig 并持久化），避免 Core 依赖 UI。</summary>
    public static Action<string>? LanguagePersist;

    static Loc()
    {
        Populate();
    }

    private static void Add(string key, string zh, string en)
    {
        Zh[key] = zh;
        En[key] = en;
    }

    // ------------------------------------------------------------- 语言状态

    /// <summary>当前语言："zh" / "en"。默认跟随 Windows 系统 UI 语言。</summary>
    public static string Language { get; private set; } =
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";

    public static void SetLanguage(string lang)
    {
        lang = lang == "en" ? "en" : "zh";
        if (lang == Language) return;
        Language = lang;
        LanguageChanged?.Invoke();
        Instance.RaiseItemChanged();
        try { LanguagePersist?.Invoke(lang); } catch { /* 持久化失败不影响切换 */ }
    }

    /// <summary>切换按钮上显示的“另一门语言”名字。</summary>
    public static string OtherLanguageName => Language == "zh" ? "English" : "中文";

    public static event Action? LanguageChanged;

    // ------------------------------------------------------------- 取词

    public static string T(string key)
    {
        var dict = Language == "en" ? En : Zh;
        if (dict.TryGetValue(key, out var text)) return text;
        if (Zh.TryGetValue(key, out var zhText)) return zhText;
        return key;
    }

    /// <summary>带参数取词：字典里用 {0} 占位。</summary>
    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    public static LocSink Instance { get; } = new();

    /// <summary>供 WPF 绑定的单例：索引器 + Item[] 变更通知。</summary>
    public sealed class LocSink : INotifyPropertyChanged
    {
        public string this[string key] => T(key);

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void RaiseItemChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    // ------------------------------------------------------------- 文案表

    private static void Populate()
    {
        // ---- 通用
        Add("Btn.Cancel", "取消", "Cancel");
        Add("Btn.Close", "关闭", "Close");
        Add("Btn.Save", "保存", "Save");
        Add("Btn.Browse", "浏览…", "Browse…");
        Add("Caption.Info", "提示", "Info");
        Add("Caption.Warn", "注意", "Warning");
        Add("Caption.Error", "出错了", "Error");
        Add("Common.Unknown", "未知", "unknown");
        Add("Lang.SwitchTarget", "English", "中文");

        // ---- 主窗口
        Add("App.Title", "GitSync · 同步项目", "GitSync · Project Sync");
        Add("Menu.OpenFolder", "打开文件夹", "Open folder");
        Add("Menu.Remotes", "远程仓库设置…", "Remote settings…");
        Add("Menu.Remove", "移除项目", "Remove project");
        Add("Log.CopyAll", "复制全部", "Copy all");
        Add("Log.CopyDiag", "复制诊断信息", "Copy diagnostics");
        Add("Log.Clear", "清空", "Clear");
        Add("Log.Expand", "展开 ▲", "Expand ▲");
        Add("Log.Collapse", "收起 ▼", "Collapse ▼");
        Add("Log.Title", "日志", "Log");
        Add("Status.Starting", "正在启动…", "Starting…");
        Add("Btn.Credentials", "凭据与密钥", "Credentials & Keys");
        Add("Btn.Add", "＋ 添加项目", "＋ Add project");
        Add("Home.Title", "同步项目", "Sync Projects");
        Add("Home.Empty", "还没有同步项目。点「＋ 添加项目」，选一个本地仓库或直接克隆一个新仓库。",
            "No sync projects yet. Click “＋ Add project”, pick a local repo, or clone a new one.");
        Add("Detail.Back", "← 返回", "← Back");
        Add("Detail.Target", "目标仓库", "Target repo");
        Add("Detail.Source", "源仓库", "Source repo");
        Add("Detail.Sync", "同步", "Sync");
        Add("Detail.Syncing", "同步中…", "Syncing…");
        Add("Detail.Recheck", "重新检查", "Re-check");
        Add("Detail.Remotes", "远程仓库…", "Remotes…");
        Add("Detail.RemotesTip", "设置这个项目的源 / 目标远程仓库名字和地址",
            "Set the names and URLs of this project's source / target remotes");
        Add("Step.Conn", "① 连接", "① Connect");
        Add("Step.Check", "② 检查差异", "② Check diffs");
        Add("Step.Push", "③ 推送", "③ Push");
        Add("Step.Verify", "④ 校验", "④ Verify");
        Add("Strategy.Force", "以源仓库为准（覆盖目标仓库）", "Source wins (overwrite target)");
        Add("Strategy.Merge", "保留双方提交（合并）", "Keep both sides (merge)");
        Add("Detail.IncludeTags", "同时同步标签", "Also sync tags");
        Add("Branch.OnlyPending", "只看需处理的", "Pending only");
        Add("Branch.OnlyPendingTip", "勾选后只显示需要同步的分支，隐藏已一致的",
            "Show only branches that need syncing; hide the ones already in sync");

        // ---- 主窗口代码
        Add("Startup.LogTitle", "=== 同步项目 ===", "=== Project Sync ===");
        Add("Msg.NoGit", "✖ 没有找到 git.exe。", "✖ git.exe was not found.");
        Add("Msg.GitInstallUrl", "安装地址：{0}", "Download: {0}");
        Add("Status.NoGit", "缺少 git", "git missing");
        Add("Confirm.NoGitText",
            "没有找到 git.exe，本工具无法工作。\r\n\r\n" +
            "请到 Git 官网下载安装「Git for Windows」：\r\n{0}" +
            "\r\n\r\n安装后关闭所有终端和编辑器，再重新打开本工具。\r\n\r\n现在打开下载页面？",
            "git.exe was not found and this tool cannot work.\r\n\r\n" +
            "Please download and install “Git for Windows” from the Git website:\r\n{0}" +
            "\r\n\r\nAfter installing, close all terminals and editors, then restart this tool.\r\n\r\nOpen the download page now?");
        Add("Caption.NoGit", "缺少 Git", "Git Missing");
        Add("Log.DiagOk", "凭据辅助程序：正常（由本机 git 管理）。", "Credential helper: OK (managed by this machine's git).");
        Add("Log.DiagProblem", "⚠ 本机 git 凭据辅助程序配置有问题：{0}", "⚠ This machine's git credential helper is misconfigured: {0}");
        Add("Log.DiagNoReplacement", "✖ 没找到可用的替代程序，HTTPS 凭据将无法保存。请重装 Git for Windows。",
            "✖ No usable replacement was found; HTTPS credentials cannot be saved. Please reinstall Git for Windows.");
        Add("Log.DiagFixed", "✔ 已自动修正（只影响本程序）：{0}", "✔ Fixed automatically (affects this app only): {0}");
        Add("Log.DiagError", "诊断凭据环境时出错：{0}", "Error while diagnosing the credential environment: {0}");
        Add("Status.Ready", "就绪。", "Ready.");
        Add("Status.ReadyChecking", "就绪，正在后台检查 {0} 个项目…", "Ready; checking {0} project(s) in the background…");
        Add("Log.AutoCheckError", "后台自动检查出错：{0}", "Background auto-check failed: {0}");
        Add("Status.AutoCheckDone", "就绪，后台检查完成。", "Ready; background check finished.");
        Add("Home.SummaryFail", "{0} 个项目，{1} 个失败，需要处理。", "{0} project(s), {1} failed, need attention.");
        Add("Home.SummaryPending", "{0} 个项目，{1} 个有待同步。", "{0} project(s), {1} pending sync.");
        Add("Home.SummaryOk", "{0} 个项目，全部已一致。", "{0} project(s), all in sync.");
        Add("Warn.DirMissing", "本地仓库目录不存在，无法设置远程。", "The local repo directory does not exist; cannot configure remotes.");
        Add("Caption.DirMissing", "目录缺失", "Directory Missing");
        Add("Status.RemotesUpdated", "已更新「{0}」的远程仓库。", "Remotes of “{0}” updated.");
        Add("Confirm.RemoveText", "移除项目「{0}」？\r\n\r\n只从本工具的列表里移除，不删除仓库文件。",
            "Remove project “{0}”?\r\n\r\nThis only removes it from the tool's list; repo files are not deleted.");
        Add("Caption.RemoveProject", "移除项目", "Remove Project");
        Add("Info.AddFirst", "先添加一个同步项目，再配置凭据。", "Add a sync project first, then configure credentials.");
        Add("Status.Added", "已添加项目：{0}", "Project added: {0}");
        Add("Branch.Summary", "{0} 个分支，{1} 个需要处理", "{0} branch(es), {1} need attention");
        Add("Branch.SummaryTags", "，{0} 个新标签", ", {0} new tag(s)");
        Add("Summary.SourceBranches", "源仓库 {0} 个分支，{1} 个需要同步。", "Source repo has {0} branch(es), {1} need syncing.");
        Add("Summary.TargetOnly", "  目标仓库多 {0} 个分支（{1}{2}），保留不动，不会被删除或覆盖。",
            "  Target repo has {0} extra branch(es) ({1}{2}); they are kept as-is and will not be deleted or overwritten.");
        Add("Summary.ExtraSuffix", " 等", " et al.");
        Add("Delta.Behind", "源多 {0} 个提交", "{0} commit(s) ahead at source");
        Add("Delta.InternalAhead", "目标多 {0} 个提交", "{0} commit(s) ahead at target");
        Add("Delta.Diverged", "源 +{0} / 目标 +{1}", "source +{0} / target +{1}");
        Add("Delta.New", "新分支", "new branch");
        Add("Plan.Behind", "直接推送（快进）", "Push directly (fast-forward)");
        Add("Plan.New", "在目标仓库新建", "Create on target");
        Add("Plan.Overwrite", "以源为准覆盖 · 先备份", "Overwrite from source · backup first");
        Add("Plan.Merge", "合并后推送", "Merge then push");
        Add("Plan.Keep", "保留", "Keep");
        Add("Plan.VendorGone", "保留，不删除", "Keep; not deleted");
        Add("Plan.UpToDate", "已一致", "In sync");
        Add("Plan.InternalOnly", "保留，不同步不删除", "Kept; not synced, not deleted");
        Add("Result.Pending", "待处理", "Pending");
        Add("Result.UpToDate", "一致", "In sync");
        Add("StateLabel.InternalOnly", "目标独有", "Target only");
        Add("Label.SourceRepo", "源仓库", "Source repo");
        Add("Label.TargetRepo", "目标仓库", "Target repo");
        Add("Log.CredNeeded", "{0}（{1}）需要登录：{2}", "{0} ({1}) needs sign-in: {2}");
        Add("Log.NoCredentials", "未提供凭据，操作取消。", "No credentials provided; operation cancelled.");
        Add("Status.NoCredentials", "缺少凭据，操作已取消。", "Missing credentials; operation cancelled.");
        Add("Status.Checking", "正在检查…", "Checking…");
        Add("Card.MsgUpToDate", "全部已一致，无需同步。", "Everything is in sync; nothing to do.");
        Add("Card.MsgPending", "{0} 个分支需要同步", "{0} branch(es) need syncing");
        Add("Msg.InspectErrorPrefix", "检查出错：{0}", "Check failed: {0}");
        Add("Card.NotChecked", "尚未检查", "Not checked yet");
        Add("Card.NeverRun", "从未运行", "Never run");
        Add("Card.LastCheck", "上次检查：{0}", "Last check: {0}");
        Add("Card.LastSync", "上次同步：{0}", "Last sync: {0}");
        Add("Sync.ConfirmBranch", "【{0}】目标仓库独有 {1} 个 / 源仓库独有 {2} 个提交",
            "[{0}] {1} commit(s) only on target / {2} only on source");
        Add("Sync.ConfirmTargetLine", "    目标: ", "    Target: ");
        Add("Sync.ConfirmForce", "将以源仓库为准覆盖这些分支；被覆盖的提交会先备份成 vendor-backup 标签，随时可以找回。",
            "These branches will be overwritten with the source version; overwritten commits are first backed up as vendor-backup tags and can be restored anytime.");
        Add("Sync.ConfirmMerge", "将生成合并提交，双方提交都保留。", "A merge commit will be created; commits from both sides are kept.");
        Add("Sync.ConfirmContinue", "\r\n继续？", "\r\nContinue?");
        Add("Caption.ConfirmSync", "确认同步", "Confirm Sync");
        Add("Card.Cancelled", "已取消。", "Cancelled.");
        Add("Sync.DonePushed", "同步完成：推送 {0} 项", "Sync finished: pushed {0} item(s)");
        Add("Sync.DoneTags", "，新增 {0} 个标签", ", {0} new tag(s) added");
        Add("Sync.DoneSuffix", "。", ".");
        Add("Sync.PartialFail", "完成，但有 {0} 项失败，见日志。", "Finished, but {0} item(s) failed; see the log.");
        Add("Sync.WarnDetailSuffix", "\r\n\r\n详情见下方日志。", "\r\n\r\nSee the log below for details.");
        Add("Caption.SyncFailed", "检查失败", "Check Failed");
        Add("Msg.SyncErrorPrefix", "同步出错：{0}", "Sync failed: {0}");
        Add("Log.ConfigSaveFail", "配置保存失败：{0}", "Failed to save configuration: {0}");
        Add("Status.LogCopied", "日志已复制到剪贴板", "Log copied to clipboard");
        Add("Status.DiagCopied", "诊断信息已复制，直接发给维护人即可", "Diagnostics copied; just send them to the maintainer");
        Add("Diag.Title", "=== GitSync 诊断信息 ===", "=== GitSync Diagnostics ===");
        Add("Diag.Time", "时间：{0}", "Time: {0}");
        Add("Diag.Project", "项目：{0}", "Project: {0}");
        Add("Diag.Repo", "仓库：{0}", "Repo: {0}");
        Add("Diag.Remotes", "源：{0}    目标：{1}", "Source: {0}    Target: {1}");
        Add("Diag.Git", "git：{0}", "git: {0}");
        Add("Log.OpenUrlFail", "打开安装页面失败：{0}", "Failed to open the download page: {0}");
        Add("Log.InspectError", "检查出错：{0}", "Check failed: {0}");
        Add("Log.SyncError", "同步出错：{0}", "Sync failed: {0}");

        // ---- 分支状态标签（SyncEngine.BranchStatus.StateLabel）
        Add("StateLabel.UpToDate", "一致", "In sync");
        Add("StateLabel.Behind", "落后", "Behind");
        Add("StateLabel.New", "新分支", "New");
        Add("StateLabel.InternalAhead", "目标领先", "Target ahead");
        Add("StateLabel.Diverged", "分叉", "Diverged");
        Add("StateLabel.VendorGone", "源已删", "Deleted at source");
        Add("StateLabel.Unknown", "未知", "Unknown");

        // ---- SyncEngine
        Add("Log.FetchParallel", "并行拉取源仓库 {0} 与目标仓库 {1}…", "Fetching source repo {0} and target repo {1} in parallel…");
        Add("Err.FetchSource", "拉取源仓库失败：{0}", "Failed to fetch source repo: {0}");
        Add("Log.SourceUpdated", "  源仓库已更新。", "  Source repo updated.");
        Add("Err.ReadTarget", "读取目标仓库失败：{0}", "Failed to read target repo: {0}");
        Add("Log.TargetUpdated", "  目标仓库已更新。", "  Target repo updated.");
        Add("Err.InspectNoBranches", "源仓库远程 {0} 上没有找到任何分支。", "No branches found on source remote {0}.");
        Add("Outcome.SkipUpToDate", "两边一致，无需上传", "Both sides match; nothing to push");
        Add("Outcome.SkipInternalAhead", "目标仓库领先 {0} 个提交，不需要上传", "Target repo is {0} commit(s) ahead; nothing to push");
        Add("Outcome.SkipVendorGone", "源仓库端已删除该分支，未做处理（不自动删除目标仓库对应分支）",
            "This branch was deleted at the source; left untouched (the target branch is not auto-deleted)");
        Add("Outcome.UnknownState", "无法判断分支状态", "Cannot determine branch state");
        Add("Log.Skip", "[跳过] {0}：{1}", "[Skip] {0}: {1}");
        Add("Log.Fail", "[失败] {0}：{1}", "[Fail] {0}: {1}");
        Add("Log.Push", "[上传] {0}：{1}，推送 {2} …", "[Push] {0}: {1}, pushing {2}…");
        Add("Log.PushOk", "  [成功] {0} → {1}", "  [OK] {0} → {1}");
        Add("Msg.CreatedBranch", "已新建目标分支", "Created target branch");
        Add("Msg.FastForward", "已快进 {0} 个提交", "Fast-forwarded {0} commit(s)");
        Add("Err.NoTargetSha", "读不到目标分支当前哈希，放弃强制推送。", "Cannot read the target branch's current hash; force push aborted.");
        Add("Log.Force", "[强制] {0}：用源仓库 {1} 覆盖目标仓库（原指向 {2}，保护性 lease）…",
            "[Force] {0}: overwriting target with source {1} (was {2}, protective lease)…");
        Add("Log.ForceDiscard", "  将被丢弃的目标仓库提交（不备份）：", "  Target commits to be discarded (no backup):");
        Add("Log.ForceOk", "  [成功] {0} 已覆盖为 {1}", "  [OK] {0} overwritten to {1}");
        Add("Msg.Overwritten", "已用源仓库版本覆盖", "Overwritten with the source version");
        Add("Log.Merge", "[合并] {0}：目标仓库独有 {1} 个 / 源仓库独有 {2} 个，生成合并提交…",
            "[Merge] {0}: {1} commit(s) only on target / {2} only on source; creating a merge commit…");
        Add("Log.MergeTargetCommit", "    目标仓库提交: ", "    Target commit: ");
        Add("Err.WorktreeFail", "无法创建临时工作区：{0}", "Cannot create a temporary worktree: {0}");
        Add("Err.MergeConflictFiles", "冲突文件：{0}", "Conflicting files: {0}");
        Add("Err.MergeConflict", "合并冲突，已放弃本次合并（目标仓库保持不变）。{0}", "Merge conflict; this merge was aborted (target repo unchanged). {0}");
        Add("Err.MergePushRejected", "合并提交已生成但推送被拒：{0}", "Merge commit was created but the push was rejected: {0}");
        Add("Log.MergeOk", "  [成功] {0} 已合并并上传 {1}", "  [OK] {0} merged and pushed {1}");
        Add("Msg.Merged", "已合并双方提交并上传", "Merged commits from both sides and pushed");
        Add("Log.TagNew", "  [标签] 新增 {0}", "  [Tag] added {0}");
        Add("Log.TagFail", "  ⚠ 标签 {0} 推送失败", "  ⚠ Failed to push tag {0}");

        // ---- ProjectRunner
        Add("Progress.Connect", "正在连接两个仓库…", "Connecting to both repos…");
        Add("Progress.Check", "正在比对分支差异…", "Comparing branch differences…");
        Add("Log.InternalOnlyBranch", "目标仓库独有分支（不会删除）：{0}", "Branches only on the target repo (not deleted): {0}");
        Add("Log.AllUpToDate", "全部内容已一致，无需同步。", "Everything already matches; no sync needed.");
        Add("Progress.UpToDate", "已一致", "In sync");
        Add("Log.UserCancelled", "用户取消了同步。", "Sync cancelled by the user.");
        Add("Log.Cancelled", "已被用户取消。", "Cancelled by the user.");
        Add("Progress.PushStart", "开始推送…", "Starting push…");
        Add("Progress.Pushing", "推送 {0}", "Pushing {0}");
        Add("Progress.PushTags", "推送 {0} 个新标签", "Pushing {0} new tag(s)");
        Add("Log.TagsDone", "  [标签] 完成：新增 {0} 个标签。", "  [Tag] done: {0} tag(s) added.");
        Add("Log.TagsPartial", "  [标签] 新增 {0} 个，{1} 个失败。", "  [Tag] {0} added, {1} failed.");
        Add("Progress.Verify", "正在核对服务器上的真实状态…", "Verifying the actual state on the server…");
        Add("Log.VerifyFail", "  ✖ 校验未通过：{0}", "  ✖ Verification failed: {0}");
        Add("Progress.Done", "完成", "Done");

        // ---- GitProblem.Explain
        Add("Problem.Timeout", "连接 {0} 超时。", "Connection to {0} timed out.");
        Add("Hint.Timeout", "多半是网络没通。连上后重试；仍超时请联系服务器管理员确认端口。",
            "The network is probably down. Retry once connected; if it still times out, ask the server admin to check the port.");
        Add("Problem.SshKey", "服务器不认识你这台机器的 SSH 密钥。", "The server does not recognize this machine's SSH key.");
        Add("Hint.SshKey", "点「凭据与密钥…」→「SSH 密钥」生成密钥并把公钥登记到 Git 服务器，然后点「测试连接」验证。",
            "Click “Credentials & Keys…” → “SSH Keys” to generate a key, register the public key on the Git server, then click “Test connection”.");
        Add("Problem.HostKey", "服务器指纹校验没通过。", "Server fingerprint verification failed.");
        Add("Hint.HostKey", "如果是第一次连接，请先确认指纹无误；如果以前能连现在变了，请联系 IT —— 这可能是中间人攻击。",
            "If this is the first connection, confirm the fingerprint is correct; if it changed since before, contact IT — this could be a man-in-the-middle attack.");
        Add("Problem.HttpsCred", "HTTPS 地址需要账号或令牌，但当前拿不到。", "The HTTPS URL needs an account or token, but none is available right now.");
        Add("Hint.HttpsCred", "请把该站点的账号 / 个人访问令牌（PAT）保存进你本机的 Git 凭据管理器（Windows 凭据管理器），保存后重试即可。",
            "Save the account / personal access token (PAT) for this site into your local Git credential manager (Windows Credential Manager), then retry.");
        Add("Problem.Dns", "域名解析不出来。", "The host name cannot be resolved.");
        Add("Hint.Dns", "通常是没连公司网络或 VPN。连上后重试。", "Usually the corporate network or VPN is not connected. Connect and retry.");
        Add("Problem.Network", "连不上服务器 {0}。", "Cannot reach server {0}.");
        Add("Hint.Network", "检查网络、VPN、端口和防火墙。", "Check the network, VPN, port, and firewall.");
        Add("Problem.RepoMissing", "仓库不存在或当前账号没有权限。", "The repository does not exist, or the current account lacks permission.");
        Add("Hint.RepoMissing", "先核对地址拼写，再确认该账号在这个仓库上有读/写权限。",
            "Double-check the URL spelling, then confirm the account has read/write access to this repository.");
        Add("Problem.Ownership", "这个目录属于别的 Windows 用户，git 拒绝操作。", "This directory belongs to another Windows user; git refuses to operate on it.");
        Add("Hint.Ownership", "在确认目录可信后执行：git config --global --add safe.directory <该目录路径>；或把目录权限改回当前用户。",
            "After confirming the directory is trusted, run: git config --global --add safe.directory <that directory>; or change the directory's ownership back to the current user.");
        Add("Problem.NotARepo", "这个目录不是 git 仓库。", "This directory is not a git repository.");
        Add("Hint.NotARepo", "确认路径指向仓库根目录，或者先克隆一次。", "Make sure the path points to the repo root, or clone it first.");
        Add("Problem.Unknown", "操作失败。", "The operation failed.");
        Add("Hint.None", "没有更多信息。", "No further information.");

        // ---- SshKeyManager
        Add("Err.NoSshKeygen", "找不到 ssh-keygen.exe。请确认已安装 Git for Windows（它自带 OpenSSH）。",
            "ssh-keygen.exe was not found. Please make sure Git for Windows is installed (it bundles OpenSSH).");
        Add("Err.BadFileName", "文件名不合法。", "Invalid file name.");
        Add("Err.KeyExists", "已存在同名密钥：{0}。换个名字，或先手工处理现有文件（本工具不会覆盖密钥）。",
            "A key with this name already exists: {0}. Choose another name, or handle the existing file manually (this tool never overwrites keys).");
        Add("Err.KeygenFail", "生成密钥失败：{0}", "Failed to generate the key: {0}");
        Add("Err.KeyReadFail", "密钥已生成但读取公钥失败。", "The key was generated but reading its public key failed.");
        Add("Log.KeyGenerated", "已生成 SSH 密钥：{0}（类型 {1}，指纹 {2}）", "SSH key generated: {0} (type {1}, fingerprint {2})");
        Add("Msg.KeyGenerated", "已生成密钥。请把公钥登记到 Git 服务器的 SSH keys 页面。",
            "Key generated. Please register the public key on the Git server's SSH keys page.");
        Add("Err.NoSsh", "找不到 ssh.exe。", "ssh.exe was not found.");
        Add("Err.NoHost", "远程地址里没有主机名。", "The remote URL contains no host name.");
        Add("Ssh.AuthOk", "认证成功。", "Authentication succeeded.");
        Add("Ssh.PermDenied", "服务器不认识这把密钥。请确认公钥已登记到 Git 服务器的个人设置 → SSH keys 页面。",
            "The server does not recognize this key. Confirm the public key is registered under the Git server's personal settings → SSH keys page.");
        Add("Ssh.HostKeyFail", "主机指纹校验失败，请联系 IT 确认。", "Host fingerprint verification failed; please confirm with IT.");
        Add("Ssh.CantConnect", "连不上服务器：检查网络、VPN 与端口。", "Cannot reach the server: check the network, VPN, and port.");
        Add("Ssh.NoResult", "没有得到明确结果。", "Got no clear result.");

        // ---- HttpsCredentialManager
        Add("Cred.NotHttpsSummary", "不是 HTTPS 地址", "Not an HTTPS URL");
        Add("Cred.NotHttpsDetail", "这个远程用的是 SSH 或本地路径，不需要 HTTPS 账号。", "This remote uses SSH or a local path; no HTTPS account needed.");
        Add("Cred.NoSavedSummary", "还没有保存的凭据", "No saved credentials yet");
        Add("Cred.NoSavedDetail", "Git 凭据管理器里没有 {0} 的可用账号。点「保存凭据」填入你的账号和 PAT。",
            "The Git credential manager has no usable account for {0}. Click “Save credentials” and enter your account and PAT.");
        Add("Cred.IncompleteSummary", "凭据不完整", "Credentials incomplete");
        Add("Cred.IncompleteDetail", "找到了用户名但没有密码或令牌，请重新保存一次。", "A username was found but no password or token; please save again.");
        Add("Cred.SavedSummary", "已保存（用户：{0}）", "Saved (user: {0})");
        Add("Cred.SavedDetail", "凭据由 Git Credential Manager 保管在 Windows 凭据管理器，本工具不保存密码。",
            "Credentials are kept by Git Credential Manager in the Windows Credential Manager; this tool never stores passwords.");
        Add("Cred.SaveNotHttpsSummary", "地址不是 HTTPS", "URL is not HTTPS");
        Add("Cred.SaveNotHttpsDetail", "SSH 地址不需要账号密码，请改用 SSH 密钥配置。", "SSH URLs need no account/password; use SSH key configuration instead.");
        Add("Cred.EmptyUserSummary", "用户名为空", "Username is empty");
        Add("Cred.EmptyUserDetail", "请填写你的账号或用户名。", "Please enter your account or username.");
        Add("Cred.EmptyPassSummary", "密码或令牌为空", "Password or token is empty");
        Add("Cred.EmptyPassDetail", "请填写密码或个人访问令牌（PAT）。", "Please enter your password or personal access token (PAT).");
        Add("Log.GcmFallback", "GCM 保存未生效，改用 wincred 保存 {0} 的凭据…", "GCM save did not take effect; saving credentials for {0} via wincred…");
        Add("Log.SaveFail", "保存凭据失败：{0}", "Failed to save credentials: {0}");
        Add("Cred.SaveFailSummary", "保存失败", "Save failed");
        Add("Cred.SaveFailDetail", "Git 没能写入凭据管理器：{0}", "Git could not write to the credential manager: {0}");
        Add("Log.CredSaved", "已把 {0} 的凭据保存到 Windows 凭据管理器（本工具不保存密码）。",
            "Credentials for {0} saved to the Windows Credential Manager (this tool never stores passwords).");
        Add("Cred.EraseNotHttpsSummary", "地址不是 HTTPS", "URL is not HTTPS");
        Add("Cred.EraseNotHttpsDetail", "没有可删除的 HTTPS 凭据。", "There are no HTTPS credentials to delete.");
        Add("Cred.DeleteFailSummary", "删除失败", "Delete failed");
        Add("Log.CredDeleted", "已删除 {0} 的已保存凭据。", "Saved credentials for {0} deleted.");
        Add("Cred.DeletedSummary", "已删除", "Deleted");
        Add("Cred.DeletedDetail", "下次操作会要求重新输入。", "You will be asked to enter them again next time.");

        // ---- CredentialHelperResolver
        Add("Helper.Available", "可用", "Available");
        Add("Helper.Missing", "文件不存在", "File not found");
        Add("Diag.NoHelperSummary", "没有配置凭据辅助程序", "No credential helper configured");
        Add("Diag.NoHelperDetailHead", "这台机器的 git 没有设置 credential.helper，HTTPS 凭据无法保存。",
            "This machine's git has no credential.helper set, so HTTPS credentials cannot be saved.");
        Add("Diag.NoHelperDetailHasHelper", "点下面的按钮可以把找到的 Git Credential Manager 配上。",
            "Click the button below to configure the Git Credential Manager that was found.");
        Add("Diag.NoHelperDetailNoHelper", "也没找到可用的 Git Credential Manager，请重装 Git for Windows。",
            "No usable Git Credential Manager was found either; please reinstall Git for Windows.");
        Add("Diag.OkSummary", "凭据辅助程序配置正常", "Credential helper configured correctly");
        Add("Diag.BrokenLine1", "  已配置：{0}（来自 {1}）", "  Configured: {0} (from {1})");
        Add("Diag.BrokenLine2", "  但文件不存在：{0}", "  But the file does not exist: {0}");
        Add("Diag.ReplacementLine", "  可用的替代：{0}", "  Usable replacement: {0}");
        Add("Diag.BrokenSummary", "凭据辅助程序路径指向了不存在的文件", "The credential helper path points to a file that does not exist");
        Add("Err.PersistFileMissing", "目标文件不存在：{0}", "Target file does not exist: {0}");
        Add("Err.PersistFail", "写入失败：{0}", "Write failed: {0}");
        Add("Err.PersistStillBroken", "写入后仍然检测到问题：{0}", "Problems detected even after writing: {0}");
        Add("Msg.PersistOk", "已写入用户级 git 配置。", "Written to the user-level git configuration.");

        // ---- HeadlessInspect
        Add("HI.NoRepo", "配置里没有仓库路径。请先在界面里选好仓库，或手工编辑 {0}",
            "No repo path in the configuration. Pick a repo in the UI first, or edit {0} manually.");
        Add("HI.NoGit", "找不到 git.exe。", "git.exe was not found.");
        Add("HI.NotRepo", "不是 git 仓库：{0}", "Not a git repository: {0}");
        Add("HI.Repo", "仓库：{0}", "Repo: {0}");
        Add("HI.Remotes", "源仓库远程：{0}    目标仓库远程：{1}", "Source remote: {0}    Target remote: {1}");
        Add("HI.FetchFail", "拉取失败：{0}", "Fetch failed: {0}");
        Add("HI.ColBranch", "分支", "Branch");
        Add("HI.ColSource", "源", "Source");
        Add("HI.ColTarget", "目标", "Target");
        Add("HI.ColState", "状态", "State");
        Add("HI.ColDelta", "差异", "Delta");
        Add("HI.Delta.Behind", "源 +{0}", "source +{0}");
        Add("HI.Delta.InternalAhead", "目标 +{0}", "target +{0}");
        Add("HI.Delta.Diverged", "目标 +{0} / 源 +{1}", "target +{0} / source +{1}");
        Add("HI.Delta.New", "全部新增", "all new");
        Add("HI.InternalOnly", "目标仓库独有分支（不会删除）：{0}", "Branches only on the target repo (not deleted): {0}");
        Add("HI.Tags", "标签：源仓库 {0} 个，其中 {1} 个是目标仓库没有的新标签。",
            "Tags: {0} in the source repo, of which {1} are new tags missing on the target repo.");
        Add("HI.ConclusionOk", "结论：全部分支已包含源仓库最新代码，无需同步。",
            "Conclusion: all branches already contain the source repo's latest code; no sync needed.");
        Add("HI.ConclusionPending", "结论：{0} 个分支需要上传（其中 {1} 个分叉）。",
            "Conclusion: {0} branch(es) need pushing ({1} diverged).");

        // ---- CredentialsWindow
        Add("Title.Credentials", "凭据与密钥", "Credentials & Keys");
        Add("Cred.Intro", "帮你把本机和远程仓库连起来：SSH 页生成密钥对并一步步指导登记；HTTPS 页只检查状态。账号密码本身仍由本机 git 和 Windows 保管，本工具不录入。",
            "Connects this machine to your remote repos: the SSH page generates key pairs and walks you through registration; the HTTPS page only checks status. Accounts and passwords stay with your local git and Windows; this tool never stores them.");
        Add("Tab.Ssh", "SSH 密钥（推荐先看这里）", "SSH Keys (start here)");
        Add("Tab.Https", "HTTPS 状态", "HTTPS Status");
        Add("Cred.SshIntro", "远程地址是 ssh:// 或 git@host:path 形式时，需要用 SSH 密钥认证。跟着下面 4 步走：① 列表为空就生成密钥 → ② 复制公钥 → ③ 到登记页粘贴 → ④ 测试连接。私钥始终留在 ~/.ssh，本工具只读取公钥用于展示和复制。",
            "When remote URLs look like ssh:// or git@host:path, SSH key authentication is required. Follow these 4 steps: ① generate a key if the list is empty → ② copy the public key → ③ paste it on the registration page → ④ test the connection. Private keys always stay in ~/.ssh; this tool only reads public keys for display and copying.");
        Add("Cred.ExistingKeys", "本机已有的密钥：", "Existing keys on this machine:");
        Add("Btn.GenKey", "① 生成 ed25519 密钥", "① Generate ed25519 key");
        Add("Btn.TestSsh", "④ 测试连接", "④ Test connection");
        Add("Cred.CommentLabel", "新密钥备注（一般是邮箱）： ", "Comment for the new key (usually an email): ");
        Add("Cred.PublicKeyLabel", "② 公钥（复制后贴到 Git 服务器的 SSH keys 登记页）：", "② Public key (copy and paste it on the Git server's SSH keys page):");
        Add("Btn.CopyPub", "② 复制公钥", "② Copy public key");
        Add("Btn.OpenRegister", "③ 打开 Git 服务器登记页", "③ Open the Git server registration page");
        Add("Cred.HttpsIntro", "远程地址是 https:// 开头时，git 会用本机的凭据管理器（Git Credential Manager）自动取用你保存过的账号 / 个人访问令牌。本页不做录入：第一次使用时 git 会自己弹出登录窗口。本工具只负责检查当前能不能取到凭据。",
            "When remote URLs start with https://, git automatically uses your saved accounts / personal access tokens via the local credential manager (Git Credential Manager). Nothing is entered on this page: the first time, git pops up its own sign-in window. This tool only checks whether credentials can currently be obtained.");
        Add("Btn.RepairGit", "永久修复本机 Git 配置", "Permanently fix this machine's Git configuration");
        Add("Cred.FoundKeys", "找到 {0} 把密钥", "Found {0} key(s)");
        Add("Cred.NoKeys", "本机还没有 SSH 密钥 —— 点「① 生成 ed25519 密钥」开始",
            "No SSH keys on this machine yet — click “① Generate ed25519 key” to start");
        Add("Cred.ConfirmGenerate", "将在 ~/.ssh 下生成一把新密钥，并把私钥权限限制为仅当前用户可读。\r\n私钥不会被上传到任何地方。继续？",
            "A new key will be generated under ~/.ssh, with the private key readable only by the current user.\r\nThe private key is never uploaded anywhere. Continue?");
        Add("Caption.GenSshKey", "生成 SSH 密钥", "Generate SSH Key");
        Add("Cred.GeneratedNext", "{0}\r\n\r\n下一步：点「② 复制公钥」，把它登记到 Git 服务器。",
            "{0}\r\n\r\nNext: click “② Copy public key” and register it on the Git server.");
        Add("Caption.Generated", "已生成", "Generated");
        Add("Caption.GenerateFail", "生成失败", "Generation Failed");
        Add("Cred.NothingToCopy", "没有可复制的公钥。先选中一把密钥或生成新的。", "No public key to copy. Select a key first, or generate a new one.");
        Add("Caption.NothingToCopy", "无可复制内容", "Nothing to Copy");
        Add("Cred.PubCopied", "公钥已复制 —— 下一步到登记页粘贴", "Public key copied — next, paste it on the registration page");
        Add("Cred.CopyFail", "复制失败（剪贴板被其他程序占用）。", "Copy failed (the clipboard is in use by another program).");
        Add("Caption.CopyFail", "复制失败", "Copy Failed");
        Add("Cred.NoSshUrl", "当前项目的远程都不是 SSH 地址，推断不出 Git 服务器的网页地址。\r\n请自行打开你的 Git 服务器，进入个人设置 → SSH Keys 页面粘贴公钥。",
            "None of the current project's remotes is an SSH URL, so the Git server's web address cannot be inferred.\r\nOpen your Git server yourself, go to personal settings → SSH Keys, and paste the public key.");
        Add("Caption.ManualRegister", "请手动打开登记页", "Please Open the Page Manually");
        Add("Cred.NoSshTest", "当前没有 SSH 远程地址可测试。", "There is no SSH remote URL to test.");
        Add("Caption.NoNeedTest", "无需测试", "Nothing to Test");
        Add("Cred.Connecting", "正在连接 git@{0}:{1} …", "Connecting to git@{0}:{1}…");
        Add("Cred.SshOk", "SSH 认证成功，本地和远程已连通", "SSH authentication succeeded; local and remote are connected");
        Add("Cred.SshFail", "SSH 认证失败", "SSH authentication failed");
        Add("Cred.NoRepo", "还没有选择有效的仓库目录", "No valid repository directory selected yet");
        Add("Cred.BrokenHelper", "凭据辅助程序配置有问题，HTTPS 凭据无法使用", "The credential helper is misconfigured; HTTPS credentials cannot be used");
        Add("Cred.BrokenHelperHint", "{0}\r\n本程序已自动绕过这个问题；要永久修好这台机器，点「永久修复本机 Git 配置」。",
            "{0}\r\nThis app works around the problem automatically; to fix this machine permanently, click “Permanently fix this machine's Git configuration”.");
        Add("Cred.AllSsh", "两个远程都是 SSH 地址，不需要 HTTPS 凭据", "Both remotes are SSH URLs; no HTTPS credentials needed");
        Add("Cred.UseSshHint", "你的配置走 SSH，请到「SSH 密钥」页生成并登记密钥。",
            "Your configuration uses SSH; go to the “SSH Keys” page to generate and register a key.");
        Add("Cred.Addr", "地址：{0}", "URL: {0}");
        Add("Cred.ConfirmRepairHead", "将要执行：\r\n\r\n", "This will run:\r\n\r\n");
        Add("Cred.ConfirmRepairLine1", "  git config --global --replace-all credential.helper \"\"\r\n", "  git config --global --replace-all credential.helper \"\"\r\n");
        Add("Cred.ConfirmRepairLine2", "  git config --global --add credential.helper \"{0}\"\r\n\r\n", "  git config --global --add credential.helper \"{0}\"\r\n\r\n");
        Add("Cred.ConfirmRepairTail", "这会把用户级 ~/.gitconfig 里的 credential.helper 改成可用的路径。\r\n只影响凭据辅助程序，不动其它配置。继续？",
            "This changes credential.helper in the user-level ~/.gitconfig to a working path.\r\nOnly the credential helper is touched; other settings are left alone. Continue?");
        Add("Caption.RepairGit", "永久修复 Git 凭据配置", "Permanently Fix Git Credential Configuration");
        Add("Cred.RepairOk", "已修好。之后这台机器上的所有 git 操作都会正常使用凭据。", "Fixed. All git operations on this machine will now use credentials normally.");
        Add("Caption.RepairOk", "修复完成", "Repair Complete");
        Add("Cred.RepairFail", "修复失败：{0}", "Repair failed: {0}");
        Add("Caption.RepairFail", "修复失败", "Repair Failed");
        Add("KeyItem.FingerprintUnavailable", "（无法计算）", "(unavailable)");
        Add("Cred.BrowserFail", "打不开浏览器：{0}", "Cannot open the browser: {0}");
        Add("Caption.OpenFail", "打开失败", "Open Failed");

        // ---- AddProjectDialog
        Add("Title.AddProject", "添加同步项目", "Add Sync Project");
        Add("Add.Intro", "一个项目 = 一个本地仓库 + 它的源仓库远程 + 目标仓库远程。已有本地克隆就选目录；还没有就先克隆。",
            "A project = a local repo + its source remote + a target remote. Already have a local clone? Pick the directory; otherwise clone first.");
        Add("Btn.CloneNew", "克隆新仓库…", "Clone new repo…");
        Add("Add.PathLabel", "本地仓库目录", "Local repo directory");
        Add("Add.VendorLabel", "源仓库远程", "Source remote");
        Add("Add.InternalLabel", "目标仓库远程", "Target remote");
        Add("Add.NameLabel", "项目名称（可选，默认用目录名）", "Project name (optional; defaults to the directory name)");
        Add("Btn.AddConfirm", "添加", "Add");
        Add("Add.FolderDialogTitle", "选择本地克隆的仓库目录", "Select the directory of the local clone");
        Add("Add.WarnNoPath", "请选择本地仓库目录，或点「克隆新仓库」。", "Select a local repo directory, or click “Clone new repo”.");
        Add("Caption.MissingDir", "缺少目录", "Directory Missing");
        Add("Add.WarnNotRepo", "这个目录还不是 git 仓库。请选仓库根目录，或用「克隆新仓库」。", "This directory is not a git repository yet. Pick the repo root, or use “Clone new repo”.");
        Add("Caption.NotARepo", "不是仓库", "Not a Repository");
        Add("Add.WarnNoRemotes", "源仓库远程和目标仓库远程都要填（一般分别是 origin 和 bitbucket）。",
            "Both the source remote and the target remote must be filled in (usually origin and bitbucket).");
        Add("Caption.MissingRemote", "缺少远程名", "Remote Name Missing");

        // ---- CloneDialog
        Add("Title.Clone", "克隆仓库", "Clone Repository");
        Add("Clone.Intro", "从源仓库克隆一份到本地；需要同步到另一个仓库的话，再填一行目标仓库地址。",
            "Clone the source repo to this machine; if you also need to sync it to another repo, fill in the target repo URL too.");
        Add("Clone.SourceLabel", "源仓库地址（upstream）", "Source repo URL (upstream)");
        Add("Clone.TargetLabel", "目标仓库地址（可选，留空则只克隆）", "Target repo URL (optional; leave empty to clone only)");
        Add("Clone.DestLabel", "克隆到目录（需要是空目录）", "Clone into directory (must be empty)");
        Add("Btn.Clone", "克隆", "Clone");
        Add("Clone.FolderDialogTitle", "选择克隆目标目录（需要是空目录）", "Select the clone target directory (must be empty)");
        Add("Clone.WarnNoSource", "请填写源仓库地址。", "Please enter the source repo URL.");
        Add("Caption.MissingUrl", "缺少地址", "URL Missing");
        Add("Clone.WarnNoDest", "请选择克隆到哪个目录。", "Please choose which directory to clone into.");
        Add("Clone.WarnDestNotEmpty", "目标目录不是空的。请选一个空目录，git 才能克隆进去。",
            "The target directory is not empty. Pick an empty directory so git can clone into it.");
        Add("Caption.DirNotEmpty", "目录非空", "Directory Not Empty");

        // ---- CredentialPromptDialog
        Add("Title.CredPrompt", "需要登录凭据", "Credentials Required");
        Add("CredPrompt.UserLabel", "用户名 / 域账号", "Username / domain account");
        Add("CredPrompt.Show", "显示", "Show");
        Add("CredPrompt.PassLabel", "密码 / 个人访问令牌（PAT）", "Password / personal access token (PAT)");
        Add("CredPrompt.Note", "输入会通过 git credential 协议交给 Windows 凭据管理器加密保存；本工具不落盘、不保存密码。之后所有 git 操作自动取用，不再询问。",
            "Your input is handed to the Windows Credential Manager for encrypted storage via the git credential protocol; this tool never writes it to disk or keeps the password. All later git operations use it automatically without asking again.");
        Add("Btn.SaveContinue", "保存并继续", "Save and continue");
        Add("CredPrompt.Header", "远程「{0}」需要登录凭据才能继续", "Remote “{0}” needs login credentials to continue");
        Add("CredPrompt.SaveFail", "{0}\r\n\r\n{1}", "{0}\r\n\r\n{1}");
        Add("Caption.SaveFail", "保存失败", "Save Failed");

        // ---- RemoteSettingsDialog
        Add("Title.RemoteSettings", "远程仓库设置", "Remote Settings");
        Add("Remote.Intro", "设置这个项目同步用的两个远程仓库：名字与地址。地址不存在时会自动挂上（git remote add），改了地址会自动更新（git remote set-url）。",
            "Configure the two remotes this project syncs between: names and URLs. Missing remotes are added automatically (git remote add); changed URLs are updated automatically (git remote set-url).");
        Add("Remote.VendorName", "源仓库远程名", "Source remote name");
        Add("Remote.VendorUrl", "源仓库地址（HTTPS 或 SSH）", "Source repo URL (HTTPS or SSH)");
        Add("Remote.InternalName", "目标仓库远程名", "Target remote name");
        Add("Remote.InternalUrl", "目标仓库地址（HTTPS 或 SSH）", "Target repo URL (HTTPS or SSH)");
        Add("Remote.WarnEmpty", "两个远程名都不能为空。", "Neither remote name may be empty.");
        Add("Remote.WarnSame", "源仓库和目标仓库不能用同一个远程名。", "The source and target remotes cannot use the same name.");
        Add("Caption.RemoteDuplicate", "远程名重复", "Duplicate Remote Name");
        Add("Remote.WarnNoUrl", "远程「{0}」还没有地址，请填一个仓库地址（HTTPS 或 SSH）。",
            "Remote “{0}” has no URL yet; please enter a repository URL (HTTPS or SSH).");
        Add("Remote.WarnFail", "设置远程「{0}」失败，详情见日志。", "Failed to configure remote “{0}”; see the log for details.");
        Add("Caption.SetFail", "设置失败", "Setup Failed");

        // ---- DetailWindow
        Add("Title.Detail", "分支详情", "Branch Details");
        Add("Detail.Meta", "状态：{0}    源：{1}    目标：{2}", "State: {0}    Source: {1}    Target: {2}");
        Add("Detail.TargetOnlyHeader", "目标仓库独有提交（{0}）—— 覆盖策略下会被直接丢弃（不备份）",
            "Commits only on the target repo ({0}) — discarded outright (no backup) under the overwrite strategy");
        Add("Detail.SourceOnlyHeader", "源仓库独有提交（{0}）", "Commits only on the source repo ({0})");
        Add("Detail.NoDiff", "没有额外的提交差异。", "No additional commit differences.");
    }
}
