using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VendorSync.Core;

namespace VendorSync.UI;

/// <summary>
/// 远程仓库设置：给当前项目选/填两个远程的名字和地址。
/// 保存时直接落到本地仓库的 git 配置里（remote add / set-url），
/// 配置文件（vendorsync.config.json）仍然只记名字，不记地址。
/// </summary>
public partial class RemoteSettingsDialog : Window
{
    private readonly GitClient _git;
    private readonly string _repo;
    private bool _loading = true;

    public string VendorRemote => VendorBox.Text.Trim();
    public string InternalRemote => InternalBox.Text.Trim();

    public RemoteSettingsDialog(GitClient git, string repo, IReadOnlyList<string> remotes, string vendor, string internalRemote)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        _git = git;
        _repo = repo;

        foreach (var r in remotes)
        {
            VendorBox.Items.Add(r);
            InternalBox.Items.Add(r);
        }
        VendorBox.Text = vendor;
        InternalBox.Text = internalRemote;
        VendorUrlBox.Text = git.GetRemoteUrl(repo, vendor) ?? string.Empty;
        InternalUrlBox.Text = git.GetRemoteUrl(repo, internalRemote) ?? string.Empty;
        _loading = false;
    }

    // 从下拉框选一个已有远程时，把它的地址带出来，避免只换名字忘了换地址。
    private void VendorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _git is null) return;
        if (VendorBox.SelectedItem is string name)
            VendorUrlBox.Text = _git.GetRemoteUrl(_repo, name) ?? VendorUrlBox.Text;
    }

    private void InternalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _git is null) return;
        if (InternalBox.SelectedItem is string name)
            InternalUrlBox.Text = _git.GetRemoteUrl(_repo, name) ?? InternalUrlBox.Text;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 没有明确确定（点了取消或窗口关闭按钮）一律视为取消。
        DialogResult ??= false;
        base.OnClosing(e);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var vendor = VendorRemote;
        var internalRemote = InternalRemote;
        if (vendor.Length == 0 || internalRemote.Length == 0)
        {
            Prompt.Warn(this, "两个远程名都不能为空。", "缺少远程名");
            return;
        }
        if (string.Equals(vendor, internalRemote, StringComparison.Ordinal))
        {
            Prompt.Warn(this, "源仓库和目标仓库不能用同一个远程名。", "远程名重复");
            return;
        }
        if (_git is not null && Directory.Exists(_repo))
        {
            // 名字与地址一起落盘：远程不存在就 add，存在就 set-url。
            // 地址留空时视为"沿用该远程现在的地址"。
            foreach (var (name, urlBox) in new[] { (vendor, VendorUrlBox), (internalRemote, InternalUrlBox) })
            {
                var url = urlBox.Text.Trim();
                if (url.Length == 0) url = _git.GetRemoteUrl(_repo, name) ?? string.Empty;
                if (url.Length == 0)
                {
                    Prompt.Warn(this, $"远程「{name}」还没有地址，请填一个仓库地址（HTTPS 或 SSH）。", "缺少地址");
                    return;
                }
                if (!_git.EnsureRemote(_repo, name, url))
                {
                    Prompt.Warn(this, $"设置远程「{name}」失败，详情见日志。", "设置失败");
                    return;
                }
                AppLog.Write($"已设置远程 {name} → {url}");
            }
        }
        DialogResult = true;
    }
}
