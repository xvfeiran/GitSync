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
            SetStatus(SshStatus, true, Loc.F("Cred.FoundKeys", keys.Count));
            KeyList.SelectedIndex = 0;
        }
        else
        {
            SetStatus(SshStatus, false, Loc.T("Cred.NoKeys"));
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
                Loc.T("Cred.ConfirmGenerate"),
                Loc.T("Caption.GenSshKey"))) return;

        BtnGenerate.IsEnabled = false;
        try
        {
            var manager = new SshKeyManager(m => AppLog.Write(m));
            var (ok, message, key) = manager.Generate(KeyComment.Text.Trim());
            if (ok)
            {
                Prompt.Info(this, Loc.F("Cred.GeneratedNext", message), Loc.T("Caption.Generated"));
                RefreshKeys();
                if (key is not null) PublicKey.Text = key.PublicKey;
            }
            else Prompt.Warn(this, message, Loc.T("Caption.GenerateFail"));
        }
        finally { BtnGenerate.IsEnabled = true; }
    }

    private void CopyPublic_Click(object sender, RoutedEventArgs e)
    {
        var text = PublicKey.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Prompt.Info(this, Loc.T("Cred.NothingToCopy"), Loc.T("Caption.NothingToCopy"));
            return;
        }
        try
        {
            Clipboard.SetText(text);
            SetStatus(SshStatus, true, Loc.T("Cred.PubCopied"));
        }
        catch (Exception)
        {
            Prompt.Warn(this, Loc.T("Cred.CopyFail"), Loc.T("Caption.CopyFail"));
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
                Loc.T("Cred.NoSshUrl"),
                Loc.T("Caption.ManualRegister"));
            return;
        }
        OpenUrl("https://" + host);
    }

    private void TestSsh_Click(object sender, RoutedEventArgs e)
    {
        var url = SshUrl;
        if (url is null)
        {
            Prompt.Info(this, Loc.T("Cred.NoSshTest"), Loc.T("Caption.NoNeedTest"));
            return;
        }
        var host = RemoteUrl.GetSshHost(url);
        var port = RemoteUrl.GetSshPort(url);
        if (string.IsNullOrEmpty(host)) return;

        BtnTest.IsEnabled = false;
        SshResult.Text = Loc.F("Cred.Connecting", host, port);
        try
        {
            var manager = new SshKeyManager(m => AppLog.Write(m));
            var result = manager.TestConnection(host, port);
            SetStatus(SshStatus, result.Ok, result.Ok ? Loc.T("Cred.SshOk") : Loc.T("Cred.SshFail"));
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
            SetStatus(HttpsStatus, false, Loc.T("Cred.NoRepo"));
            return;
        }

        var diagnosis = CredentialHelperResolver.Diagnose(_git);
        if (diagnosis.NeedsRepair)
        {
            SetStatus(HttpsStatus, false, Loc.T("Cred.BrokenHelper"));
            HttpsHint.Text = Loc.F("Cred.BrokenHelperHint", diagnosis.Detail);
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
            SetStatus(HttpsStatus, true, Loc.T("Cred.AllSsh"));
            HttpsHint.Text = Loc.T("Cred.UseSshHint");
            return;
        }

        var manager = new HttpsCredentialManager(_git, m => AppLog.Write(m));
        var status = manager.Probe(httpsUrl);
        SetStatus(HttpsStatus, status.Ok, status.Summary);
        HttpsHint.Text = status.Detail + Loc.F("Cred.Addr", RemoteUrl.Redact(httpsUrl)).Insert(0, "\r\n");
    }

    private void RepairHelper_Click(object sender, RoutedEventArgs e)
    {
        if (_repairHelperPath is null) return;
        if (!Prompt.Confirm(this,
                Loc.T("Cred.ConfirmRepairHead") +
                Loc.T("Cred.ConfirmRepairLine1") +
                Loc.F("Cred.ConfirmRepairLine2", _repairHelperPath) +
                Loc.T("Cred.ConfirmRepairTail"),
                Loc.T("Caption.RepairGit"))) return;

        var (ok, message) = CredentialHelperResolver.PersistToUserConfig(_git, _repairHelperPath);
        if (ok)
        {
            _git.CredentialOverride = Array.Empty<string>();
            Prompt.Info(this, Loc.T("Cred.RepairOk"), Loc.T("Caption.RepairOk"));
        }
        else Prompt.Warn(this, Loc.F("Cred.RepairFail", message), Loc.T("Caption.RepairFail"));
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
            Prompt.Warn(null, Loc.F("Cred.BrowserFail", ex.Message), Loc.T("Caption.OpenFail"));
        }
    }
}

/// <summary>SSH 密钥列表的一行。</summary>
public class KeyItem
{
    public SshKeyInfo Info { get; }

    public KeyItem(SshKeyInfo info) => Info = info;

    public string Display =>
        $"{System.IO.Path.GetFileName(Info.Path)}  ·  {Info.KeyType}  ·  {(string.IsNullOrEmpty(Info.Fingerprint) ? Loc.T("KeyItem.FingerprintUnavailable") : Info.Fingerprint)}  ·  {Info.Comment}";
}
