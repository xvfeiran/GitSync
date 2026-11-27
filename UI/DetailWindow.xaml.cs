using System.Windows;
using VendorSync.Core;

namespace VendorSync.UI;

public partial class DetailWindow : Window
{
    public DetailWindow(BranchStatus s)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        Title = "分支详情 · " + s.Name;
        HeaderText.Text = s.Name;
        MetaText.Text = $"状态：{s.StateLabel}    源：{s.VendorShort}    目标：{s.InternalShort}";

        if (s.InternalOnlyCommits.Count > 0)
        {
            TargetOnlyHeader.Text = $"目标仓库独有提交（{s.InternalOnlyCommits.Count}）—— 覆盖策略下会被直接丢弃（不备份）";
            TargetOnlyBox.Text = string.Join(Environment.NewLine, s.InternalOnlyCommits);
            TargetOnlyBox.Visibility = TargetOnlyHeader.Visibility = Visibility.Visible;
        }
        if (s.VendorOnlyCommits.Count > 0)
        {
            SourceOnlyHeader.Text = $"源仓库独有提交（{s.VendorOnlyCommits.Count}）";
            SourceOnlyBox.Text = string.Join(Environment.NewLine, s.VendorOnlyCommits);
            SourceOnlyBox.Visibility = SourceOnlyHeader.Visibility = Visibility.Visible;
        }
        if (s.InternalOnlyCommits.Count == 0 && s.VendorOnlyCommits.Count == 0)
        {
            MetaText.Text += "\r\n没有额外的提交差异。";
        }
    }
}
