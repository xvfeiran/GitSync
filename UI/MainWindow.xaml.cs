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
        // 先读配置并确定语言，再初始化界面：XAML 里的 loc 绑定取到的就是最终语言。
        _store = new ConfigStore();
        _config = _store.Load();
        _config.EnsureLists();
        Loc.LanguagePersist = lang => { _config.Language = lang; Persist(); };
        if (!string.IsNullOrWhiteSpace(_config.Language)) Loc.SetLanguage(_config.Language);
        Loc.LanguageChanged += OnLanguageChanged;

        InitializeComponent();
        Icon = AppIcon.Load();

        // 软件渲染（老电脑 / 远程桌面）：关闭阴影，进简洁模式。
        Neu.ReducedEffects = RenderCapability.Tier >> 16 == 0;

        CardList.ItemsSource = _projects;

        AppLog.Attach(AppendLog);
        TryEnableMica();
        Loaded += (_, _) => Startup();
    }

    /// <summary>语言切换后：重设 XAML 之外的动态文本（状态栏、日志行、摘要等）。</summary>
    private void OnLanguageChanged()
    {
        RunOnUi(() =>
        {
            LogOneLine.Text = Loc.T("Log.Title");
            BtnLogToggle.Content = Loc.T(_logExpanded ? "Log.Collapse" : "Log.Expand");
            RefreshHome();
            if (_current is not null) RenderDetail();
            if (_projects.Count == 0) SetStatus(Loc.T("Status.Ready"));
        });
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
        => Loc.SetLanguage(Loc.Language == "zh" ? "en" : "zh");

    // ------------------------------------------------------------- 启动

    private void Startup()
    {
        AppendLog(Loc.T("Startup.LogTitle"));
        SetAppInfo();
        _git = GitClient.Locate();
        if (_git is null)
        {
            AppendLog(Loc.T("Msg.NoGit"));
            const string gitUrl = "https://git-scm.com/download/win";
            AppendLog(Loc.F("Msg.GitInstallUrl", gitUrl));
            SetStatus(Loc.T("Status.NoGit"));
            var open = Prompt.Confirm(this,
                Loc.F("Confirm.NoGitText", gitUrl),
                Loc.T("Caption.NoGit"), defaultWhenSilent: false);
            if (open) OpenGitInstallPage();
            return;
        }
        EnvText.Text = "git " + _git.Run(new[] { "--version" }).StdOut.Trim().Replace("git version ", "");
        DiagnoseCredentials();

        foreach (var p in _config.Projects) _projects.Add(new ProjectVm(p));
        RefreshHome();
        SetStatus(_projects.Count == 0 ? Loc.T("Status.Ready") : Loc.F("Status.ReadyChecking", _projects.Count));

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
                    AppLog.Write(Loc.F("Log.AutoCheckError", ex));
                }
                SetStatus(Loc.T("Status.AutoCheckDone"));
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
        catch (Exception ex) { AppendLog(Loc.F("Log.OpenUrlFail", ex.Message)); }
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
                AppendLog(Loc.T("Log.DiagOk"));
                return;
            }
            AppendLog(Loc.F("Log.DiagProblem", diagnosis.Summary));
            if (diagnosis.ReplacementPath is null)
            {
                AppendLog(Loc.T("Log.DiagNoReplacement"));
                return;
            }
            _git.CredentialOverride = CredentialHelperResolver.BuildOverrideArgs(diagnosis);
            AppendLog(Loc.F("Log.DiagFixed", diagnosis.ReplacementPath));
        }
        catch (Exception ex)
        {
            AppendLog(Loc.F("Log.DiagError", ex.Message));
        }
    }

    // ------------------------------------------------------------- 首页

    private void RefreshHome()
    {
        EmptyHint.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pending = _projects.Count(p => p.Orb is OrbState.Pending);
        var failed = _projects.Count(p => p.Orb is OrbState.Failed);
        HomeSummary.Text = _projects.Count == 0 ? "" :
            failed > 0 ? Loc.F("Home.SummaryFail", _projects.Count, failed)
            : pending > 0 ? Loc.F("Home.SummaryPending", _projects.Count, pending)
            : Loc.F("Home.SummaryOk", _projects.Count);
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
            Prompt.Warn(this, Loc.T("Warn.DirMissing"), Loc.T("Caption.DirMissing"));
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
        SetStatus(Loc.F("Status.RemotesUpdated", vm.Model.DisplayName));
        RunInspect(vm);
    }

    private void MenuRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectVm vm) return;
        if (!Prompt.Confirm(this, Loc.F("Confirm.RemoveText", vm.Model.DisplayName), Loc.T("Caption.RemoveProject"))) return;
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
            Prompt.Info(this, Loc.T("Info.AddFirst"), Loc.T("Caption.Info"));
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
            SetStatus(Loc.F("Status.Added", existing.DisplayName));

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
                Loc.F("Branch.Summary", snap.Statuses.Count, snap.PendingBranches) +
                (snap.NewTagCount > 0 ? Loc.F("Branch.SummaryTags", snap.NewTagCount) : "");
            // 目标仓库独有的分支：一律保留不动，这里明说，免得用户担心被删。
            var extra = snap?.InternalOnlyBranches ?? Array.Empty<string>();
            DetailSummary.Text = snap is null ? "" :
                Loc.F("Summary.SourceBranches", snap.Statuses.Count, snap.PendingBranches) +
                (extra.Count == 0 ? "" :
                    Loc.F("Summary.TargetOnly", extra.Count,
                        string.Join("、", extra.Take(4)),
                        extra.Count > 4 ? Loc.T("Summary.ExtraSuffix") : ""));
            BranchList.ItemsSource = BuildBranchRows(snap, (BranchToggle.IsChecked == true));
            SetStep(StepConn, vm.Stage >= SyncStage.Connect && vm.Stage != SyncStage.Connect ? "done" : vm.Stage == SyncStage.Connect ? "running" : "pending");
            SetStep(StepCheck, vm.Stage >= SyncStage.Check ? "done" : vm.Stage == SyncStage.Check ? "running" : "pending");
            SetStep(StepPush, vm.Stage >= SyncStage.Push ? "done" : vm.Stage == SyncStage.Push ? "running" : "pending");
            SetStep(StepVerify, vm.Stage >= SyncStage.Verify ? "done" : vm.Stage == SyncStage.Verify ? "running" : "pending");
            DetailRing.Progress = vm.Fraction;
            DetailPercent.Text = vm.Fraction > 0 && vm.Fraction < 1 ? $"{(int)(vm.Fraction * 100)}%" : "";
            DetailKnobText.Text = Loc.T(vm.Busy ? "Detail.Syncing" : "Detail.Sync");
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
                    BranchState.Behind => Loc.F("Delta.Behind", s.VendorOnly),
                    BranchState.InternalAhead => Loc.F("Delta.InternalAhead", s.InternalOnly),
                    BranchState.Diverged => Loc.F("Delta.Diverged", s.VendorOnly, s.InternalOnly),
                    BranchState.New => Loc.T("Delta.New"),
                    _ => "—"
                },
                Plan = s.State switch
                {
                    BranchState.Behind => Loc.T("Plan.Behind"),
                    BranchState.New => Loc.T("Plan.New"),
                    BranchState.Diverged => Loc.T(snap.Strategy == SyncStrategy.Force ? "Plan.Overwrite" : "Plan.Merge"),
                    BranchState.InternalAhead => Loc.T(snap.Strategy == SyncStrategy.Force ? "Plan.Overwrite" : "Plan.Keep"),
                    BranchState.VendorGone => Loc.T("Plan.VendorGone"),
                    _ => Loc.T("Plan.UpToDate")
                },
                Result = needs ? Loc.T("Result.Pending") : s.State == BranchState.UpToDate ? Loc.T("Result.UpToDate") : ""
            });
        }
        // 目标仓库独有的分支：一并列出，明确标注保留不动。
        foreach (var b in snap.InternalOnlyBranches)
        {
            rows.Add(new BranchRow
            {
                Name = b,
                State = BranchState.VendorGone,
                StateLabel = Loc.T("StateLabel.InternalOnly"),
                Delta = "—",
                Plan = Loc.T("Plan.InternalOnly"),
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
        foreach (var (label, remote) in new[] { (Loc.T("Label.SourceRepo"), vm.Model.VendorRemote), (Loc.T("Label.TargetRepo"), vm.Model.InternalRemote) })
        {
            var url = _git.GetRemoteUrl(vm.Model.Path, remote);
            if (url is null || RemoteUrl.Detect(url) != RemoteScheme.Https) continue;
            var status = manager.Probe(url);
            if (status.Ok) continue;

            AppendLog(Loc.F("Log.CredNeeded", label, remote, status.Summary));
            var dialog = new CredentialPromptDialog(this, _git, AppendLog, $"{label} · {remote}", url);
            if (dialog.ShowDialog() != true)
            {
                AppendLog(Loc.T("Log.NoCredentials"));
                SetStatus(Loc.T("Status.NoCredentials"));
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
            vm.Message = Loc.T("Status.Checking");
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
                        vm.Message = snap.UpToDate ? Loc.T("Card.MsgUpToDate") : Loc.F("Card.MsgPending", snap.PendingBranches);
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
                AppLog.Write(Loc.F("Log.InspectError", ex));
                Dispatch(() =>
                {
                    vm.Busy = false;
                    vm.Orb = OrbState.Failed;
                    vm.Message = Loc.F("Msg.InspectErrorPrefix", ex.Message);
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
                    detail.AppendLine(Loc.F("Sync.ConfirmBranch", s.Name, s.InternalOnly, s.VendorOnly));
                    foreach (var c in s.InternalOnlyCommits.Take(3)) detail.AppendLine(Loc.T("Sync.ConfirmTargetLine") + c);
                }
                detail.AppendLine();
                detail.AppendLine(options.Strategy == SyncStrategy.Force
                    ? Loc.T("Sync.ConfirmForce")
                    : Loc.T("Sync.ConfirmMerge"));
                detail.Append(Loc.T("Sync.ConfirmContinue"));

                var answer = false;
                Invoke(() => { answer = Prompt.Confirm(this, detail.ToString(), Loc.T("Caption.ConfirmSync")); });
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
                    vm.Message = Loc.T("Card.Cancelled");
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
                        vm.Message = Loc.F("Sync.DonePushed", result.Pushed)
                            + (result.TagsPushed > 0 ? Loc.F("Sync.DoneTags", result.TagsPushed) : "")
                            + Loc.T("Sync.DoneSuffix");
                        vm.Model.LastHealth = ProjectHealth.UpToDate;
                        vm.Model.PendingCount = 0;
                    }
                    else
                    {
                        vm.Orb = OrbState.Failed;
                        vm.Message = Loc.F("Sync.PartialFail", result.Failed);
                        vm.Model.LastHealth = ProjectHealth.Failed;
                    }
                    vm.Model.LastMessage = vm.Message;
                }
                vm.Refresh();
                Persist();
                if (_current == vm) RenderDetail();
                RefreshHome();
                if (result.Error is not null)
                    Prompt.Warn(this, FirstLine(result.Error) + Loc.T("Sync.WarnDetailSuffix"), Loc.T("Caption.SyncFailed"));
                });
            }
            catch (Exception ex)
            {
                AppLog.Write(Loc.F("Log.SyncError", ex));
                Dispatch(() =>
                {
                    vm.Busy = false;
                    vm.Orb = OrbState.Failed;
                    vm.Message = Loc.F("Msg.SyncErrorPrefix", ex.Message);
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
        if (!_store.Save(_config, out var error)) AppendLog(Loc.F("Log.ConfigSaveFail", error));
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
        BtnLogToggle.Content = Loc.T(_logExpanded ? "Log.Collapse" : "Log.Expand");
        if (_logExpanded) LogBox.ScrollToEnd();
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogBox.Text)) return;
        try { Clipboard.SetText(LogBox.Text); SetStatus(Loc.T("Status.LogCopied")); }
        catch (ExternalException) { }
    }

    private void BtnCopyDiag_Click(object sender, RoutedEventArgs e)
    {
        var vm = _current;
        var text = new System.Text.StringBuilder()
            .AppendLine(Loc.T("Diag.Title"))
            .AppendLine(Loc.F("Diag.Time", DateTime.Now))
            .AppendLine(Loc.F("Diag.Project", vm?.Model.DisplayName ?? "—"))
            .AppendLine(Loc.F("Diag.Repo", vm?.Model.Path ?? "—"))
            .AppendLine(Loc.F("Diag.Remotes", vm?.Model.VendorRemote ?? "—", vm?.Model.InternalRemote ?? "—"))
            .AppendLine(Loc.F("Diag.Git", EnvText.Text))
            .AppendLine()
            .Append(LogBox.Text);
        try { Clipboard.SetText(text.ToString()); SetStatus(Loc.T("Status.DiagCopied")); }
        catch (ExternalException) { }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        LogOneLine.Text = Loc.T("Log.Title");
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
    public string Message { get; set; } = Loc.T("Card.NotChecked");
    public string LastRunText
    {
        get
        {
            var t = Model.LastSyncedAt ?? Model.LastCheckedAt;
            return t is null ? Loc.T("Card.NeverRun")
                : Loc.F(Model.LastSyncedAt is null ? "Card.LastCheck" : "Card.LastSync", t);
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
