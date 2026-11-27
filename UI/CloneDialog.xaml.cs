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
        var dialog = new OpenFolderDialog { Title = Loc.T("Clone.FolderDialogTitle") };
        if (Directory.Exists(DestBox.Text)) dialog.InitialDirectory = DestBox.Text;
        if (dialog.ShowDialog(this) == true) DestBox.Text = dialog.FolderName;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (SourceUrl.Trim().Length == 0)
        {
            Prompt.Warn(this, Loc.T("Clone.WarnNoSource"), Loc.T("Caption.MissingUrl"));
            return;
        }
        if (DestinationDir.Trim().Length == 0)
        {
            Prompt.Warn(this, Loc.T("Clone.WarnNoDest"), Loc.T("Caption.MissingDir"));
            return;
        }
        if (Directory.Exists(DestinationDir) && Directory.EnumerateFileSystemEntries(DestinationDir).Any())
        {
            Prompt.Warn(this, Loc.T("Clone.WarnDestNotEmpty"), Loc.T("Caption.DirNotEmpty"));
            return;
        }
        DialogResult = true;
    }
}
