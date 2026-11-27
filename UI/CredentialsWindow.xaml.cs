using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VendorSync.Core;

namespace VendorSync.UI;

/// <summary>凭据与密钥向导：SSH 生成/登记指导 + HTTPS 状态检查。</summary>
public partial class CredentialsWindow : Window
{
    private readonly GitClient _git;
    private readonly AppConfig _config;
    private string? _repairHelperPath;

    public CredentialsWindow(GitClient git, AppConfig config)
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        _git = git;
        _config = config;
        KeyComment.Text = Environment.UserName + "@" + Environment.MachineName;
        RefreshKeys();
        RefreshHttpsStatus();
    }

    private string? GetRemoteUrl(string remote)
    {
        var repo = _config.RepoPath;
        if (string.IsNullOrWhiteSpace(repo) || !_git.IsRepository(repo)) return null;
        return _git.GetRemoteUrl(repo, remote);
    }

    private string? SshUrl => new[] { GetRemoteUrl(_config.InternalRemote), GetRemoteUrl(_config.VendorRemote) }
        .FirstOrDefault(u => RemoteUrl.Detect(u) == RemoteScheme.Ssh);

    private string? HttpsUrl => new[] { GetRemoteUrl(_config.InternalRemote), GetRemoteUrl(_config.VendorRemote) }
        .FirstOrDefault(u => RemoteUrl.Detect(u) == RemoteScheme.Https);

    // -------------------------------------------------------------- SSH

    private void RefreshKeys()
    {
        var manager = new SshKeyManager(_ => { });
        var keys = manager.FindKeys();

        KeyList.ItemsSource = keys.Select(k => new KeyItem(k)).ToList();

        if (keys.Count > 0)
        {
            SetStatus(SshStatus, true, $"找到 {keys.Count} 把密钥");
            KeyList.SelectedIndex = 0;
        }
        else
        {
            SetStatus(SshStatus, false, "本机还没有 SSH 密钥 —— 点「① 生成 ed25519 密钥」开始");
            PublicKey.Text = string.Empty;
        }
    }

    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyList.SelectedItem is KeyItem item) PublicKey.Text = item.Info.PublicKey;
    }

    private void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        if (!Prompt.Confirm(this,
                "将在 ~/.ssh 下生成一把新密钥，并把私钥权限限制为仅当前用户可读。\r\n私钥不会被上传到任何地方。继续？",
                "生成 SSH 密钥")) return;

        BtnGenerate.IsEnabled = false;
        try
        {
            var manager = new SshKeyManager(m => AppLog.Write(m));
            var (ok, message, key) = manager.Generate(KeyComment.Text.Trim());
            if (ok)
            {
                Prompt.Info(this, message + "\r\n\r\n下一步：点「② 复制公钥」，把它登记到 Git 服务器。", "已生成");
                RefreshKeys();
                if (key is not null) PublicKey.Text = key.PublicKey;
            }
            else Prompt.Warn(this, message, "生成失败");
        }
        finally { BtnGenerate.IsEnabled = true; }
    }

    private void CopyPublic_Click(object sender, RoutedEventArgs e)
    {
        var text = PublicKey.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Prompt.Info(this, "没有可复制的公钥。先选中一把密钥或生成新的。", "无可复制内容");
            return;
        }
        try
        {
            Clipboard.SetText(text);
            SetStatus(SshStatus, true, "公钥已复制 —— 下一步到登记页粘贴");
        }
        catch (Exception)
        {
            Prompt.Warn(this, "复制失败（剪贴板被其他程序占用）。", "复制失败");
        }
    }

    private void OpenRegisterPage_Click(object sender, RoutedEventArgs e)
    {
        // 从 SSH 远程地址推断 Git 服务器的网页地址（ssh://git@host:port → https://host）。
        var url = SshUrl;
        var host = url is null ? null : RemoteUrl.GetSshHost(url);
        if (string.IsNullOrEmpty(host))
        {
            Prompt.Info(this,
                "当前项目的远程都不是 SSH 地址，推断不出 Git 服务器的网页地址。\r\n" +
                "请自行打开你的 Git 服务器，进入个人设置 → SSH Keys 页面粘贴公钥。",
                "请手动打开登记页");
            return;
        }
        OpenUrl("https://" + host);
    }

    private void TestSsh_Click(object sender, RoutedEventArgs e)
    {
        var url = SshUrl;
        if (url is null)
        {
            Prompt.Info(this, "当前没有 SSH 远程地址可测试。", "无需测试");
            return;
        }
        var host = RemoteUrl.GetSshHost(url);
        var port = RemoteUrl.GetSshPort(url);
        if (string.IsNullOrEmpty(host)) return;

        BtnTest.IsEnabled = false;
        SshResult.Text = $"正在连接 git@{host}:{port} …";
        try
        {
            var manager = new SshKeyManager(m => AppLog.Write(m));
            var result = manager.TestConnection(host, port);
            SetStatus(SshStatus, result.Ok, result.Ok ? "SSH 认证成功，本地和远程已连通" : "SSH 认证失败");
            SshResult.Text = result.Message + (string.IsNullOrWhiteSpace(result.RawOutput)
                ? string.Empty : "\r\n\r\n" + result.RawOutput);
        }
        finally { BtnTest.IsEnabled = true; }
    }

    // ------------------------------------------------------------ HTTPS

    private void HttpsCheck_Click(object sender, RoutedEventArgs e) => RefreshHttpsStatus();

    private void RefreshHttpsStatus()
    {
        var repo = _config.RepoPath;
        if (string.IsNullOrWhiteSpace(repo) || !_git.IsRepository(repo))
        {
            SetStatus(HttpsStatus, false, "还没有选择有效的仓库目录");
            return;
        }

        var diagnosis = CredentialHelperResolver.Diagnose(_git);
        if (diagnosis.NeedsRepair)
        {
            SetStatus(HttpsStatus, false, "凭据辅助程序配置有问题，HTTPS 凭据无法使用");
            HttpsHint.Text = diagnosis.Detail + "\r\n本程序已自动绕过这个问题；要永久修好这台机器，点「永久修复本机 Git 配置」。";
            if (diagnosis.ReplacementPath is not null)
            {
                _repairHelperPath = diagnosis.ReplacementPath;
                BtnRepair.Visibility = Visibility.Visible;
            }
            return;
        }

        BtnRepair.Visibility = Visibility.Collapsed;

        var httpsUrl = HttpsUrl;
        if (httpsUrl is null)
        {
            SetStatus(HttpsStatus, true, "两个远程都是 SSH 地址，不需要 HTTPS 凭据");
            HttpsHint.Text = "你的配置走 SSH，请到「SSH 密钥」页生成并登记密钥。";
            return;
        }

        var manager = new HttpsCredentialManager(_git, m => AppLog.Write(m));
        var status = manager.Probe(httpsUrl);
        SetStatus(HttpsStatus, status.Ok, status.Summary);
        HttpsHint.Text = status.Detail + $"\r\n地址：{RemoteUrl.Redact(httpsUrl)}";
    }

    private void RepairHelper_Click(object sender, RoutedEventArgs e)
    {
        if (_repairHelperPath is null) return;
        if (!Prompt.Confirm(this,
                "将要执行：\r\n\r\n" +
                "  git config --global --replace-all credential.helper \"\"\r\n" +
                $"  git config --global --add credential.helper \"{_repairHelperPath}\"\r\n\r\n" +
                "这会把用户级 ~/.gitconfig 里的 credential.helper 改成可用的路径。\r\n只影响凭据辅助程序，不动其它配置。继续？",
                "永久修复 Git 凭据配置")) return;

        var (ok, message) = CredentialHelperResolver.PersistToUserConfig(_git, _repairHelperPath);
        if (ok)
        {
            _git.CredentialOverride = Array.Empty<string>();
            Prompt.Info(this, "已修好。之后这台机器上的所有 git 操作都会正常使用凭据。", "修复完成");
        }
        else Prompt.Warn(this, "修复失败：" + message, "修复失败");
        RefreshHttpsStatus();
    }

    private static void SetStatus(TextBlock label, bool ok, string text)
    {
        label.Text = (ok ? "✔ " : "✖ ") + text;
        label.SetResourceReference(TextBlock.ForegroundProperty, ok ? "OkBrush" : "ErrorBrush");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Prompt.Warn(null, "打不开浏览器：" + ex.Message, "打开失败");
        }
    }
}

/// <summary>SSH 密钥列表的一行。</summary>
public class KeyItem
{
    public SshKeyInfo Info { get; }

    public KeyItem(SshKeyInfo info) => Info = info;

    public string Display =>
        $"{System.IO.Path.GetFileName(Info.Path)}  ·  {Info.KeyType}  ·  {(string.IsNullOrEmpty(Info.Fingerprint) ? "（无法计算）" : Info.Fingerprint)}  ·  {Info.Comment}";
}
