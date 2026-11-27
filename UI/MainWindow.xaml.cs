using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VendorSync.Core;
using VendorSync.UI.Controls;

namespace VendorSync.UI;

/// <summary>主窗口：多项目卡片墙 + 详情页 + 日志抽屉（Ctrl+L 展开）。</summary>
public partial class MainWindow : Window
{
    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private GitClient? _git;
    private readonly ObservableCollection<ProjectVm> _projects = new();
    private ProjectVm? _current;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Load();

        // 软件渲染（老电脑 / 远程桌面）：关闭阴影，进简洁模式。
        Neu.ReducedEffects = RenderCapability.Tier >> 16 == 0;

        _store = new ConfigStore();
        _config = _store.Load();
        _config.EnsureLists();
        CardList.ItemsSource = _projects;

        AppLog.Attach(AppendLog);
        TryEnableMica();
        Loaded += (_, _) => Startup();
    }

    // ------------------------------------------------------------- 启动

    private void Startup()
    {
        AppendLog("=== 同步项目 ===");
        SetAppInfo();
        _git = GitClient.Locate();
        if (_git is null)
        {
            AppendLog("✖ 没有找到 git.exe。");
            const string gitUrl = "https://git-scm.com/download/win";
            AppendLog("安装地址：" + gitUrl);
            SetStatus("缺少 git");
            var open = Prompt.Confirm(this,
                "没有找到 git.exe，本工具无法工作。\r\n\r\n" +
                "请到 Git 官网下载安装「Git for Windows」：\r\n" + gitUrl +
                "\r\n\r\n安装后关闭所有终端和编辑器，再重新打开本工具。\r\n\r\n现在打开下载页面？",
                "缺少 Git", defaultWhenSilent: false);
            if (open) OpenGitInstallPage();
            return;
        }
        EnvText.Text = "git " + _git.Run(new[] { "--version" }).StdOut.Trim().Replace("git version ", "");
        DiagnoseCredentials();

        foreach (var p in _config.Projects) _projects.Add(new ProjectVm(p));
        RefreshHome();
        SetStatus(_projects.Count == 0 ? "就绪。" : $"就绪，正在后台检查 {_projects.Count} 个项目…");

        // 后台自动检查：项目之间并行跑，谁先完成谁先亮灯。
        if (_projects.Count > 0)
        {
            Task.Run(async () =>
            {
                var vms = _projects.ToList();
                try
                {
                    await Task.WhenAll(vms.Select(vm => Task.Run(() => RunInspect(vm))));
                }
                catch (Exception ex)
                {
                    AppLog.Write("后台自动检查出错：" + ex);
                }
                SetStatus("就绪，后台检查完成。");
            });
        }
    }

    private void OpenGitInstallPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://git-scm.com/download/win",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { AppendLog("打开安装页面失败：" + ex.Message); }
    }

    /// <summary>右下角应用信息：名称 + 版本。</summary>
    private void SetAppInfo()
    {
        var ver = typeof(MainWindow).Assembly.GetName().Version;
        var text = $"GitSync v{(ver is null ? "?" : ver.ToString(3))}";
        RunOnUi(() => AppInfoText.Text = text);
    }

    private void DiagnoseCredentials()
    {
        if (_git is null) return;
        try
        {
            var diagnosis = CredentialHelperResolver.Diagnose(_git);
            if (!diagnosis.NeedsRepair)
            {
                AppendLog("凭据辅助程序：正常（由本机 git 管理）。");
                return;
            }
            AppendLog("⚠ 本机 git 凭据辅助程序配置有问题：" + diagnosis.Summary);
            if (diagnosis.ReplacementPath is null)
            {
                AppendLog("✖ 没找到可用的替代程序，HTTPS 凭据将无法保存。请重装 Git for Windows。");
                return;
            }
            _git.CredentialOverride = CredentialHelperResolver.BuildOverrideArgs(diagnosis);
            AppendLog($"✔ 已自动修正（只影响本程序）：{diagnosis.ReplacementPath}");
        }
        catch (Exception ex)
        {
            AppendLog("诊断凭据环境时出错：" + ex.Message);
        }
    }

    // ------------------------------------------------------------- 首页

    private void RefreshHome()
    {
        EmptyHint.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pending = _projects.Count(p => p.Orb is OrbState.Pending);
        var failed = _projects.Count(p => p.Orb is OrbState.Failed);
        HomeSummary.Text = _projects.Count == 0 ? "" :
            failed > 0 ? $"{_projects.Count} 个项目，{failed} 个失败，需要处理。"
            : pending > 0 ? $"{_projects.Count} 个项目，{pending} 个有待同步。"
            : $"{_projects.Count} 个项目，全部已一致。";
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectVm vm) ShowDetail(vm);
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectVm vm && Directory.Exists(vm.Model.Path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.Model.Path) { UseShellExecute = true });
    }

    private void MenuRemotes_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectVm vm) EditRemotes(vm);
    }

    private void BtnDetailRemotes_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) EditRemotes(_current);
    }

    /// <summary>打开远程仓库设置：改名字 / 地址，保存后立刻重新检查。</summary>
    private void EditRemotes(ProjectVm vm)
    {
        if (_git is null || vm.Busy) return;
        if (!Directory.Exists(vm.Model.Path))
        {
            Prompt.Warn(this, "本地仓库目录不存在，无法设置远程。", "目录缺失");
            return;
        }
        var dialog = new RemoteSettingsDialog(
            _git, vm.Model.Path, _git.GetRemotes(vm.Model.Path),
            vm.Model.VendorRemote, vm.Model.InternalRemote) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        vm.Model.VendorRemote = dialog.VendorRemote;
        vm.Model.InternalRemote = dialog.InternalRemote;
        _config.SetCurrent(vm.Model);
        Persist();
        vm.Refresh();
        if (_current == vm)
        {
            DetailSource.Text = _git.GetRemoteUrl(vm.Model.Path, vm.Model.VendorRemote) ?? vm.Model.VendorRemote;
            DetailTarget.Text = vm.Model.InternalRemote + " · " + (_git.GetRemoteUrl(vm.Model.Path, vm.Model.InternalRemote) ?? "—");
        }
        SetStatus($"已更新「{vm.Model.DisplayName}」的远程仓库。");
        RunInspect(vm);
    }

    private void MenuRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectVm vm) return;
        if (!Prompt.Confirm(this, $"移除项目「{vm.Model.DisplayName}」？\r\n\r\n只从本工具的列表里移除，不删除仓库文件。", "移除项目")) return;
        _config.RemoveProject(vm.Model);
        Persist();
        _projects.Remove(vm);
        RefreshHome();
    }

    private void BtnCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (_git is null) return;
        var project = _current?.Model ?? _config.Projects.FirstOrDefault();
        if (project is null)
        {
            Prompt.Info(this, "先添加一个同步项目，再配置凭据。", "提示");
            return;
        }
        _config.SetCurrent(project);
        new CredentialsWindow(_git, _config) { Owner = this }.ShowDialog();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_git is null || _busyAdding) return;
        _busyAdding = true;
        try
        {
            var dialog = new AddProjectDialog(_git, AppendLog) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var profile = dialog.Result;
            if (profile is null) return;

            var existing = _config.AddProject(profile.Path, profile.VendorRemote, profile.InternalRemote, profile.Name);
            _config.SetCurrent(existing);
            Persist();

            var vm = _projects.FirstOrDefault(p => p.Model.Path == existing.Path);
            if (vm is null)
            {
                vm = new ProjectVm(existing);
                _projects.Add(vm);
            }
            RefreshHome();
            SetStatus($"已添加项目：{existing.DisplayName}");

            // 立即检查一次，让新卡片有状态（RunInspect 自行处理线程切换）。
            RunInspect(vm);
        }
        finally { _busyAdding = false; }
    }

    private bool _busyAdding;

    // ------------------------------------------------------------- 详情页

    private void ShowDetail(ProjectVm vm)
    {
        _current = vm;
        _config.SetCurrent(vm.Model);
        DetailTitle.Text = vm.Model.DisplayName;
        DetailSource.Text = _git?.GetRemoteUrl(vm.Model.Path, vm.Model.VendorRemote) ?? vm.Model.VendorRemote;
        DetailTarget.Text = vm.Model.InternalRemote + " · " + (_git?.GetRemoteUrl(vm.Model.Path, vm.Model.InternalRemote) ?? "—");
        DetailStrategy.SelectedIndex = _config.StrategyFor(vm.Model) == SyncStrategy.Force ? 0 : 1;
        DetailTags.IsChecked = _config.IncludeTagsFor(vm.Model);
        HomeView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        RenderDetail();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        DetailView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
        RefreshHome();
    }

    private void DetailStrategy_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_current is null || DetailStrategy.SelectedIndex < 0) return;
        _current.Model.Strategy = DetailStrategy.SelectedIndex == 0 ? "Force" : "Merge";
        Persist();
        RenderDetail();
    }

    private void DetailTags_Changed(object sender, RoutedEventArgs e)
    {
        if (_current is null || _loadingDetail) return;
        _current.Model.IncludeTags = DetailTags.IsChecked == true;
        Persist();
        RunInspect(_current);
    }

    private bool _loadingDetail;

    private void BranchToggle_Click(object sender, RoutedEventArgs e) => RenderDetail();

    private void BtnDetailCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) RunInspect(_current);
    }

    private void BtnDetailSync_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) RunSync(_current);
    }

    private void RenderDetail()
    {
        var vm = _current;
        if (vm is null) return;
        _loadingDetail = true;
        try
        {
            DetailStrategy.SelectedIndex = _config.StrategyFor(vm.Model) == SyncStrategy.Force ? 0 : 1;
            DetailTags.IsChecked = _config.IncludeTagsFor(vm.Model);
            var snap = vm.Snapshot;
            BranchSummary.Text = snap is null ? "" :
                $"{snap.Statuses.Count} 个分支，{snap.PendingBranches} 个需要处理" +
                (snap.NewTagCount > 0 ? $"，{snap.NewTagCount} 个新标签" : "");
            // 目标仓库独有的分支：一律保留不动，这里明说，免得用户担心被删。
            var extra = snap?.InternalOnlyBranches ?? Array.Empty<string>();
            DetailSummary.Text = snap is null ? "" :
                $"源仓库 {snap.Statuses.Count} 个分支，{snap.PendingBranches} 个需要同步。" +
                (extra.Count == 0 ? "" :
                    $"  目标仓库多 {extra.Count} 个分支（{string.Join("、", extra.Take(4))}{(extra.Count > 4 ? " 等" : "")}），保留不动，不会被删除或覆盖。");
            BranchList.ItemsSource = BuildBranchRows(snap, (BranchToggle.IsChecked == true));
            SetStep(StepConn, vm.Stage >= SyncStage.Connect && vm.Stage != SyncStage.Connect ? "done" : vm.Stage == SyncStage.Connect ? "running" : "pending");
            SetStep(StepCheck, vm.Stage >= SyncStage.Check ? "done" : vm.Stage == SyncStage.Check ? "running" : "pending");
            SetStep(StepPush, vm.Stage >= SyncStage.Push ? "done" : vm.Stage == SyncStage.Push ? "running" : "pending");
            SetStep(StepVerify, vm.Stage >= SyncStage.Verify ? "done" : vm.Stage == SyncStage.Verify ? "running" : "pending");
            DetailRing.Progress = vm.Fraction;
            DetailPercent.Text = vm.Fraction > 0 && vm.Fraction < 1 ? $"{(int)(vm.Fraction * 100)}%" : "";
            DetailKnobText.Text = vm.Busy ? "同步中…" : "同步";
            BtnDetailSync.IsEnabled = !vm.Busy;
            BtnDetailCheck.IsEnabled = !vm.Busy;
        }
        finally { _loadingDetail = false; }
    }

    private System.Collections.IEnumerable BuildBranchRows(ProjectSnapshot? snap, bool onlyPending)
    {
        if (snap is null) return Array.Empty<BranchRow>();
        IEnumerable<BranchStatus> list = snap.Statuses;
        var rows = new List<BranchRow>();
        foreach (var s in list)
        {
            var needs = snap.NeedsAction(s);
            if (onlyPending && !needs && s.State != BranchState.Diverged) continue;
            rows.Add(new BranchRow
            {
                Name = s.Name,
                State = s.State,
                StateLabel = s.StateLabel,
                Delta = s.State switch
                {
                    BranchState.Behind => $"源多 {s.VendorOnly} 个提交",
                    BranchState.InternalAhead => $"目标多 {s.InternalOnly} 个提交",
                    BranchState.Diverged => $"源 +{s.VendorOnly} / 目标 +{s.InternalOnly}",
                    BranchState.New => "新分支",
                    _ => "—"
                },
                Plan = s.State switch
                {
                    BranchState.Behind => "直接推送（快进）",
                    BranchState.New => "在目标仓库新建",
                    BranchState.Diverged => snap.Strategy == SyncStrategy.Force ? "以源为准覆盖 · 先备份" : "合并后推送",
                    BranchState.InternalAhead => snap.Strategy == SyncStrategy.Force ? "以源为准覆盖 · 先备份" : "保留",
                    BranchState.VendorGone => "保留，不删除",
                    _ => "已一致"
                },
                Result = needs ? "待处理" : s.State == BranchState.UpToDate ? "一致" : ""
            });
        }
        // 目标仓库独有的分支：一并列出，明确标注保留不动。
        foreach (var b in snap.InternalOnlyBranches)
        {
            rows.Add(new BranchRow
            {
                Name = b,
                State = BranchState.VendorGone,
                StateLabel = "目标独有",
                Delta = "—",
                Plan = "保留，不同步不删除",
                Result = ""
            });
        }
        return rows;
    }

    // ------------------------------------------------------------- 检查 / 同步

    private SyncOptions OptionsFor(ProjectVm vm) => new()
    {
        RepoPath = vm.Model.Path,
        VendorRemote = vm.Model.VendorRemote,
        InternalRemote = vm.Model.InternalRemote,
        Strategy = _config.StrategyFor(vm.Model),
        IncludeTags = _config.IncludeTagsFor(vm.Model)
    };

    private bool EnsureCredentialReady(ProjectVm vm)
    {
        if (_git is null || !Directory.Exists(vm.Model.Path)) return true;
        var manager = new HttpsCredentialManager(_git, AppendLog);
        foreach (var (label, remote) in new[] { ("源仓库", vm.Model.VendorRemote), ("目标仓库", vm.Model.InternalRemote) })
        {
            var url = _git.GetRemoteUrl(vm.Model.Path, remote);
            if (url is null || RemoteUrl.Detect(url) != RemoteScheme.Https) continue;
            var status = manager.Probe(url);
            if (status.Ok) continue;

            AppendLog($"{label}（{remote}）需要登录：{status.Summary}");
            var dialog = new CredentialPromptDialog(this, _git, AppendLog, $"{label} · {remote}", url);
            if (dialog.ShowDialog() != true)
            {
                AppendLog("未提供凭据，操作取消。");
                SetStatus("缺少凭据，操作已取消。");
                return false;
            }
        }
        return true;
    }

    private void RunInspect(ProjectVm vm)
    {
        if (_git is null) return;
        var ok = Dispatcher.Invoke(() =>
        {
            if (vm.Busy) return false;
            return EnsureCredentialReady(vm);
        });
        if (!ok) return;

        // 视图状态只能在 UI 线程改：后台线程直接改绑定属性会让 WPF 抛
        // 「调用线程无法访问此对象」，表现为启动后快速进详情页时闪退。
        RunOnUi(() =>
        {
            vm.Busy = true;
            vm.Orb = OrbState.Running;
            vm.Stage = SyncStage.Connect;
            vm.Message = "正在检查…";
            vm.Refresh();
        });
        var options = OptionsFor(vm);
        var runner = new ProjectRunner(_git, m => AppendLog($"[{vm.Model.DisplayName}] {m}"));
        // Progress<T> 必须在 UI 线程创建：它捕获创建线程的同步上下文，
        // 若在后台线程创建，回调就会在后台线程跑，RenderDetail 一碰 UI 就闪退。
        var progress = Dispatcher.Invoke(() => new Progress<SyncProgress>(p =>
        {
            vm.Stage = p.Stage;
            vm.Message = p.Text;
            if (_current == vm) RenderDetail();
        }));

        Task.Run(() =>
        {
            try
            {
                var snap = runner.Inspect(options, out var error, progress);
                Dispatch(() =>
                {
                    vm.Busy = false;
                    if (snap is null)
                    {
                        vm.Orb = OrbState.Failed;
                        vm.Message = FirstLine(error);
                        vm.Model.LastHealth = ProjectHealth.Failed;
                        vm.Model.LastMessage = FirstLine(error);
                        vm.Model.LastCheckedAt = DateTime.Now;
                    }
                    else
                    {
                        vm.Snapshot = snap;
                        vm.Stage = SyncStage.Done;
                        vm.Message = snap.UpToDate ? "全部已一致，无需同步。" : $"{snap.PendingBranches} 个分支需要同步";
                        vm.Model.LastHealth = snap.UpToDate ? ProjectHealth.UpToDate : ProjectHealth.Pending;
                        vm.Model.PendingCount = snap.PendingCount;
                        vm.Model.LastMessage = vm.Message;
                        vm.Model.LastCheckedAt = DateTime.Now;
                        vm.Orb = snap.UpToDate ? OrbState.UpToDate : OrbState.Pending;
                    }
                    vm.Refresh();
                    Persist();
                    if (_current == vm) RenderDetail();
                    RefreshHome();
                });
            }
            catch (Exception ex)
            {
                // 后台线程的未处理异常会直接拉崩整个进程，这里兜住并转成卡片状态。
                AppLog.Write("检查出错：" + ex);
                Dispatch(() =>
                {
                    vm.Busy = false;
                    vm.Orb = OrbState.Failed;
                    vm.Message = "检查出错：" + ex.Message;
                    vm.Model.LastHealth = ProjectHealth.Failed;
                    vm.Model.LastMessage = vm.Message;
                    vm.Model.LastCheckedAt = DateTime.Now;
                    vm.Refresh();
                    if (_current == vm) RenderDetail();
                    RefreshHome();
                });
            }
        });
    }

    private void RunSync(ProjectVm vm)
    {
        if (vm.Busy || _git is null) return;
        if (!Dispatcher.Invoke(() => EnsureCredentialReady(vm))) return;

        vm.Busy = true;
        vm.Orb = OrbState.Running;
        vm.Stage = SyncStage.Connect;
        var options = OptionsFor(vm);
        var runner = new ProjectRunner(_git, m => AppendLog($"[{vm.Model.DisplayName}] {m}"));
        var progress = new Progress<SyncProgress>(p =>
        {
            vm.Stage = p.Stage;
            vm.Fraction = p.Fraction;
            vm.Message = p.Text;
            if (_current == vm) RenderDetail();
        });

        Task.Run(() =>
        {
            try
            {
            var result = runner.Sync(options, risky =>
            {
                var detail = new System.Text.StringBuilder();
                foreach (var s in risky)
                {
                    detail.AppendLine($"【{s.Name}】目标仓库独有 {s.InternalOnly} 个 / 源仓库独有 {s.VendorOnly} 个提交");
                    foreach (var c in s.InternalOnlyCommits.Take(3)) detail.AppendLine("    目标: " + c);
                }
                detail.AppendLine();
                detail.AppendLine(options.Strategy == SyncStrategy.Force
                    ? "将以源仓库为准覆盖这些分支；被覆盖的提交会先备份成 vendor-backup 标签，随时可以找回。"
                    : "将生成合并提交，双方提交都保留。");
                detail.Append("\r\n继续？");

                var answer = false;
                Invoke(() => { answer = Prompt.Confirm(this, detail.ToString(), "确认同步"); });
                return answer;
            }, progress);

            Dispatch(() =>
            {
                vm.Busy = false;
                vm.Stage = SyncStage.Done;
                vm.Fraction = result.Succeeded || (result.Error is null && !result.Cancelled) ? 1 : 0;
                if (result.Error is not null)
                {
                    vm.Orb = OrbState.Failed;
                    vm.Message = FirstLine(result.Error);
                    vm.Model.LastHealth = ProjectHealth.Failed;
                }
                else if (result.Cancelled)
                {
                    vm.Orb = OrbState.Pending;
                    vm.Message = "已取消。";
                }
                else
                {
                    var snap = result.Snapshot!;
                    vm.Snapshot = snap;
                    vm.Model.LastSyncedAt = DateTime.Now;
                    vm.Model.LastCheckedAt = DateTime.Now;
                    if (result.Succeeded)
                    {
                        vm.Orb = OrbState.UpToDate;
                        vm.Message = $"同步完成：推送 {result.Pushed} 项" + (result.TagsPushed > 0 ? $"，新增 {result.TagsPushed} 个标签" : "") + "。";
                        vm.Model.LastHealth = ProjectHealth.UpToDate;
                        vm.Model.PendingCount = 0;
                    }
                    else
                    {
                        vm.Orb = OrbState.Failed;
                        vm.Message = $"完成，但有 {result.Failed} 项失败，见日志。";
                        vm.Model.LastHealth = ProjectHealth.Failed;
                    }
                    vm.Model.LastMessage = vm.Message;
                }
                vm.Refresh();
                Persist();
                if (_current == vm) RenderDetail();
                RefreshHome();
                if (result.Error is not null)
                    Prompt.Warn(this, FirstLine(result.Error) + "\r\n\r\n详情见下方日志。", "检查失败");
                });
            }
            catch (Exception ex)
            {
                AppLog.Write("同步出错：" + ex);
                Dispatch(() =>
                {
                    vm.Busy = false;
                    vm.Orb = OrbState.Failed;
                    vm.Message = "同步出错：" + ex.Message;
                    vm.Model.LastHealth = ProjectHealth.Failed;
                    vm.Model.LastMessage = vm.Message;
                    vm.Refresh();
                    if (_current == vm) RenderDetail();
                    RefreshHome();
                });
            }
        });
    }

    private static string FirstLine(string text) => (text ?? "").Split('\n')[0].Trim();

    // ------------------------------------------------------------- 添加项目对话框

    private void Persist()
    {
        if (!_store.Save(_config, out var error)) AppendLog("配置保存失败：" + error);
    }

    // ------------------------------------------------------------- 日志抽屉

    private bool _logExpanded;

    private void AppendLog(string message)
    {
        RunOnUi(() =>
        {
            LogOneLine.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            if (_logExpanded) LogBox.ScrollToEnd();
        });
    }

    private void BtnLogToggle_Click(object sender, RoutedEventArgs e) => ToggleLog();
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) ToggleLog();
    }

    private void ToggleLog()
    {
        _logExpanded = !_logExpanded;
        LogBox.Visibility = _logExpanded ? Visibility.Visible : Visibility.Collapsed;
        BtnLogToggle.Content = _logExpanded ? "收起 ▼" : "展开 ▲";
        if (_logExpanded) LogBox.ScrollToEnd();
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogBox.Text)) return;
        try { Clipboard.SetText(LogBox.Text); SetStatus("日志已复制到剪贴板"); }
        catch (ExternalException) { }
    }

    private void BtnCopyDiag_Click(object sender, RoutedEventArgs e)
    {
        var vm = _current;
        var text = new System.Text.StringBuilder()
            .AppendLine("=== GitSync 诊断信息 ===")
            .AppendLine("时间：" + DateTime.Now)
            .AppendLine("项目：" + (vm?.Model.DisplayName ?? "—"))
            .AppendLine("仓库：" + (vm?.Model.Path ?? "—"))
            .AppendLine("源：" + (vm?.Model.VendorRemote ?? "—") + "    目标：" + (vm?.Model.InternalRemote ?? "—"))
            .AppendLine("git：" + EnvText.Text)
            .AppendLine()
            .Append(LogBox.Text);
        try { Clipboard.SetText(text.ToString()); SetStatus("诊断信息已复制，直接发给维护人即可"); }
        catch (ExternalException) { }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        LogOneLine.Text = "日志";
    }

    // ------------------------------------------------------------- 杂项

    private void Dispatch(Action a) => Dispatcher.Invoke(a);
    private void Invoke(Action a) => Dispatcher.Invoke(a);

    private void RunOnUi(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else try { Dispatcher.BeginInvoke(a); } catch (Exception) { }
    }

    private void SetStatus(string text) => RunOnUi(() => StatusText.Text = text);

    private void SetStep(TextBlock step, string state)
    {
        step.FontWeight = state == "running" ? FontWeights.SemiBold : FontWeights.Normal;
        step.Foreground = state switch
        {
            "done" => FindResource("OkBrush") as Brush,
            "running" => FindResource("AccentBrush") as Brush,
            "failed" => FindResource("ErrorBrush") as Brush,
            _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6))
        };
    }

    private void TryEnableMica()
    {
        // Win11 Mica 真透明；其它系统静默忽略。
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            int backdrop = 2; // DWMSBT_MAINWINDOW
            DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        }
        catch (Exception) { /* Win10 / 旧系统：保持纯色 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>一张项目卡片的视图状态。</summary>
public class ProjectVm : INotifyPropertyChanged
{
    public RepoProfile Model { get; }
    public ProjectSnapshot? Snapshot { get; set; }
    public OrbState Orb { get; set; } = OrbState.Unknown;
    public SyncStage Stage { get; set; } = SyncStage.Connect;
    public double Fraction { get; set; }
    public bool Busy { get; set; }

    public string PendingText => Snapshot is null ? "" : Snapshot.PendingCount > 0 ? Snapshot.PendingCount.ToString() : "";
    public string RoutesText => $"{Model.VendorRemote} → {Model.InternalRemote}";
    public string Message { get; set; } = "尚未检查";
    public string LastRunText
    {
        get
        {
            var t = Model.LastSyncedAt ?? Model.LastCheckedAt;
            return t is null ? "从未运行" : $"上次{(Model.LastSyncedAt is null ? "检查" : "同步")}：{t:MM-dd HH:mm}";
        }
    }

    public ProjectVm(RepoProfile model) => Model = model;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        foreach (var p in new[] { nameof(Orb), nameof(PendingText), nameof(Message), nameof(LastRunText), nameof(RoutesText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}

/// <summary>分支列表的一行。</summary>
public class BranchRow
{
    public string Name { get; set; } = string.Empty;
    public BranchState State { get; set; }
    public string StateLabel { get; set; } = string.Empty;
    public string Delta { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public Brush StateFg => State switch
    {
        BranchState.Behind => new SolidColorBrush(Color.FromRgb(0x00, 0x3E, 0x92)),
        BranchState.New => new SolidColorBrush(Color.FromRgb(0x00, 0x57, 0x5C)),
        BranchState.Diverged => new SolidColorBrush(Color.FromRgb(0x7A, 0x48, 0x00)),
        BranchState.InternalAhead => new SolidColorBrush(Color.FromRgb(0x4B, 0x35, 0x96)),
        BranchState.VendorGone => new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)),
        _ => new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F))
    };
    public Brush StateBg => State switch
    {
        BranchState.Behind => new SolidColorBrush(Color.FromRgb(0xDC, 0xE9, 0xF8)),
        BranchState.New => new SolidColorBrush(Color.FromRgb(0xDC, 0xF2, 0xF0)),
        BranchState.Diverged => new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xCE)),
        BranchState.InternalAhead => new SolidColorBrush(Color.FromRgb(0xEB, 0xE5, 0xF8)),
        BranchState.VendorGone => new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3)),
        _ => new SolidColorBrush(Color.FromRgb(0xE3, 0xF3, 0xE3))
    };
}
