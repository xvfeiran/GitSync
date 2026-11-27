using System.Windows;
using System.Windows.Controls;
using VendorSync.Core;

namespace VendorSync.UI;

/// <summary>
/// HTTPS 凭据弹窗：当远程需要账号/PAT 而本机还没有保存时弹出。
/// 输入经 git credential approve 交给 Windows 凭据管理器，本工具不留存。
/// </summary>
public partial class CredentialPromptDialog : Window
{
    private readonly string _url;
    private readonly HttpsCredentialManager _manager;

    public CredentialPromptDialog(Window owner, GitClient git, Action<string> log, string remoteName, string url)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        Owner = owner;
        _url = url;
        _manager = new HttpsCredentialManager(git, log);

        HeaderText.Text = $"远程「{remoteName}」需要登录凭据才能继续";
        var target = RemoteUrl.GetHttpsTarget(url);
        HeaderText.Text = $"远程「{remoteName}」需要登录凭据才能继续";
        HostText.Text = target?.Host ?? url;
        UserBox.Text = target?.Username ?? Environment.UserName;
    }

    private void ShowToggle_Changed(object sender, RoutedEventArgs e)
    {
        // PasswordBox 原生不支持明文切换，用附加的 TextBox 替换展示。
        if (ShowToggle.IsChecked == true)
        {
            PlainBox.Visibility = Visibility.Visible;
            PassBox.Visibility = Visibility.Collapsed;
            PlainBox.Text = PassBox.Password;
            PlainBox.Focus();
        }
        else
        {
            PassBox.Visibility = Visibility.Visible;
            PlainBox.Visibility = Visibility.Collapsed;
            PassBox.Password = PlainBox.Text;
            PassBox.Focus();
        }
    }

    private string Password => ShowToggle.IsChecked == true ? PlainBox.Text : PassBox.Password;

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var status = _manager.Save(_url, UserBox.Text.Trim(), Password);
        if (!status.Ok)
        {
            Prompt.Warn(this, status.Summary + "\r\n\r\n" + status.Detail, "保存失败");
            return;
        }
        DialogResult = true;
    }
}
