using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarcelinoClient;

public partial class MainWindow
{
    private sealed record RemoteBackupEntry(string Name, string FullPath, bool IsDirectory, string Kind);
    private readonly BackupTransfer _backupTransfer = new();
    private readonly List<LocalizedText> _backupLogEntries = [];
    private string? _remoteDirectory;
    private string? _remoteShareRoot;
    private string? _backupDestination;
    private CancellationTokenSource? _transferCancellation;
    private int _backupGeneration;
    private int _remoteBrowseGeneration;
    private bool _remoteBrowsing;
    private LocalizedText? _backupProgressMessage;

    private void StartBackupSession()
    {
        _backupGeneration++;
        RestoreBackupDestination();
        _ = LoadRemoteEntriesAsync();
        UpdateBackupControls();
    }

    private void ResetBackupSession()
    {
        _backupGeneration++;
        _remoteBrowseGeneration++;
        _transferCancellation?.Cancel();
        _remoteBrowsing = false;
        _remoteDirectory = null;
        _remoteShareRoot = null;
        _backupDestination = null;
        RemoteEntriesGrid.ItemsSource = null;
        RemotePathText.Text = Localization.Get("NoConnection");
        DestinationText.Text = Localization.Get("NoDestination");
        BackupProgressBar.IsIndeterminate = false;
        BackupProgressBar.Value = 0;
        _backupProgressMessage = Localization.Message("AwaitingTransfer");
        BackupProgressText.Text = _backupProgressMessage.Format();
        _backupLogEntries.Clear();
        BackupLog.Clear();
    }

    private async Task LoadRemoteEntriesAsync()
    {
        var host = _connectedHost;
        if (host is null)
        {
            return;
        }

        var generation = ++_remoteBrowseGeneration;
        var directory = _remoteDirectory;
        var shares = SharesList.Items.Cast<string>().ToArray();
        _remoteBrowsing = true;
        UpdateBackupControls();
        RemotePathText.Text = directory ?? $"\\\\{host.Address}\\";
        try
        {
            var entries = await Task.Run(() => directory is null
                ? shares.Select(share => new RemoteBackupEntry(share, $"\\\\{host.Address}\\{share}", true, Localization.Get("RemoteShareKind"))).ToArray()
                : new DirectoryInfo(directory).EnumerateFileSystemInfos()
                    .Where(entry => (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(entry => entry is DirectoryInfo).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new RemoteBackupEntry(entry.Name, entry.FullName, entry is DirectoryInfo, Localization.Get(entry is DirectoryInfo ? "RemoteFolderKind" : "RemoteFileKind"))).ToArray());
            if (generation == _remoteBrowseGeneration && ReferenceEquals(host, _connectedHost))
            {
                RemoteEntriesGrid.ItemsSource = entries;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (generation == _remoteBrowseGeneration)
            {
                RemoteEntriesGrid.ItemsSource = null;
                AppendBackupLog(Localization.Message("BackupListFailed", error.Message));
            }
        }
        finally
        {
            if (generation == _remoteBrowseGeneration)
            {
                _remoteBrowsing = false;
                UpdateBackupControls();
            }
        }
    }

    private async void RemoteEntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_transferCancellation is not null || RemoteEntriesGrid.SelectedItem is not RemoteBackupEntry { IsDirectory: true } entry)
        {
            return;
        }

        _remoteShareRoot ??= entry.FullPath;
        _remoteDirectory = entry.FullPath;
        await LoadRemoteEntriesAsync();
    }

