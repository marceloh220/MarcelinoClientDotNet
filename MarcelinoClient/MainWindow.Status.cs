using System.IO;
using System.Security;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MarcelinoClient;

public partial class MainWindow
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _statusReadLock = new(1, 1);
    private CancellationTokenSource? _statusSession;
    private bool _statusReadFailed;

    private void StartStatusMonitoring()
    {
        StopStatusMonitoring();
        _statusSession = new CancellationTokenSource();
        _statusTimer.Tick -= StatusTimer_Tick;
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    private void StopStatusMonitoring()
    {
        _statusTimer.Stop();
        _statusSession?.Cancel();
        _statusSession?.Dispose();
        _statusSession = null;
        _statusReadFailed = false;
        if (MainView is not null)
        {
            MainView.DataContext = new MonitorStatus();
            SystemOverview.Text = Localization.Get("AwaitingAuthentication");
        }
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e) => await RefreshStatusAsync();

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private void ApplyShareVisibility(MonitorStatus status)
    {
        var visibleShares = status.FilterBrowseableShares(_serverShares);
        var changed = !SharesList.Items.Cast<string>().SequenceEqual(visibleShares, StringComparer.OrdinalIgnoreCase);
        if (changed)
        {
            var selectedShare = SharesList.SelectedItem as string;
            SharesList.Items.Clear();
            foreach (var share in visibleShares)
            {
                SharesList.Items.Add(share);
            }

            SharesList.SelectedItem = visibleShares.FirstOrDefault(share =>
                string.Equals(share, selectedShare, StringComparison.OrdinalIgnoreCase));
        }

        SharesSummary.Text = Localization.Get("ShareCount", visibleShares.Length);
        if (changed && _remoteDirectory is null && _transferCancellation is null)
        {
            _ = LoadRemoteEntriesAsync();
        }
    }

    private void StatusIntervalInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusIntervalInput.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var seconds))
        {
            _statusTimer.Interval = TimeSpan.FromSeconds(seconds);
        }
    }

    private async Task RefreshStatusAsync()
    {
        var session = _statusSession;
        var host = _connectedHost;
        if (session is null || host is null || !await _statusReadLock.WaitAsync(0))
        {
            return;
        }

        var cancellationToken = session.Token;
        RefreshStatusButton.IsEnabled = false;
        try
        {
            var status = await Task.Run(() => _smbClient.ReadStatus(host.Address), cancellationToken);
            if (!ReferenceEquals(session, _statusSession) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            MainView.DataContext = status;
            ApplyShareVisibility(status);
            SystemOverview.Text = status.Overview;
            if (_statusReadFailed)
            {
                SetStatus(Localization.Get("MetricsRestored"));
                _statusReadFailed = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            if (ReferenceEquals(session, _statusSession) && !cancellationToken.IsCancellationRequested)
            {
                _statusReadFailed = true;
                DisconnectFromServer(Localization.Get("MetricsReadFailed"));
            }
        }
        finally
        {
            _statusReadLock.Release();
            RefreshStatusButton.IsEnabled = true;
        }
    }
}