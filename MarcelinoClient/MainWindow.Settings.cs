using System.IO;
using System.Security;
using System.Text.Json;
using System.Windows.Controls;

namespace MarcelinoClient;

public partial class MainWindow
{
    private readonly ClientSettingsStore _settingsStore;
    private ClientSettings _clientSettings = new();
    private bool _settingsReady;

    private void InitializeClientSettings()
    {
        try
        {
            _clientSettings = _settingsStore.Load();
            HostInput.Text = _clientSettings.Server;
            UsernameInput.Text = _clientSettings.Username;
            DomainInput.Text = _clientSettings.Domain;
            foreach (var source in _clientSettings.Sources)
            {
                BackupSourcesList.Items.Add(source);
            }

            SshKeyPathInput.Text = _clientSettings.SshKeyPath;
            ThemeSelector.SelectedIndex = _clientSettings.Theme == "dark" ? 1 : 0;
            LanguageSelector.ItemsSource = Localization.Languages;
            LanguageSelector.SelectedValue = _clientSettings.Language;
            Localization.Apply(_clientSettings.Language);
            RefreshLocalizedTableHeaders();
            _settingsReady = true;
            foreach (var input in new[] { HostInput, UsernameInput, DomainInput, SshKeyPathInput })
            {
                input.LostKeyboardFocus += (_, _) => SaveClientSettings();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            SetStatus(Localization.Get("SettingsLoadFailed", error.Message));
        }

        UpdateBackupControls();
    }

    private void SaveClientSettings()
    {
        if (!_settingsReady)
        {
            return;
        }

        _clientSettings.Server = HostInput.Text.Trim();
        _clientSettings.Username = UsernameInput.Text.Trim();
        _clientSettings.Domain = DomainInput.Text.Trim();
        _clientSettings.Sources = BackupSourcesList.Items.Cast<string>().ToArray();
        _clientSettings.SshKeyPath = SshKeyPathInput.Text.Trim();
        _clientSettings.Theme = ThemeSelector.SelectedIndex == 1 ? "dark" : "light";
        _clientSettings.Language = LanguageSelector.SelectedValue?.ToString() ?? "pt-BR";
        try
        {
            if (_connectedHost is not null && _backupDestination is not null)
            {
                _clientSettings.Destination = SavedBackupDestination.FromPath(_connectedHost.Address, _backupDestination);
            }

            _settingsStore.Save(_clientSettings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SecurityException or ArgumentException)
        {
            SetStatus(Localization.Get("SettingsSaveFailed", error.Message));
        }
    }

    private void RestoreBackupDestination()
    {
        _backupDestination = _connectedHost is null ? null : _clientSettings.Destination?.ForServer(_connectedHost.Address);
        DestinationText.Text = _backupDestination is null ? Localization.Get("NoDestination") : Localization.Get("DestinationPath", _backupDestination);
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var language = LanguageSelector.SelectedValue?.ToString();
        if (language is null)
        {
            return;
        }

        Localization.Apply(language);
        RefreshLocalizedTableHeaders();
        RefreshSshLocalizedText();
        RefreshBackupLocalizedText();
        SaveClientSettings();
    }

    private void RefreshLocalizedTableHeaders()
    {
        SetTableHeaders(FileSystemsGrid, "Mountpoint", "Device", "Format", "Usage", "FreeSpace");
        SetTableHeaders(PhysicalDisksGrid, "Device", "Model", "Capacity", "Temperature", "Smart");
        SetTableHeaders(ArraysGrid, "Array", "Level", "Status", "Disks", "Activity");
        SetTableHeaders(DhcpLeasesGrid, "Ip", "Hostname", "Mac", "ExpiresUtc");
        SetTableHeaders(SharesPermissionsGrid, "Share", "Path", "UsersGroups", "ReadOnly", "ReadWrite");
        SetTableHeaders(RemoteEntriesGrid, "Name", "Type");
    }

    private static void SetTableHeaders(DataGrid grid, params string[] resourceKeys)
    {
        for (var index = 0; index < grid.Columns.Count && index < resourceKeys.Length; index++)
        {
            grid.Columns[index].Header = Localization.Get(resourceKeys[index]);
        }
    }
}