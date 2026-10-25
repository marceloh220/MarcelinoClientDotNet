using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MarcelinoClient;

public partial class MainWindow
{
    private string? _sshUsername;
    private string? _sshPassword;
    private CancellationTokenSource? _sshSession;
    private SshConnection? _sshConnection;
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
    }

    private void SetSshAuthentication(string username, string password)
    {
        _sshUsername = username;
        _sshPassword = password;
        SshToggleButton.IsEnabled = true;
        SetSshStatus(Localization.Message("SshReady", username, _connectedHost?.Address));
    }

    private void ResetSshAuthentication()
    {
        CloseSsh(Localization.Message("LoginForSsh"));
        _sshUsername = null;
        _sshPassword = null;
        SshToggleButton.IsEnabled = false;
        SshPassphraseInput.Clear();
    }

    private async void SshToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sshSession is not null)
        {
            CloseSsh(Localization.Message("SshDisconnected"));
            return;
        }

        var host = _connectedHost?.Address;
        var username = _sshUsername;
        var password = _sshPassword;
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
        SetSshControls(connecting: true);
        SetSshStatus(Localization.Message("SshConnecting", username, host));
        var hostKeys = new SshHostKeyStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "marcelinoSrv", "ssh-host-keys.json"));

        try
        {
            var connection = await SshConnection.ConnectAsync(host, username, password, keyPath, passphrase,
                SshTerminal.Columns, SshTerminal.Rows, hostKeys, key => Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(_sshSession, session) || token.IsCancellationRequested)
                    {
                        return false;
                    }

                    return MessageBox.Show(this,
                        Localization.Get("TrustSshServer", key.Host, key.Algorithm, key.Fingerprint),
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
            SetSshControls(connecting: false, connected: true);
            SetSshStatus(Localization.Message("SshConnected", username, host));
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
        SetSshControls(connecting: false);
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

        SetSshControls(connecting: _sshSession is not null && _sshConnection is null, connected: _sshConnection is not null);
    }

    private void SetSshControls(bool connecting, bool connected = false)
    {
        SshAuthenticationPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        SshToggleButton.Content = connecting ? Localization.Get("SshCancelConnect") : connected ? Localization.Get("SshDisconnect") : Localization.Get("ConnectSsh");
        SshToggleButton.Tag = connecting ? "\uE711" : connected ? "\uE7E8" : "\uE8D4";
        SshToggleButton.IsEnabled = _sshUsername is not null;
        SshKeyPathInput.IsEnabled = !connecting && !connected;
        SelectSshKeyButton.IsEnabled = !connecting && !connected;
        SshPassphraseInput.IsEnabled = !connecting && !connected;
        SshPasteButton.IsEnabled = connected;
    }

    private void SshPasteButton_Click(object sender, RoutedEventArgs e) => SshTerminal.PasteClipboard();
}