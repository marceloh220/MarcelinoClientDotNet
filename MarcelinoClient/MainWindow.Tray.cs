using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace MarcelinoClient;

public partial class MainWindow
{
    private TaskbarIcon? _trayIcon;
    private bool _exitRequested;
    private WindowState _restoreWindowState = WindowState.Normal;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        InitializeWindowWorkArea();
        try
        {
            var restore = new MenuItem { Header = Localization.Get("TrayRestore") };
            restore.Click += (_, _) => RestoreFromTray();
            var disconnect = new MenuItem { Header = Localization.Get("Disconnect") };
            disconnect.Click += (_, _) =>
            {
                DisconnectButton_Click(this, new RoutedEventArgs());
                RestoreFromTray();
            };
            var exit = new MenuItem { Header = Localization.Get("TrayExit") };
            exit.Click += (_, _) => ExitApplication();
            var startup = new MenuItem { Header = Localization.Get("TrayStartup"), IsCheckable = true };
            startup.Click += (_, _) =>
            {
                try
                {
                    WindowsStartup.ForCurrentApplication().SetEnabled(startup.IsChecked);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
                {
                    startup.IsChecked = false;
                    RestoreFromTray();
                    SetStatus(Localization.Get("TrayStartupFailed", error.Message));
                }
            };
            var menu = new ContextMenu();
            menu.Items.Add(restore);
            menu.Items.Add(disconnect);
            menu.Items.Add(new Separator());
            menu.Items.Add(startup);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);
            menu.Opened += (_, _) =>
            {
                restore.Header = Localization.Get("TrayRestore");
                disconnect.Header = Localization.Get("Disconnect");
                exit.Header = Localization.Get("TrayExit");
                startup.Header = Localization.Get("TrayStartup");
                disconnect.IsEnabled = _connectedHost is not null;
                try
                {
                    startup.IsChecked = WindowsStartup.ForCurrentApplication().IsEnabled;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
                {
                    startup.IsEnabled = false;
                }
            };

            _trayIcon = new TaskbarIcon
            {
                IconSource = Icon,
                ToolTipText = "Marcelino Client",
                ContextMenu = menu,
                Visibility = Visibility.Visible
            };
            _trayIcon.TrayMouseDoubleClick += (_, _) => RestoreFromTray();
            if (Application.Current is { } application)
            {
                application.SessionEnding += Application_SessionEnding;
            }
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            DisposeTrayIcon();
            SetStatus(Localization.Get("TrayUnavailable", error.Message));
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_exitRequested && _trayIcon is not null && !Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateCaptionButtons();
        if (WindowState == WindowState.Minimized)
        {
            if (_trayIcon is not null && !_exitRequested)
            {
                HideToTray();
            }
        }
        else
        {
            _restoreWindowState = WindowState;
        }
    }

    private void HideToTray()
    {
        SaveClientSettings();
        Hide();
    }

    internal void StartInTray()
    {
        if (_trayIcon is not null)
        {
            HideToTray();
        }
    }

    internal void RestoreFromTray()
    {
        if (_exitRequested)
        {
            return;
        }

        Show();
        WindowState = _restoreWindowState;
        Activate();
        Focus();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e) => _exitRequested = true;

    private void DisposeTrayIcon()
    {
        if (Application.Current is { } application)
        {
            application.SessionEnding -= Application_SessionEnding;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

}