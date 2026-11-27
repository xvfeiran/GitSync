using System.Windows;
using Microsoft.Win32;

namespace VendorSync.UI;

public partial class CloneDialog : Window
{
    public string SourceUrl => SourceBox.Text;
    public string TargetUrl => TargetBox.Text;
    public string DestinationDir => DestBox.Text;

    public CloneDialog(string initialDir)
    {
        InitializeComponent();
        DestBox.Text = initialDir;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择克隆目标目录（需要是空目录）" };
        if (Directory.Exists(DestBox.Text)) dialog.InitialDirectory = DestBox.Text;
        if (dialog.ShowDialog(this) == true) DestBox.Text = dialog.FolderName;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (SourceUrl.Trim().Length == 0)
        {
            Prompt.Warn(this, "请填写源仓库地址。", "缺少地址");
            return;
        }
        if (DestinationDir.Trim().Length == 0)
        {
            Prompt.Warn(this, "请选择克隆到哪个目录。", "缺少目录");
            return;
        }
        if (Directory.Exists(DestinationDir) && Directory.EnumerateFileSystemEntries(DestinationDir).Any())
        {
            Prompt.Warn(this, "目标目录不是空的。请选一个空目录，git 才能克隆进去。", "目录非空");
            return;
        }
        DialogResult = true;
    }
}