    private async void RemoteUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_remoteDirectory, _remoteShareRoot, StringComparison.OrdinalIgnoreCase))
        {
            _remoteDirectory = null;
            _remoteShareRoot = null;
        }
        else if (_remoteDirectory is not null)
        {
            _remoteDirectory = Path.GetDirectoryName(_remoteDirectory);
        }

        await LoadRemoteEntriesAsync();
    }

    private async void RefreshRemoteButton_Click(object sender, RoutedEventArgs e) => await LoadRemoteEntriesAsync();

    private void RemoteEntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBackupControls();

    private void RemoveSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupSourcesList.SelectedItem is string source)
        {
            BackupSourcesList.Items.Remove(source);
            SaveClientSettings();
            UpdateBackupControls();
        }
    }

    private async void StartBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var destination = _backupDestination;
        var sources = BackupSourcesList.Items.Cast<string>().ToArray();
        if (_connectedHost is null || destination is null || sources.Length == 0 || _transferCancellation is not null)
        {
            return;
        }

        if (MessageBox.Show(this, Localization.Get("ConfirmBackup", sources.Length, destination),
            Localization.Get("ConfirmBackupTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunTransferAsync((progress, token) => _backupTransfer.BackupAsync(sources, destination, progress, token), Localization.Message("BackupOperation"));
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var source = (RemoteEntriesGrid.SelectedItem as RemoteBackupEntry)?.FullPath ?? _remoteDirectory;
        if (_connectedHost is null || source is null || _transferCancellation is not null)
        {
            return;
        }

        var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = Localization.Get("RestoreFolderDialog") };
        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        var destination = folderDialog.FolderName;
        var incremental = IncrementalRestoreInput.IsChecked == true;
        var finalPath = Path.Combine(destination, BackupTransfer.GetRootName(source));
        if (MessageBox.Show(this, Localization.Get("ConfirmRestore", source, finalPath),
            Localization.Get("ConfirmRestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunTransferAsync((progress, token) => _backupTransfer.RestoreAsync(source, destination, incremental, progress, token), Localization.Message("RestoreOperation"));
    }

    private async Task RunTransferAsync(Func<IProgress<TransferProgress>, CancellationToken, Task<TransferResult>> operation, LocalizedText title)
    {
        var generation = _backupGeneration;
        var cancellation = new CancellationTokenSource();
        _transferCancellation = cancellation;
        UpdateBackupControls();
        BackupProgressBar.Value = 0;
        BackupProgressBar.IsIndeterminate = true;
        AppendBackupLog(Localization.Message("BackupLogStarted", title));
        var progress = new Progress<TransferProgress>(update =>
        {
            if (generation != _backupGeneration)
            {
                return;
            }

            BackupProgressBar.IsIndeterminate = update.Total == 0;
            BackupProgressBar.Value = update.Total > 0 ? 100.0 * update.Completed / update.Total : 0;
            SetBackupProgress(Localization.Message("TransferProgress", update.Completed, update.Total, update.Copied, update.Skipped, update.Failed));
            AppendBackupLog(update.LocalizedMessage!);
        });
        try
        {
            var result = await operation(progress, cancellation.Token);
            if (generation == _backupGeneration)
            {
                BackupProgressBar.Value = 100;
                SetBackupProgress(Localization.Message("BackupFinished", title, result.Copied, result.Skipped, result.Failed));
                AppendBackupLog(_backupProgressMessage!);
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _backupGeneration)
            {
                SetBackupProgress(Localization.Message("BackupCancelled", title));
                AppendBackupLog(_backupProgressMessage!);
            }
        }
        catch (Exception error)
        {
            if (generation == _backupGeneration)
            {
                SetBackupProgress(Localization.Message("BackupFailed", error.Message));
                AppendBackupLog(_backupProgressMessage!);
            }
        }
        finally
        {
            _transferCancellation = null;
            cancellation.Dispose();
            BackupProgressBar.IsIndeterminate = false;
            UpdateBackupControls();
            if (generation == _backupGeneration)
            {
                await LoadRemoteEntriesAsync();
            }
        }
    }

    private void CancelTransferButton_Click(object sender, RoutedEventArgs e)
    {
        _transferCancellation?.Cancel();
        CancelTransferButton.IsEnabled = false;
        SetBackupProgress(Localization.Message("CancelingTransfer"));
    }

    private void UpdateBackupControls()
    {
        if (StartBackupButton is null)
        {
            return;
        }

        var idle = _transferCancellation is null;
        var connected = _connectedHost is not null;
        StartBackupButton.IsEnabled = connected && idle && _backupDestination is not null && BackupSourcesList.Items.Count > 0;
        RestoreBackupButton.IsEnabled = connected && idle && !_remoteBrowsing && (RemoteEntriesGrid.SelectedItem is RemoteBackupEntry || _remoteDirectory is not null);
        CancelTransferButton.IsEnabled = !idle;
        RemoteEntriesGrid.IsEnabled = idle && !_remoteBrowsing;
        RemoteUpButton.IsEnabled = connected && idle && !_remoteBrowsing && _remoteDirectory is not null;
        RefreshRemoteButton.IsEnabled = connected && idle && !_remoteBrowsing;
        SetDestinationButton.IsEnabled = connected && idle && !_remoteBrowsing;
        AddSourceButton.IsEnabled = idle;
        RemoveSourceButton.IsEnabled = idle;
        IncrementalRestoreInput.IsEnabled = idle;
    }

    private void SetBackupProgress(LocalizedText message)
    {
        _backupProgressMessage = message;
        BackupProgressText.Text = message.Format();
    }

    private void AppendBackupLog(LocalizedText message)
    {
        var rendered = message.Format();
        if (BackupLog.Text.Length + rendered.Length > 100000)
        {
            _backupLogEntries.Clear();
            BackupLog.Clear();
        }

        _backupLogEntries.Add(message);
        BackupLog.AppendText(rendered + Environment.NewLine);
        BackupLog.ScrollToEnd();
    }

    private void RefreshBackupLocalizedText()
    {
        if (_backupProgressMessage is not null)
        {
            BackupProgressText.Text = _backupProgressMessage.Format();
        }

        BackupLog.Text = string.Join(Environment.NewLine, _backupLogEntries.Select(message => message.Format()));
        if (_backupLogEntries.Count > 0)
        {
            BackupLog.AppendText(Environment.NewLine);
        }

        BackupLog.ScrollToEnd();
    }
}