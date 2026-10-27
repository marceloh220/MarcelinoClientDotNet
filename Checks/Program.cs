using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarcelinoClient;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        const string sample = """
        {
          "hostname": "nas-check", "sampled_at": "2026-10-23T12:00:00", "cpu_percent": 12.5,
          "cpu_count": 8, "cpu_frequency_mhz": 2400, "cpu_temperature_c": 42,
          "uptime_seconds": 90061,
          "memory": { "percent": 25, "used_bytes": 1073741824, "total_bytes": 4294967296 },
          "network": { "bytes_received": 2048, "bytes_sent": 1024 },
          "system_info": { "os_name": "Linux", "cpu_model": "Sample CPU", "gpu_models": ["GPU"],
            "package_count": { "count": 123, "manager": "apt" } },
          "filesystems": [{ "mountpoint": "/data", "device": "/dev/md0", "filesystem": "ext4", "percent": 40, "free_bytes": 1024 }],
          "physical_disks": [{ "name": "/dev/sda", "model": "Disk", "capacity_bytes": 1099511627776, "temperature_c": null, "smart_status": "PASSED" }],
          "raid": { "status": "OK", "arrays": [{ "name": "md0", "level": "raid1", "status": "Ativa", "active_devices": 2, "total_devices": 2, "sync_action": "resync", "progress_percent": 55.5 }] },
          "dns_dhcp": { "service_state": "active", "dns_enabled": true, "dhcp_enabled": true, "lease_count": 1,
            "leases": [{ "ip": "192.0.2.10", "hostname": "pc", "mac": "00:11:22:33:44:55", "expires_at": "2026-10-24" }],
            "allowed_events": [{ "timestamp": "12:00", "kind": "dns", "message": "allowed.example" }] },
          "internet": { "local_ip": "192.0.2.1", "remote_ip": "198.51.100.1", "tests": [
            { "timestamp": "old", "download_mbps": 100, "upload_mbps": 50, "ping_ms": 4 },
            { "timestamp": "new", "error": "offline" }] },
          "samba_shares": [
            { "name": "Monitor", "browseable": "no" },
            { "name": "Hidden", "browseable": false },
            { "name": "HiddenNo", "browseable": " NO " },
            { "name": "HiddenZero", "browseable": 0 },
            { "name": "HiddenFalse", "browseable": "false" },
            { "name": "Data", "path": "/data", "valid_users": "@users", "read_only": "no" },
            { "name": "Archive", "path": "/archive", "read_only": "yes", "write_list": "admin" }]
        }
        """;
        var status = MonitorStatus.Parse(sample);
        Check(status.Cpu == "12.5%" && status.CpuDetails.Contains("2.40 GHz"), "CPU");
        Check(status.MemoryDetails == "1.0 GB de 4.0 GB", "Memory units");
        Check(status.Uptime == "1d 1h" && status.Network.Contains("2.0 KB"), "Uptime/network");
        Check(status.Hardware.Single(entry => entry.Key == "Pacotes instalados").Value == "123 (apt)", "Hardware");
        Check(status.FileSystems.Single().Mountpoint == "/data" && status.Disks.Single().Temperature == "Indisponível", "Disks/null temperature");
        Check(status.Arrays.Single().Devices == "2/2" && status.Arrays.Single().Activity.Contains("55.5%"), "RAID");
        Check(status.Leases.Single().Ip == "192.0.2.10" && status.AllowedEvents.Contains("allowed.example"), "DNS/DHCP");
        Check(status.LocalIp == "192.0.2.1" && status.InternetTests.StartsWith("new ERRO: offline"), "Internet order/errors");
        Check(status.Shares.Length == 2 && status.Shares[1].WriteAccess == "Todos" && status.Shares[0].WriteAccess == "admin", "Samba visibility/permissions");
        string[] enumeratedShares = ["mONITOR", "hIDDEN", "HiddenNo", "HiddenZero", "HiddenFalse", "Data", "Archive", "Unreported"];
        Check(status.FilterBrowseableShares(enumeratedShares).SequenceEqual(["Data", "Archive", "Unreported"]),
            "SMB browser hides non-browseable shares with case-insensitive names");
        var browseableStatus = MonitorStatus.Parse(sample.Replace("\"browseable\": false", "\"browseable\": true"));
        Check(browseableStatus.FilterBrowseableShares(enumeratedShares).Contains("hIDDEN"),
            "SMB browser restores shares made browseable");
        const string minimal = """{"cpu_percent":0,"uptime_seconds":0,"memory":{"percent":0},"network":{}}""";
        var minimalStatus = MonitorStatus.Parse(minimal);
        Check(minimalStatus.Disks.Length == 0 && minimalStatus.Arrays.Length == 0 && minimalStatus.MemoryDetails.Contains("Indisponível"), "Optional fields");
        Check(minimalStatus.FilterBrowseableShares(["Monitor", "Data"]).SequenceEqual(["Data"]),
            "SMB shares preserved when agent metadata is absent");
        foreach (var invalid in new[] { "{", "[]", "{}", minimal.Replace("\"percent\":0", "\"percent\":\"bad\"") })
        {
            try
            {
                MonitorStatus.Parse(invalid);
                throw new InvalidOperationException("Invalid status accepted.");
            }
            catch (JsonException)
            {
            }
        }

        Console.WriteLine("PASS: invalid JSON/schema rejected");
        CheckLocalizationResources();
        CheckWindowsStartup();
        CheckClientSettings();
        CheckBackupTransfers();
        CheckSshSecurity();
        CheckWindow(status);
    }

    private static void CheckLocalizationResources()
    {
        var portuguese = (ResourceDictionary)Application.LoadComponent(
            new Uri("/MarcelinoClient;component/Localization/Strings.pt-BR.xaml", UriKind.Relative));
        var english = (ResourceDictionary)Application.LoadComponent(
            new Uri("/MarcelinoClient;component/Localization/Strings.en-US.xaml", UriKind.Relative));
        Check(portuguese.Keys.Cast<object>().ToHashSet().SetEquals(english.Keys.Cast<object>()),
            "Localization dictionaries contain matching keys");
    }

    private static void CheckWindowsStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Marcelino Startup Checks " + Guid.NewGuid().ToString("N"));
        var executable = Path.Combine(directory, "MarcelinoClient.exe");
        var registryPath = @"Software\MarcelinoClient\Checks\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(executable, []);
        try
        {
            var startup = new WindowsStartup(executable, registryPath);
            Check(!startup.IsEnabled, "Windows startup is opt-in by default");
            startup.SetEnabled(true);
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath))
            {
                Check(key?.GetValue(WindowsStartup.EntryName) as string == $"\"{executable}\" --tray",
                    "Windows startup quotes executable paths with spaces and starts in tray");
            }

            Check(startup.IsEnabled, "Windows startup enabled state read back");
            startup.SetEnabled(false);
            Check(!startup.IsEnabled, "Windows startup toggle removes autorun entry");
            startup.SetEnabled(false);
            Check(!startup.IsEnabled, "Disabling absent autorun entry is safe");
            try
            {
                WindowsStartup.CommandFor("relative.exe");
                throw new InvalidOperationException("Relative autorun path accepted.");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("PASS: relative Windows startup path rejected");
            }

            File.Delete(executable);
            try
            {
                startup.SetEnabled(true);
                throw new InvalidOperationException("Missing autorun executable accepted.");
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("PASS: missing Windows startup executable rejected");
            }
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckClientSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MarcelinoSettingsChecks-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new ClientSettingsStore(path);
        try
        {
            Check(store.Load().Theme == "light" && store.Load().Language == "pt-BR" && !File.Exists(path),
                "Missing settings use defaults without creating a file");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """{"server":{"ip":"nas-check","name":"NAS","selection":"NAS (nas-check)"},"username":"sample","sources":["C:\\Data",null,2,"c:\\data"],"theme":"dark","status_interval_seconds":30}""");
            var settings = store.Load();
            Check(settings.Server == "nas-check" && settings.Username == "sample" && settings.Sources.Length == 1 && settings.Theme == "dark",
                "Python settings loaded and invalid source entries ignored");
            settings.Domain = "WORKGROUP";
            settings.SshKeyPath = "C:\\Keys\\id_ed25519";
            settings.Destination = SavedBackupDestination.FromPath("nas-check", "\\\\nas-check\\Data\\Backups");
            store.Save(settings);
            var restored = store.Load();
            Check(restored.Server == settings.Server && restored.Username == settings.Username && restored.Domain == settings.Domain &&
                restored.SshKeyPath == settings.SshKeyPath && restored.Destination == settings.Destination && restored.Theme == settings.Theme &&
                restored.Language == settings.Language,
                "All requested preferences survive save/reload");
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
            {
                Check(json.RootElement.GetProperty("status_interval_seconds").GetInt32() == 30 &&
                    json.RootElement.GetProperty("server").GetProperty("name").GetString() == "NAS", "Unrelated Python settings preserved");
                Check(!json.RootElement.TryGetProperty("password", out _) && !json.RootElement.TryGetProperty("passphrase", out _),
                    "Settings contain no password or passphrase fields");
            }

            Check(restored.Destination!.ForServer("NAS-CHECK") == "\\\\NAS-CHECK\\Data\\Backups" && restored.Destination.ForServer("other-nas") is null,
                "Backup destination restored only for its own server");
            Check(new SavedBackupDestination("nas-check", "Data", "../other").ForServer("nas-check") is null,
                "Invalid saved destination traversal rejected");
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Atomic settings write cleans temporary files");
            File.WriteAllText(path, "{");
            try
            {
                store.Save(settings);
                throw new InvalidOperationException("Corrupt settings overwritten.");
            }
            catch (JsonException)
            {
                Check(File.ReadAllText(path) == "{", "Corrupt settings preserved instead of overwritten");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void CheckBackupTransfers()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarcelinoBackupChecks-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "remote");
        var restore = Path.Combine(root, "restored");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "original");
        File.SetLastWriteTimeUtc(Path.Combine(source, "nested", "data.txt"), DateTime.UtcNow.AddMinutes(-10));
        var transfer = new BackupTransfer();
        try
        {
            Check(BackupTransfer.GetRootName("\\\\nas-check\\Data") == "Data" && BackupTransfer.GetRootName("\\\\nas-check\\Data\\") == "Data",
                "Restore names UNC share roots correctly");
            var first = transfer.BackupAsync([source], destination, null, CancellationToken.None).GetAwaiter().GetResult();
            var remoteRoot = Path.Combine(destination, "source");
            var remoteFile = Path.Combine(remoteRoot, "nested", "data.txt");
            Check(first.Copied == 1 && first.Failed == 0 && File.ReadAllText(remoteFile) == "original", "Backup copies source folder structure");
            Check(Directory.Exists(Path.Combine(remoteRoot, "empty")), "Backup preserves empty directories");
            Check(File.GetLastWriteTimeUtc(remoteFile) == File.GetLastWriteTimeUtc(Path.Combine(source, "nested", "data.txt")), "Backup preserves modification timestamps");
            var repeated = transfer.BackupAsync([source], destination, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(repeated.Copied == 0 && repeated.Skipped == 1, "Backup skips up-to-date files");
            File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "changed contents");
            var changed = transfer.BackupAsync([source], destination, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(changed.Copied == 1 && File.ReadAllText(remoteFile) == "changed contents", "Backup replaces modified files");
            File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "newer source contents");
            using (var lockedDestination = new FileStream(remoteFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var blocked = transfer.BackupAsync([source], destination, null, CancellationToken.None).GetAwaiter().GetResult();
                Check(blocked.Failed == 1 && blocked.Copied == 0 && File.ReadAllText(remoteFile) == "changed contents",
                    "Failed overwrite preserves previous destination");
            }

            File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "changed contents");
            var restored = transfer.RestoreAsync(remoteRoot, restore, true, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(restored.Copied == 1 && File.ReadAllText(Path.Combine(restore, "source", "nested", "data.txt")) == "changed contents", "Restore recreates selected remote folder");
            var restoredAgain = transfer.RestoreAsync(remoteRoot, restore, true, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(restoredAgain.Skipped == 1 && restoredAgain.Copied == 0, "Restore skips current local files");
            var restoredFile = transfer.RestoreAsync(remoteFile, Path.Combine(root, "single"), false, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(restoredFile.Copied == 1 && File.Exists(Path.Combine(root, "single", "data.txt")), "Restore individual file");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                transfer.BackupAsync([source], destination, null, cancellation.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Cancelled backup succeeded.");
            }
            catch (OperationCanceledException)
            {
                Check(File.ReadAllText(remoteFile) == "changed contents", "Cancelled backup preserves existing destination");
            }

            using var afterScanCancellation = new CancellationTokenSource();
            var cancellationProgress = new InlineTransferProgress(update =>
            {
                if (update.Total > 0)
                {
                    afterScanCancellation.Cancel();
                }
            });
            try
            {
                transfer.BackupAsync([source], destination, cancellationProgress, afterScanCancellation.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Cancellation after scan ignored.");
            }
            catch (OperationCanceledException)
            {
                Check(File.ReadAllText(remoteFile) == "changed contents", "Cancellation after scan preserves destination");
            }

            try
            {
                transfer.BackupAsync([source], Path.Combine(source, "recursive"), null, CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("Recursive backup accepted.");
            }
            catch (IOException)
            {
                Console.WriteLine("PASS: backup rejects overlapping source/destination");
            }

            Check(!Directory.EnumerateFiles(destination, ".marcelino-*.tmp", SearchOption.AllDirectories).Any(), "Transfer temporary files cleaned up");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckSshSecurity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MarcelinoSshChecks-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "host-keys.json");
        var store = new SshHostKeyStore(path);
        var key = new byte[] { 1, 2, 3, 4 };
        try
        {
            Check(!store.Verify("nas-check", "ssh-ed25519", key, _ => false) && !File.Exists(path), "Unknown SSH key denied without persistence");
            Check(store.Verify("nas-check", "ssh-ed25519", key, host => host.Fingerprint.StartsWith("SHA256:")), "SSH key confirmation");
            var reloaded = new SshHostKeyStore(path);
            Check(reloaded.Verify("NAS-CHECK", "ssh-ed25519", key, _ => throw new InvalidOperationException("Known host asked for trust.")), "SSH key pin persisted");
            try
            {
                store.Verify("nas-check", "ssh-ed25519", [9, 8, 7], _ => throw new InvalidOperationException("Changed key asked for trust."));
                throw new InvalidOperationException("Changed host key accepted.");
            }
            catch (Renci.SshNet.Common.SshConnectionException)
            {
                Console.WriteLine("PASS: changed SSH key blocked");
            }

            File.WriteAllText(path, "{");
            try
            {
                store.Verify("other-host", "ssh-ed25519", key, _ => throw new InvalidOperationException("Corrupt trust store ignored."));
                throw new InvalidOperationException("Corrupt trust store accepted.");
            }
            catch (JsonException)
            {
                Console.WriteLine("PASS: corrupt SSH trust store fails closed");
            }

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                SshConnection.ConnectAsync("127.0.0.1", "unused", "unused", "", "", 80, 24, store,
                    _ => throw new InvalidOperationException("Cancelled connection asked for trust."), cancelled.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Cancelled SSH connection succeeded.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("PASS: SSH connection cancellation");
            }

            try
            {
                SshConnection.ConnectAsync("127.0.0.1", "unused", "unused", Path.Combine(directory, "missing-key"), "", 80, 24,
                    store, _ => false, CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("Missing SSH private key accepted.");
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("PASS: missing SSH key reported");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void CheckWindow(MonitorStatus status)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        CheckWindowPreferences();
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "MarcelinoWindowChecks-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(new ClientSettingsStore(Path.Combine(settingsDirectory, "settings.json")));
        try
        {
            window.Show();
            var passwordInput = (PasswordBox)window.FindName("PasswordInput");
            var revealedPassword = (TextBox)window.FindName("RevealedPasswordInput");
            var passwordVisibilityButton = (Button)window.FindName("PasswordVisibilityButton");
            Check(passwordInput.Visibility == Visibility.Visible && revealedPassword.Visibility == Visibility.Collapsed,
                "Login password is hidden by default");
            passwordInput.Password = "reveal-check";
            passwordVisibilityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(revealedPassword.Visibility == Visibility.Visible && passwordInput.Visibility == Visibility.Collapsed &&
                revealedPassword.Text == "reveal-check", "Login password eye reveals the entered password");
            revealedPassword.Text = "edited-check";
            Check(passwordInput.Password == "edited-check", "Visible password edits update login credentials");
            passwordVisibilityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(passwordInput.Visibility == Visibility.Visible && revealedPassword.Visibility == Visibility.Collapsed &&
                revealedPassword.Text.Length == 0 && passwordInput.Password == "edited-check",
                "Hiding password preserves credentials and clears plaintext field");
            var passwordContainer = (FrameworkElement)passwordVisibilityButton.Parent;
            var eyeBounds = passwordVisibilityButton.TransformToAncestor(passwordContainer)
                .TransformBounds(new Rect(passwordVisibilityButton.RenderSize));
            Check(eyeBounds.Right <= passwordContainer.ActualWidth &&
                passwordContainer.ActualWidth - eyeBounds.Right <= 2 &&
                eyeBounds.Left >= passwordInput.ActualWidth,
                "Password eye fits inside the input at the right without overlapping text");
            passwordInput.Clear();
            var loginStatusBar = (FrameworkElement)((FrameworkElement)window.FindName("StatusText")).Parent;
            ((FrameworkElement)window.FindName("LoginView")).Visibility = Visibility.Collapsed;
            var mainView = (FrameworkElement)window.FindName("MainView");
            mainView.Visibility = Visibility.Visible;
            mainView.DataContext = status;
            window.UpdateLayout();
            Check(loginStatusBar.Visibility == Visibility.Collapsed &&
                ((Grid)loginStatusBar.Parent).RowDefinitions[Grid.GetRow(loginStatusBar)].ActualHeight == 0,
                "Successful login hides status bar and reclaims its layout space");
            CheckWindowWorkArea(window, "Initial borderless window fits monitor work area");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            var trayField = typeof(MainWindow).GetField("_trayIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)trayField.GetValue(window);
            Check(tray is not null && tray.IconSource is not null, "System tray icon initialized with NAS artwork");
            var restoreMenu = tray!.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Restaurar"));
            Check(tray.ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Iniciar com o Windows") && item.IsCheckable),
                "Tray exposes the Windows startup toggle");
            typeof(MainWindow).GetMethod("StartInTray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null);
            Check(!window.IsVisible && trayField.GetValue(window) is not null, "Automatic startup hides window while retaining tray access");
            restoreMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(window.WindowStyle == WindowStyle.None && window.ResizeMode == ResizeMode.CanResize &&
                System.Windows.Shell.WindowChrome.GetWindowChrome(window).ResizeBorderThickness.Left > 0,
                "Custom title bar removes native caption and retains resize borders");
            var maximizeButton = (Button)window.FindName("MaximizeWindowButton");
            maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(window.WindowState == WindowState.Maximized && Equals(maximizeButton.Tag, "\uE923"),
                "Integrated maximize button maximizes window and switches to restore icon");
            CheckWindowWorkArea(window, "Maximized window does not cover the taskbar");
            maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(window.WindowState == WindowState.Normal && Equals(maximizeButton.Tag, "\uE922"),
                "Integrated restore button returns window to normal");
            ((Button)window.FindName("CloseWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!window.IsVisible && trayField.GetValue(window) is not null && ReferenceEquals(mainView.DataContext, status),
                "Window close hides to tray without clearing the active view");
            restoreMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "Tray menu restores hidden window");
            ((Button)window.FindName("MinimizeWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(!window.IsVisible, "Minimize hides window to system tray");
            restoreMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "Tray restores minimized window");
            var tabs = (TabControl)window.FindName("MainTabs");
            var themeSelector = (ComboBox)window.FindName("ThemeSelector");
            var hostInput = (TextBox)window.FindName("HostInput");
            Check(((PasswordBox)window.FindName("PasswordInput")).VerticalContentAlignment == VerticalAlignment.Center &&
                ((PasswordBox)window.FindName("SshPassphraseInput")).VerticalContentAlignment == VerticalAlignment.Center,
                "Password and SSH passphrase input vertically centered");
            Check(new Typeface("Segoe MDL2 Assets").TryGetGlyphTypeface(out var iconFont), "Native Windows icon font available");
            Check(tabs.Items.Cast<TabItem>().All(tab => tab.Tag is string { Length: > 0 } glyph &&
                iconFont.CharacterToGlyphMap.ContainsKey(char.ConvertToUtf32(glyph, 0))), "All tab icons exist in the Windows font");
            foreach (var name in new[] { "DisconnectButton", "ScanButton", "ConnectButton", "RemoteUpButton", "RefreshRemoteButton",
                "StartBackupButton", "RestoreBackupButton", "CancelTransferButton", "SelectSshKeyButton", "SshToggleButton", "SshPasteButton" })
            {
                var button = (Button)window.FindName(name);
                Check(button.Tag is string { Length: > 0 } glyph && iconFont.CharacterToGlyphMap.ContainsKey(char.ConvertToUtf32(glyph, 0)),
                    "Action icon available: " + name);
            }

            var lightBackground = ((SolidColorBrush)window.Background).Color;
            foreach (var themeIndex in new[] { 1, 0 })
            {
                themeSelector.SelectedIndex = themeIndex;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var background = ((SolidColorBrush)window.Background).Color;
                Check(themeIndex == 1 ? background != lightBackground : background == lightBackground, "Theme switch " + themeIndex);
                Check(((SolidColorBrush)hostInput.Background).Color == ((SolidColorBrush)window.FindResource("InputBrush")).Color,
                    "Input theme " + themeIndex);
                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                var selectedTab = (TabItem)tabs.SelectedItem;
                var tabGlyph = (TextBlock)selectedTab.Template.FindName("TabGlyph", selectedTab);
                Check(((SolidColorBrush)tabGlyph.Foreground).Color == ((SolidColorBrush)window.FindResource("SelectedTextBrush")).Color,
                    "Selected tab icon contrast " + themeIndex);
                var cpuText = Descendants(window).OfType<TextBlock>().Single(text => text.Text == status.Cpu);
                Check(((SolidColorBrush)cpuText.Foreground).Color == ((SolidColorBrush)window.FindResource("TextBrush")).Color,
                    "System metric foreground " + themeIndex);
                var hardwareText = Descendants(window).OfType<TextBlock>().Single(text => text.Text == "Sample CPU");
                Check(((SolidColorBrush)hardwareText.Foreground).Color == ((SolidColorBrush)window.FindResource("TextBrush")).Color,
                    "System hardware foreground " + themeIndex);
                themeSelector.IsDropDownOpen = true;
                window.UpdateLayout();
                themeSelector.IsDropDownOpen = false;
                foreach (TabItem tab in tabs.Items)
                {
                    tabs.SelectedItem = tab;
                    window.UpdateLayout();
                    foreach (var grid in Descendants(window).OfType<DataGrid>())
                    {
                        Check(((SolidColorBrush)grid.Background).Color == ((SolidColorBrush)window.FindResource("InputBrush")).Color,
                            "Grid theme " + themeIndex);
                    }
                }
            }

            foreach (TabItem tab in tabs.Items)
            {
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            tabs.SelectedIndex = 0;
            window.UpdateLayout();
            Check(Descendants(window).OfType<TextBlock>().Any(text => text.Text == status.Cpu), "WPF metric binding");
            Check(bindingErrors.Errors.Count == 0, "WPF tab bindings: " + string.Join("\n", bindingErrors.Errors));
            Check(tabs.Items.Count == 8, "Eight tabs loaded at minimum window size");
            tabs.SelectedIndex = 6;
            var remoteEntries = (DataGrid)window.FindName("RemoteEntriesGrid");
            remoteEntries.ItemsSource = Enumerable.Range(1, 100).Select(index => new { Name = $"Pasta {index:00}", Kind = "Pasta" }).ToArray();
            foreach (var dimensions in new[] { new Size(1366, 720), new Size(window.MinWidth, window.MinHeight) })
            {
                window.Width = dimensions.Width;
                window.Height = dimensions.Height;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var header = (FrameworkElement)window.FindName("WindowHeader");
                foreach (var name in new[] { "MinimizeWindowButton", "MaximizeWindowButton", "CloseWindowButton" })
                {
                    var captionButton = (Button)window.FindName(name);
                    var bounds = captionButton.TransformToAncestor(header).TransformBounds(new Rect(captionButton.RenderSize));
                    Check(bounds.Left >= 0 && bounds.Right <= header.ActualWidth && captionButton.ActualWidth == captionButton.Width,
                        $"Caption control fits compact header: {name} at {dimensions.Width:0}");
                }
                var backupScroller = (ScrollViewer)window.FindName("BackupScrollViewer");
                var destinationButton = (Button)window.FindName("SetDestinationButton");
                var destinationBounds = destinationButton.TransformToAncestor(backupScroller)
                    .TransformBounds(new Rect(destinationButton.RenderSize));
                Check(header.ActualHeight <= 40, $"Compact header at {dimensions.Width:0}x{dimensions.Height:0}");
                Check(remoteEntries.ActualHeight >= 120 && remoteEntries.ActualWidth >= 200,
                    $"Backup destination list retains usable dimensions at {dimensions.Width:0}x{dimensions.Height:0}");
                Check(remoteEntries.ActualHeight < 400,
                    $"Long backup directory listing scrolls without stretching the page at {dimensions.Width:0}x{dimensions.Height:0}");
                Check(destinationBounds.Top >= 0 && destinationBounds.Bottom <= backupScroller.ActualHeight,
                    $"Backup destination action visible at {dimensions.Width:0}x{dimensions.Height:0}");
                if (dimensions.Width == 1366)
                {
                    var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
                        (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                    }

                    screenshot.Render(visual);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
                    var screenshotPath = Path.Combine(Path.GetTempPath(), "MarcelinoClient-backup-hd.png");
                    using var output = File.Create(screenshotPath);
                    encoder.Save(output);
                    Console.WriteLine("Backup layout screenshot: " + screenshotPath);
                }
            }

            remoteEntries.ItemsSource = null;
            tabs.SelectedIndex = 7;
            window.UpdateLayout();
            var terminal = (SshTerminalView)window.FindName("SshTerminal");
            Check(terminal.Rows == 25 && terminal.ScreenText.Split('\n').Length == 25 && Math.Abs(terminal.ActualHeight - 441) < 1,
                "SSH terminal has a minimum of 25 visible rows");
            var terminalScroller = (ScrollViewer)window.FindName("SshTerminalScrollViewer");
            Check(terminalScroller.ScrollableHeight > 0, "SSH terminal accessible by scrolling in a small window");
            var initialColumns = terminal.Columns;
            var resizedRows = 0;
            terminal.TerminalSizeChanged += (_, rows) => resizedRows = rows;
            ((FrameworkElement)window.FindName("SshAuthenticationPanel")).Visibility = Visibility.Collapsed;
            window.Width += 100;
            terminalScroller.Height = 900;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(terminal.Columns > initialColumns && resizedRows == terminal.Rows && terminal.Rows > 25 && terminal.ActualHeight > 441,
                $"SSH terminal grows beyond 25 rows and reports the new size (height={terminal.ActualHeight:0}, rows={terminal.Rows}, columns={terminal.Columns}, viewport={terminalScroller.ViewportHeight:0})");
            ((FrameworkElement)window.FindName("SshAuthenticationPanel")).Visibility = Visibility.Visible;
            terminalScroller.ClearValue(FrameworkElement.HeightProperty);
            window.Height = window.MinHeight;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(terminal.Rows == 25 && terminal.ActualHeight >= 441 && terminalScroller.ScrollableHeight > 0,
                "SSH terminal never shrinks below 25 rows");
            terminal.Feed(Encoding.UTF8.GetBytes("\u001b[2J\u001b[Hhello\rworld\r\n\u001b[31mred\u001b[0m"));
            Check(terminal.ScreenText.StartsWith("world") && terminal.ScreenText.Contains("red") && !terminal.ScreenText.Contains('\u001b'),
                "SSH terminal ANSI/cursor handling");
            terminal.Reset();
            Check(terminal.ScreenText.Split('\n').Length == 25, "SSH terminal reset preserves 25 rows");
            var utf8 = Encoding.UTF8.GetBytes("ação");
            foreach (var value in utf8)
            {
                terminal.Feed([value]);
            }

            Check(terminal.ScreenText.StartsWith("ação"), "SSH terminal split UTF-8");
            byte[]? terminalReply = null;
            terminal.DataToSend += data => terminalReply = data;
            terminal.Feed(Encoding.UTF8.GetBytes("\u001b[6n"));
            Check(terminalReply is not null && Encoding.ASCII.GetString(terminalReply).EndsWith("R"), "SSH terminal cursor report");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)terminal.ActualWidth,
                (int)terminal.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var terminalVisual = new DrawingVisual();
            using (var drawing = terminalVisual.RenderOpen())
            {
                drawing.DrawRectangle(new VisualBrush(terminal), null,
                    new Rect(0, 0, terminal.ActualWidth, terminal.ActualHeight));
            }

            bitmap.Render(terminalVisual);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Check(pixels.Where((_, index) => index % 4 != 3).Any(value => value > 0), "SSH terminal renders nonblank output");
            terminal.Reset();
            terminal.Feed(Encoding.UTF8.GetBytes("\u001b[2J\u001b[H" + new string('\u28ff', 40) +
                "\u001b[42m \u001b[0m\r\n" + new string('\u2500', 40) +
                "\u001b[42m \u001b[0m\r\n\u001b[1m" + string.Concat(Enumerable.Repeat("a\u0301", 40)) +
                "\u001b[42m \u001b[0m"));
            var terminalDrawing = new DrawingGroup();
            using (var drawing = terminalDrawing.Open())
            {
                typeof(SshTerminalView).GetMethod("OnRender",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(terminal, [drawing]);
            }

            var markerCells = terminalDrawing.Children.OfType<GeometryDrawing>()
                .Where(entry => entry.Brush is SolidColorBrush brush && brush.Color.G > 0 &&
                    brush.Color.R == 0 && brush.Color.B == 0).ToArray();
            var cellWidth = new FormattedText("M", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 13, Brushes.White, VisualTreeHelper.GetDpi(terminal).PixelsPerDip)
                .WidthIncludingTrailingWhitespace;
            Check(markerCells.Length == 3 && markerCells.All(entry =>
                Math.Abs(entry.Bounds.X - (8 + 40 * cellWidth)) < 0.01 &&
                Math.Abs(entry.Bounds.Width - cellWidth) < 0.01),
                "SSH terminal aligns Braille, box drawing and bold combining text to fixed cells");
            var authenticationPanel = (FrameworkElement)window.FindName("SshAuthenticationPanel");
            var setSshControls = typeof(MainWindow).GetMethod("SetSshControls",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            setSshControls.Invoke(window, [true, false]);
            Check(authenticationPanel.Visibility == Visibility.Visible && !((TextBox)window.FindName("SshKeyPathInput")).IsEnabled,
                "SSH authentication visible but disabled while connecting");
            setSshControls.Invoke(window, [false, true]);
            window.UpdateLayout();
            Check(authenticationPanel.Visibility == Visibility.Collapsed && ((Button)window.FindName("SshPasteButton")).IsVisible,
                "SSH authentication hidden after connection, actions remain visible");
            var sshSession = new CancellationTokenSource();
            typeof(MainWindow).GetField("_sshSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(window, sshSession);
            terminal.IsConnected = true;
            ((Button)window.FindName("SshToggleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(string.IsNullOrWhiteSpace(terminal.ScreenText) && !terminal.IsConnected,
                "SSH disconnect button clears terminal screen and connection state");
            Check(authenticationPanel.Visibility == Visibility.Visible && ((TextBox)window.FindName("SshKeyPathInput")).IsEnabled,
                "SSH authentication restored after disconnection");
            terminal.IsConnected = true;
            terminal.Feed(Encoding.UTF8.GetBytes("sensitive session output"));
            ((PasswordBox)window.FindName("SshPassphraseInput")).Password = "temporary-check";
            using var backupCancellation = new CancellationTokenSource();
            var transferCancellationField = typeof(MainWindow).GetField("_transferCancellation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            transferCancellationField.SetValue(window, backupCancellation);
            ((TextBox)window.FindName("BackupLog")).Text = "Previous backup output";
            passwordInput.Password = "logout-check";
            passwordVisibilityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("DisconnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(passwordInput.Password.Length == 0 && revealedPassword.Text.Length == 0 &&
                revealedPassword.Visibility == Visibility.Collapsed && passwordInput.Visibility == Visibility.Visible,
                "Logout clears both password fields and restores hidden mode");
            Check(loginStatusBar.Visibility == Visibility.Visible && loginStatusBar.ActualHeight > 0,
                "Server logout restores login status bar");
            Check(backupCancellation.IsCancellationRequested && ((TextBox)window.FindName("BackupLog")).Text.Length == 0 &&
                ((DataGrid)window.FindName("RemoteEntriesGrid")).Items.Count == 0,
                "Server logout cancels backup and clears remote browser/log");
            transferCancellationField.SetValue(window, null);
            Check(string.IsNullOrWhiteSpace(terminal.ScreenText) && !terminal.IsConnected,
                "Server logout clears terminal screen and connection state");
            Check(((PasswordBox)window.FindName("SshPassphraseInput")).Password.Length == 0 && authenticationPanel.Visibility == Visibility.Visible,
                "Server logout clears SSH passphrase and restores authentication fields");
            terminal.Feed(Encoding.UTF8.GetBytes("tray disconnect output"));
            tray.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Desconectar"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(window.IsVisible && string.IsNullOrWhiteSpace(terminal.ScreenText) &&
                ((FrameworkElement)window.FindName("LoginView")).Visibility == Visibility.Visible,
                "Tray disconnect clears session and restores login window");
            tray.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Sair"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!window.IsVisible && trayField.GetValue(window) is null, "Tray exit closes window and disposes icon");
        }
        finally
        {
            if (window.IsLoaded)
            {
                typeof(MainWindow).GetMethod("ExitApplication", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, recursive: true);
            }
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
            application.Shutdown();
        }
    }

    private static void CheckWindowPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MarcelinoPreferencesWindowChecks-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new ClientSettingsStore(path);
        store.Save(new ClientSettings
        {
            Server = "nas-check", Username = "saved-user", Domain = "WORKGROUP", Sources = ["C:\\BackupSource"],
            Destination = new SavedBackupDestination("nas-check", "Data", "Backups"),
            SshKeyPath = "C:\\Keys\\id_ed25519", Theme = "dark", Language = "pt-BR"
        });
        var window = new MainWindow(store);
        MainWindow? reopened = null;
        try
        {
            Check(((TextBox)window.FindName("HostInput")).Text == "nas-check" &&
                ((TextBox)window.FindName("UsernameInput")).Text == "saved-user" &&
                ((TextBox)window.FindName("DomainInput")).Text == "WORKGROUP", "WPF restores server/user/domain on startup");
            Check(((ComboBox)window.FindName("ThemeSelector")).SelectedIndex == 1 &&
                ((TextBox)window.FindName("SshKeyPathInput")).Text == "C:\\Keys\\id_ed25519" &&
                ((ListBox)window.FindName("BackupSourcesList")).Items.Contains("C:\\BackupSource"), "WPF restores theme, SSH key and backup sources");
            var languageSelector = (ComboBox)window.FindName("LanguageSelector");
            languageSelector.SelectedValue = "en-US";
            Check(Equals(languageSelector.SelectedValue, "en-US") &&
                languageSelector.Text == "English (United States)" &&
                Equals(((TextBlock)window.FindName("LoginTitle")).Text, "SMB server login") &&
                Equals(((DataGrid)window.FindName("FileSystemsGrid")).Columns[0].Header, "Mount point") &&
                Equals(window.FindResource("SmbLoginTitle"), "SMB server login") && store.Load().Language == "en-US",
                "Language selector updates resources and persists immediately");
            const string englishMonitorSample = """{"cpu_percent":0,"uptime_seconds":0,"memory":{"percent":0},"network":{},"samba_shares":[{"name":"A","read_only":"yes"},{"name":"B","read_only":"no"}]}""";
            var englishStatus = Task.Run(() => MonitorStatus.Parse(englishMonitorSample)).GetAwaiter().GetResult();
            Check(englishStatus.ShareStatus == "2 share(s) configured in Samba." &&
                englishStatus.Shares[0].ReadOnly == "Yes" && englishStatus.Shares[1].WriteAccess == "Everyone",
                "Monitor fields use selected language during background parsing");
            var hostField = typeof(MainWindow).GetField("_connectedHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            hostField.SetValue(window, new SmbHost("nas-check", "NAS"));
            typeof(MainWindow).GetMethod("SetSshAuthentication", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, ["saved-user", "check-password"]);
            var localizationType = typeof(MainWindow).Assembly.GetType("MarcelinoClient.Localization")!;
            var copiedMessage = localizationType.GetMethod("Message", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, ["CopiedFile", new object?[] { "C:\\BackupSource\\file.txt" }]);
            typeof(MainWindow).GetMethod("AppendBackupLog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [copiedMessage]);
            Check(((TextBlock)window.FindName("SshStatusText")).Text.StartsWith("Ready to connect to", StringComparison.Ordinal) &&
                ((TextBox)window.FindName("BackupLog")).Text.Contains("Copied:"),
                "SSH status and backup log use selected language when first rendered");
            languageSelector.SelectedValue = "pt-BR";
            Check(Equals(((DataGrid)window.FindName("FileSystemsGrid")).Columns[0].Header, "Montagem"),
                "DataGrid column headers update when language changes");
            Check(((TextBlock)window.FindName("SshStatusText")).Text.StartsWith("Pronto para conectar a", StringComparison.Ordinal) &&
                ((TextBox)window.FindName("BackupLog")).Text.Contains("Copiado:"),
                "SSH status and existing backup log update when language changes");
            languageSelector.SelectedValue = "en-US";
            typeof(MainWindow).GetMethod("RestoreBackupDestination", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null);
            Check(((TextBlock)window.FindName("DestinationText")).Text.Contains("\\\\nas-check\\Data\\Backups"),
                "WPF restores destination for the matching connected server");
            hostField.SetValue(window, null);
            ((PasswordBox)window.FindName("PasswordInput")).Password = "not-for-disk";
            ((PasswordBox)window.FindName("SshPassphraseInput")).Password = "not-for-disk-either";
            ((Button)window.FindName("DisconnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.Load().Destination?.ForServer("nas-check") == "\\\\nas-check\\Data\\Backups", "Logout retains saved backup destination");
            ((TextBox)window.FindName("HostInput")).Text = "new-nas";
            ((TextBox)window.FindName("UsernameInput")).Text = "new-user";
            ((TextBox)window.FindName("SshKeyPathInput")).Text = "C:\\Keys\\new-key";
            ((ListBox)window.FindName("BackupSourcesList")).Items.Add("C:\\AnotherSource");
            ((ComboBox)window.FindName("ThemeSelector")).SelectedIndex = 0;
            Check(store.Load().Theme == "light", "Theme change persisted immediately");
            window.Close();
            reopened = new MainWindow(store);
            Check(((TextBox)reopened.FindName("HostInput")).Text == "new-nas" &&
                ((TextBox)reopened.FindName("UsernameInput")).Text == "new-user" &&
                ((TextBox)reopened.FindName("SshKeyPathInput")).Text == "C:\\Keys\\new-key" &&
                ((ListBox)reopened.FindName("BackupSourcesList")).Items.Contains("C:\\AnotherSource") &&
                ((ComboBox)reopened.FindName("ThemeSelector")).SelectedIndex == 0 &&
                Equals(((ComboBox)reopened.FindName("LanguageSelector")).SelectedValue, "en-US") &&
                Equals(reopened.FindResource("SmbLoginTitle"), "SMB server login"),
                "Preferences restored after closing and reopening WPF window");
            Check(((PasswordBox)reopened.FindName("PasswordInput")).Password.Length == 0 &&
                ((PasswordBox)reopened.FindName("SshPassphraseInput")).Password.Length == 0 &&
                !File.ReadAllText(path).Contains("not-for-disk"), "Passwords/passphrases neither persisted nor restored");
        }
        finally
        {
            window.Close();
            reopened?.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckWindowWorkArea(Window window, string label)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var monitor = new TestMonitorInfo { Size = Marshal.SizeOf<TestMonitorInfo>() };
        Check(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor) && GetWindowRect(handle, out _), "Native monitor bounds available");
        GetWindowRect(handle, out var bounds);
        Check(bounds.Left >= monitor.Work.Left && bounds.Top >= monitor.Work.Top &&
            bounds.Right <= monitor.Work.Right && bounds.Bottom <= monitor.Work.Bottom,
            $"{label} (window={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; work={monitor.Work.Left},{monitor.Work.Top},{monitor.Work.Right},{monitor.Work.Bottom})");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestMonitorInfo
    {
        public int Size;
        public TestRect Monitor;
        public TestRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref TestMonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out TestRect rectangle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }

        Console.WriteLine($"PASS: {label}");
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) => WriteLine(message);
        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Errors.Add(message);
            }
        }
    }

    private sealed class InlineTransferProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }
}