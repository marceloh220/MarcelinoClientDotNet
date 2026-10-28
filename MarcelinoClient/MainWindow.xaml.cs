using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Media;

namespace MarcelinoClient;

public partial class MainWindow : Window
{
    private readonly SmbClient _smbClient = new();
    private SmbHost? _connectedHost;
    private string[] _serverShares = [];

    public MainWindow() : this(new ClientSettingsStore(ClientSettingsStore.DefaultPath))
    {
    }

    public MainWindow(ClientSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();
        InitializeSshTerminal();
        InitializeClientSettings();
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var dark = ((ComboBox)sender).SelectedIndex == 1;
        var colors = new Dictionary<string, string>
        {
            ["PageBrush"] = dark ? "#181D20" : "#F1F4F2",
            ["SurfaceBrush"] = dark ? "#242B2F" : "#FFFFFF",
            ["InputBrush"] = dark ? "#121719" : "#FFFFFF",
            ["TextBrush"] = dark ? "#EDF2F0" : "#202B27",
            ["MutedBrush"] = dark ? "#B1BFB9" : "#5E6D67",
            ["BorderBrush"] = dark ? "#52605A" : "#C9D3CE",
            ["ButtonBrush"] = dark ? "#303B36" : "#F7F9F7",
            ["HoverBrush"] = dark ? "#3D5348" : "#DBE9E2",
            ["PanelBrush"] = dark ? "#2B3530" : "#E5EBE7",
            ["AccentBrush"] = dark ? "#286F60" : "#176B5B",
            ["ConnectBrush"] = dark ? "#36A269" : "#237A45",
            ["DisconnectBrush"] = dark ? "#D94F49" : "#B42318"
        };

        foreach (var (key, color) in colors)
        {
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        SaveClientSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeWindowWorkArea();
        DisposeTrayIcon();
        SaveClientSettings();
        ResetBackupSession();
        ResetSshAuthentication();
        StopStatusMonitoring();
        if (_connectedHost is not null)
        {
            _smbClient.Disconnect(_connectedHost.Address);
        }

        base.OnClosed(e);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        HostsList.Items.Clear();
        SetStatus(Localization.Get("ScanSearching"));

        try
        {
            var hosts = await _smbClient.DiscoverHostsAsync(CancellationToken.None);
            foreach (var host in hosts)
            {
                HostsList.Items.Add(host);
            }

            SetStatus(hosts.Count == 0
                ? Localization.Get("ScanNone")
                : Localization.Get("ScanComplete", hosts.Count));
        }
        catch (Exception error)
        {
            SetStatus(Localization.Get("ScanFailed", error.Message));
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void HostsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HostsList.SelectedItem is SmbHost host)
        {
            HostInput.Text = host.Address;
        }
    }

    private void LoginInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (sender == DomainInput)
        {
            UsernameInput.Focus();
        }
        else
        {
            PasswordInput.Focus();
        }
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConnectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (RevealedPasswordInput is not null && RevealedPasswordInput.Visibility == Visibility.Visible &&
            RevealedPasswordInput.Text != PasswordInput.Password)
        {
            RevealedPasswordInput.Text = PasswordInput.Password;
        }
    }

