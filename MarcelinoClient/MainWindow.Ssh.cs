using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MarcelinoClient;

public partial class MainWindow
{
    private string? _sshUsername;
    private string? _sshPassword;
    private CancellationTokenSource? _sshSession;
    private SshConnection? _sshConnection;
    private CancellationTokenSource? _sshTunnelSession;
    private SshTunnelConnection? _sshTunnelConnection;
    private LocalizedText? _sshStatusMessage;

    private void InitializeSshTerminal()
    {
        SshTerminal.DataToSend += data =>
        {
            try
            {
                _sshConnection?.Send(data);
            }
            catch (IOException error)
            {
                CloseSsh(Localization.Message("SshSendFailed", error.Message));
            }
        };
        SshTerminal.TerminalSizeChanged += ResizeSshTerminal;
        RefreshSshControls();
    }

    private void SetSshAuthentication(string username, string password)
    {
        _sshUsername = username;
        _sshPassword = password;
        RefreshSshControls();
        var port = SshPortInput.Text.Trim();
        SetSshStatus(Localization.Message("SshReady", username, _connectedHost?.Address,
            string.IsNullOrEmpty(port) ? 22 : port));
    }

    private void ResetSshAuthentication()
    {
        CloseSshTunnel(Localization.Message("LoginForSsh"));
        CloseSsh(Localization.Message("LoginForSsh"));
        _sshUsername = null;
        _sshPassword = null;
        SshPassphraseInput.Clear();
        RefreshSshControls();
    }

