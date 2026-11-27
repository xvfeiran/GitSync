using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VendorSync.Core;

namespace VendorSync.UI;

public partial class AddProjectDialog : Window
{
    private readonly GitClient _git;
    private readonly Action<string> _log;

    /// <summary>添加成功后的项目配置。</summary>
    public RepoProfile? Result { get; private set; }

    public AddProjectDialog(GitClient git, Action<string> log)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        _git = git;
        _log = log;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Add.FolderDialogTitle") };
        if (Directory.Exists(PathBox.Text)) dialog.InitialDirectory = PathBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
            FillRemotes();
        }
    }

    private void BtnClone_Click(object sender, RoutedEventArgs e)
    {
        var clone = new CloneDialog(PathBox.Text.Trim()) { Owner = this };
        if (clone.ShowDialog() != true) return;

        // 克隆成功：目标目录有了，源远程 origin、目标远程 bitbucket（若填了）已自动挂好。
        PathBox.Text = clone.DestinationDir;
        VendorBox.Text = "origin";
        InternalBox.Text = string.IsNullOrWhiteSpace(clone.TargetUrl) ? "bitbucket" : "bitbucket";
        FillRemotes();
    }

    private void FillRemotes()
    {
        if (_git is null || !Directory.Exists(PathBox.Text)) return;
        var remotes = _git.GetRemotes(PathBox.Text.Trim());
        if (remotes.Count == 0) return;

        VendorBox.Items.Clear();
        InternalBox.Items.Clear();
        foreach (var r in remotes) { VendorBox.Items.Add(r); InternalBox.Items.Add(r); }

        VendorBox.Text = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault() ?? "origin";
        InternalBox.Text = remotes.Contains("bitbucket") ? "bitbucket" : remotes.LastOrDefault() ?? "bitbucket";
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (path.Length == 0)
        {
            Prompt.Warn(this, Loc.T("Add.WarnNoPath"), Loc.T("Caption.MissingDir"));
            return;
        }
        if (_git is null || !_git.IsRepository(path))
        {
            Prompt.Warn(this, Loc.T("Add.WarnNotRepo"), Loc.T("Caption.NotARepo"));
            return;
        }

        var vendor = VendorBox.Text.Trim();
        var internalRemote = InternalBox.Text.Trim();
        if (vendor.Length == 0 || internalRemote.Length == 0)
        {
            Prompt.Warn(this, Loc.T("Add.WarnNoRemotes"), Loc.T("Caption.MissingRemote"));
            return;
        }

        Result = new RepoProfile
        {
            Path = path,
            VendorRemote = vendor,
            InternalRemote = internalRemote,
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim()
        };
        DialogResult = true;
    }
}