    private void RevealedPasswordInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RevealedPasswordInput.Visibility == Visibility.Visible && PasswordInput.Password != RevealedPasswordInput.Text)
        {
            PasswordInput.Password = RevealedPasswordInput.Text;
        }
    }

    private void SetLoginPasswordVisibility(bool revealed)
    {
        RevealedPasswordInput.Visibility = Visibility.Collapsed;
        RevealedPasswordInput.Text = revealed ? PasswordInput.Password : "";
        PasswordInput.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
        RevealedPasswordInput.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
        PasswordVisibilityButton.Tag = revealed ? "\uED1A" : "\uE890";
        var resource = revealed ? "HidePassword" : "ShowPassword";
        PasswordVisibilityButton.SetResourceReference(ToolTipProperty, resource);
        PasswordVisibilityButton.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, resource);
    }

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        var revealed = RevealedPasswordInput.Visibility != Visibility.Visible;
        SetLoginPasswordVisibility(revealed);
        if (revealed)
        {
            RevealedPasswordInput.Focus();
            RevealedPasswordInput.CaretIndex = RevealedPasswordInput.Text.Length;
        }
        else
        {
            PasswordInput.Focus();
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        SaveClientSettings();
        var host = HostInput.Text.Trim();
        var username = UsernameInput.Text.Trim();
        var password = PasswordInput.Password;
        var domain = DomainInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            SetStatus(Localization.Get("EnterCredentials"));
            return;
        }

        ResetBackupSession();
        ResetSshAuthentication();
        StopStatusMonitoring();
        if (_connectedHost is not null)
        {
            _smbClient.Disconnect(_connectedHost.Address);
            _connectedHost = null;
        }

        SetBusy(true);
            SharesList.Items.Clear();
            SharesSummary.Text = Localization.Get("ValidatingAccess", host);
        SetStatus(Localization.Get("ValidatingAccess", host));

        try
        {
            var shares = await Task.Run(() => _smbClient.ListShares(
                host, username, password, domain));
            _serverShares = shares.ToArray();
            var visibleShares = new MonitorStatus().FilterBrowseableShares(_serverShares);

            foreach (var share in visibleShares)
            {
                SharesList.Items.Add(share);
            }

            _connectedHost = new SmbHost(host, host);
            LoginView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            SessionText.Text = Localization.Get("Session", username, host);
            SharesSummary.Text = Localization.Get("ShareCount", visibleShares.Length);
            SetSshAuthentication(username, password);
            PasswordInput.Clear();
            SetLoginPasswordVisibility(false);
            MainTabs.SelectedIndex = 0;
            SetStatus(visibleShares.Length == 0
                ? Localization.Get("NoDiskShares")
                : Localization.Get("ConnectionValidated", visibleShares.Length));
            DisconnectButton.IsEnabled = true;
            StartBackupSession();
            StartStatusMonitoring();
        }
        catch (Exception error)
        {
            _connectedHost = null;
            SharesSummary.Text = Localization.Get("NoConnection");
            SetStatus(Localization.Get("ConnectionFailed", host, error.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectFromServer(Localization.Get("Disconnected"));
    }

    private void DisconnectFromServer(string message)
    {
        SaveClientSettings();
        ResetBackupSession();
        ResetSshAuthentication();
        StopStatusMonitoring();
        var host = _connectedHost;
        _connectedHost = null;
        if (host is not null)
        {
            try
            {
                _smbClient.Disconnect(host.Address);
            }
            catch (Exception error)
            {
                message = Localization.Get("SmbDisconnectFailed", message, error.Message);
            }
        }

        _serverShares = [];
        SharesList.Items.Clear();
        SharesSummary.Text = Localization.Get("NoSharesYet");
        SessionText.Text = Localization.Get("Unauthenticated");
        DestinationText.Text = Localization.Get("NoDestination");
        MainTabs.SelectedIndex = 0;
        MainView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        PasswordInput.Clear();
        SetLoginPasswordVisibility(false);
        DisconnectButton.IsEnabled = false;
        SetSshActionButtonState(DisconnectButton, disconnecting: false);
        SetStatus(message);
    }

    private void OpenSharesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectedHost is null)
        {
            return;
        }

        OpenInExplorer($"\\\\{_connectedHost.Address}");
    }

    private void SharesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_connectedHost is not null && SharesList.SelectedItem is string share)
        {
            OpenInExplorer($"\\\\{_connectedHost.Address}\\{share}");
        }
    }

    private void SetDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        var path = RemoteEntriesGrid.SelectedItem is RemoteBackupEntry { IsDirectory: true } entry ? entry.FullPath : _remoteDirectory;
        if (_connectedHost is null || path is null)
        {
            SetStatus(Localization.Get("ChooseRemoteFolder"));
            return;
        }

        _backupDestination = path;
        DestinationText.Text = Localization.Get("DestinationPath", path);
        SaveClientSettings();
        UpdateBackupControls();
    }

    private void AddSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localization.Get("SourceFolderDialog")
        };

        if (folderDialog.ShowDialog(this) == true && !BackupSourcesList.Items.Contains(folderDialog.FolderName))
        {
            BackupSourcesList.Items.Add(folderDialog.FolderName);
            SaveClientSettings();
            UpdateBackupControls();
        }
    }

    private void SelectSshKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var fileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.Get("SshKeyDialog"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (fileDialog.ShowDialog(this) == true)
        {
            SshKeyPathInput.Text = fileDialog.FileName;
            SaveClientSettings();
        }
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            SetStatus(Localization.Get("ExplorerFailed", error.Message));
        }
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        DisconnectButton.IsEnabled = !busy && _connectedHost is not null;
        SetSshActionButtonState(DisconnectButton, disconnecting: _connectedHost is not null);
        HostInput.IsEnabled = !busy;
        DomainInput.IsEnabled = !busy;
        UsernameInput.IsEnabled = !busy;
        PasswordInput.IsEnabled = !busy;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }
}