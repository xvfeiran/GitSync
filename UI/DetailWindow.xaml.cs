using System.Windows;
using VendorSync.Core;

namespace VendorSync.UI;

public partial class DetailWindow : Window
{
    public DetailWindow(BranchStatus s)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        Title = Loc.T("Title.Detail") + " · " + s.Name;
        HeaderText.Text = s.Name;
        MetaText.Text = Loc.F("Detail.Meta", s.StateLabel, s.VendorShort, s.InternalShort);

        if (s.InternalOnlyCommits.Count > 0)
        {
            TargetOnlyHeader.Text = Loc.F("Detail.TargetOnlyHeader", s.InternalOnlyCommits.Count);
            TargetOnlyBox.Text = string.Join(Environment.NewLine, s.InternalOnlyCommits);
            TargetOnlyBox.Visibility = TargetOnlyHeader.Visibility = Visibility.Visible;
        }
        if (s.VendorOnlyCommits.Count > 0)
        {
            SourceOnlyHeader.Text = Loc.F("Detail.SourceOnlyHeader", s.VendorOnlyCommits.Count);
            SourceOnlyBox.Text = string.Join(Environment.NewLine, s.VendorOnlyCommits);
            SourceOnlyBox.Visibility = SourceOnlyHeader.Visibility = Visibility.Visible;
        }
        if (s.InternalOnlyCommits.Count == 0 && s.VendorOnlyCommits.Count == 0)
        {
            MetaText.Text += "\r\n" + Loc.T("Detail.NoDiff");
        }
    }
}