    private async void SshToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sshSession is not null)
        {
            CloseSsh(Localization.Message("SshDisconnected"));
            return;
        }

        if (_sshTunnelSession is not null)
        {
            return;
        }

        var host = _connectedHost?.Address;
        var username = _sshUsername;
        var password = _sshPassword;
        var portText = SshPortInput.Text.Trim();
        if (portText.Length == 0)
        {
            portText = "22";
        }

        if (!TryParsePort(portText, 22, allowDefault: true, out var port))
        {
            SetSshStatus(Localization.Message("InvalidSshPort"));
            return;
        }

        var keyPath = SshKeyPathInput.Text.Trim();
        var passphrase = SshPassphraseInput.Password;
        SshPassphraseInput.Clear();
        if (host is null || username is null || password is null)
        {
            SetSshStatus(Localization.Message("SshLoginFirst"));
            return;
        }

        var session = new CancellationTokenSource();
        var token = session.Token;
        _sshSession = session;
        SshTerminal.Reset();
        RefreshSshControls();
        SetSshStatus(Localization.Message("SshConnecting", username, host, port));
        var hostKeys = new SshHostKeyStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "marcelinoSrv", "ssh-host-keys.json"));

        try
        {
            var connection = await SshConnection.ConnectAsync(host, port, username, password, keyPath, passphrase,
                SshTerminal.Columns, SshTerminal.Rows, hostKeys, key => Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(_sshSession, session) || token.IsCancellationRequested)
                    {
                        return false;
                    }

                    return MessageBox.Show(this,
                        Localization.Get("TrustSshServer", key.Host, port, key.Algorithm, key.Fingerprint),
                        Localization.Get("TrustSshServerTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes;
                }, DispatcherPriority.Normal, token), token);

            if (!ReferenceEquals(_sshSession, session) || token.IsCancellationRequested)
            {
                await Task.Run(connection.Dispose);
                return;
            }

            _sshConnection = connection;
            SshTerminal.IsConnected = true;
            RefreshSshControls();
            SetSshStatus(Localization.Message("SshConnected", username, host, port));
            SshTerminal.Focus();
            ResizeSshTerminal(SshTerminal.Columns, SshTerminal.Rows);
            _ = RunSshTerminalAsync(connection, session, token);
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_sshSession, session))
            {
                CloseSsh(Localization.Message("SshConnectFailed", error.Message));
            }
        }
    }

    private async void SshTunnelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sshTunnelSession is not null)
        {
            CloseSshTunnel(Localization.Message(_sshTunnelConnection is null ? "SshTunnelCancelled" : "SshTunnelDisconnected"));
            return;
        }

        if (_sshSession is not null || _sshConnection is not null)
        {
            return;
        }

        var host = _connectedHost?.Address;
        var username = _sshUsername;
        var password = _sshPassword;
        if (host is null || username is null || password is null)
        {
            SetSshStatus(Localization.Message("SshLoginFirst"));
            return;
        }

        if (!TryParsePort(SshPortInput.Text, 22, allowDefault: true, out var sshPort))
        {
            SetSshStatus(Localization.Message("InvalidSshPort"));
            return;
        }

        if (!TryParsePort(SshRemotePortInput.Text, 0, allowDefault: false, out var remotePort) ||
            !TryParsePort(SshLocalPortInput.Text, 0, allowDefault: false, out var localPort))
        {
            SetSshStatus(Localization.Message("InvalidSshTunnelPorts"));
            return;
        }

        var keyPath = SshKeyPathInput.Text.Trim();
        var passphrase = SshPassphraseInput.Password;
        SshPassphraseInput.Clear();
        var session = new CancellationTokenSource();
        var token = session.Token;
        _sshTunnelSession = session;
        RefreshSshControls();
        SetSshStatus(Localization.Message("SshTunnelConnecting", localPort, remotePort));
        var hostKeys = new SshHostKeyStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "marcelinoSrv", "ssh-host-keys.json"));

        try
        {
            var connection = await SshTunnelConnection.ConnectAsync(host, sshPort, remotePort, localPort,
                username, password, keyPath, passphrase, hostKeys, key => Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(_sshTunnelSession, session) || token.IsCancellationRequested)
                    {
                        return false;
                    }

                    return MessageBox.Show(this,
                        Localization.Get("TrustSshServer", key.Host, sshPort, key.Algorithm, key.Fingerprint),
                        Localization.Get("TrustSshServerTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes;
                }, DispatcherPriority.Normal, token), token);

            if (!ReferenceEquals(_sshTunnelSession, session) || token.IsCancellationRequested)
            {
                await Task.Run(connection.Dispose);
                return;
            }

            _sshTunnelConnection = connection;
            RefreshSshControls();
            SetSshStatus(Localization.Message("SshTunnelConnected", localPort, remotePort));
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_sshTunnelSession, session))
            {
                CloseSshTunnel(Localization.Message("SshTunnelConnectFailed", error.Message));
            }
        }
    }

    private async Task RunSshTerminalAsync(SshConnection connection, CancellationTokenSource session, CancellationToken token)
    {
        var writer = Task.Run(() => connection.WriteInputAsync(token));
        var reader = Task.Run(async () =>
        {
            var buffer = new byte[32768];
            while (!token.IsCancellationRequested)
            {
                var count = connection.Shell.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    return;
                }

                var data = buffer.AsSpan(0, count).ToArray();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_sshSession, session))
                    {
                        SshTerminal.Feed(data);
                    }
                }, DispatcherPriority.Background, token);
            }
        });

        var message = Localization.Message("SshServerClosed");
        try
        {
            var completed = await Task.WhenAny(reader, writer);
            await completed;
        }
        catch (Exception error)
        {
            message = Localization.Message("SshConnectionEnded", error.Message);
        }
        finally
        {
            if (ReferenceEquals(_sshSession, session))
            {
                CloseSsh(message);
            }

            await Task.Run(connection.Dispose);
            try
            {
                await Task.WhenAll(reader, writer);
            }
            catch (Exception)
            {
            }
        }
    }

    private async void ResizeSshTerminal(int columns, int rows)
    {
        var connection = _sshConnection;
        var session = _sshSession;
        if (connection is null || session is null)
        {
            return;
        }

        var token = session.Token;
        try
        {
            await Task.Run(() => connection.ResizeAsync(columns, rows, token));
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_sshSession, session))
            {
                CloseSsh(Localization.Message("SshResizeFailed", error.Message));
            }
        }
    }

    private void CloseSsh(LocalizedText message)
    {
        var connection = _sshConnection;
        _sshConnection = null;
        var session = _sshSession;
        _sshSession = null;
        session?.Cancel();
        session?.Dispose();
        if (connection is not null)
        {
            _ = Task.Run(connection.Dispose);
        }

        SshTerminal.IsConnected = false;
        SshTerminal.Reset();
        RefreshSshControls();
        SetSshStatus(message);
    }

    private void CloseSshTunnel(LocalizedText message)
    {
        var connection = _sshTunnelConnection;
        _sshTunnelConnection = null;
        var session = _sshTunnelSession;
        _sshTunnelSession = null;
        session?.Cancel();
        session?.Dispose();
        if (connection is not null)
        {
            _ = Task.Run(connection.Dispose);
        }

        RefreshSshControls();
        SetSshStatus(message);
    }

    private void SetSshStatus(LocalizedText message)
    {
        _sshStatusMessage = message;
        SshStatusText.Text = message.Format();
    }

    private void RefreshSshLocalizedText()
    {
        if (_sshStatusMessage is not null)
        {
            SshStatusText.Text = _sshStatusMessage.Format();
        }

        RefreshSshControls();
    }

    private void RefreshSshControls() => SetSshControls(
        _sshSession is not null && _sshConnection is null,
        _sshConnection is not null,
        _sshTunnelSession is not null && _sshTunnelConnection is null,
        _sshTunnelConnection is not null);

    private void SetSshControls(bool connecting, bool connected, bool tunnelConnecting, bool tunnelConnected)
    {
        var terminalBusy = connecting || connected;
        var tunnelBusy = tunnelConnecting || tunnelConnected;
        var controlsEnabled = !terminalBusy && !tunnelBusy;
        SshAuthenticationPanel.Visibility = connected || tunnelConnected ? Visibility.Collapsed : Visibility.Visible;
        SshTunnelControlsPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        SshTerminalActionsPanel.Visibility = tunnelConnected ? Visibility.Collapsed : Visibility.Visible;
        SshTerminal.Visibility = tunnelConnected ? Visibility.Collapsed : Visibility.Visible;
        SshToggleButton.Content = connecting ? Localization.Get("SshCancelConnect") : connected ? Localization.Get("SshDisconnect") : Localization.Get("ConnectSsh");
        SshToggleButton.Tag = connecting ? "\uE711" : connected ? "\uE7E8" : "\uE8D4";
        SshToggleButton.IsEnabled = _sshUsername is not null && !tunnelBusy;
        SshTunnelToggleButton.Content = tunnelConnecting ? Localization.Get("SshCancelTunnel") :
            tunnelConnected ? Localization.Get("SshDisconnectTunnel") : Localization.Get("ConnectSshTunnel");
        SshTunnelToggleButton.Tag = tunnelConnecting ? "\uE711" : tunnelConnected ? "\uE7E8" : "\uE8D4";
        SshTunnelToggleButton.IsEnabled = _sshUsername is not null && !terminalBusy;
        SetSshActionButtonState(SshToggleButton, connected);
        SetSshActionButtonState(SshTunnelToggleButton, tunnelConnected);
        SshKeyPathInput.IsEnabled = controlsEnabled;
        SelectSshKeyButton.IsEnabled = controlsEnabled;
        SshPortInput.IsEnabled = controlsEnabled;
        SshRemotePortInput.IsEnabled = controlsEnabled;
        SshLocalPortInput.IsEnabled = controlsEnabled;
        SshPassphraseInput.IsEnabled = controlsEnabled;
        SshPasteButton.IsEnabled = connected;
    }

    private void SetSshActionButtonState(Button button, bool disconnecting)
    {
        var brush = disconnecting ? "DisconnectBrush" : "ConnectBrush";
        button.SetResourceReference(BackgroundProperty, brush);
        button.SetResourceReference(BorderBrushProperty, brush);
        button.SetResourceReference(ForegroundProperty, "SelectedTextBrush");
    }

    private static bool TryParsePort(string? value, int defaultPort, bool allowDefault, out int port)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) && allowDefault)
        {
            port = defaultPort;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
    }

    private static bool TryParseOptionalPort(string? value, out int port)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            port = 0;
            return true;
        }

        return TryParsePort(value, 0, allowDefault: false, out port);
    }

    private void SshPasteButton_Click(object sender, RoutedEventArgs e) => SshTerminal.PasteClipboard();
}