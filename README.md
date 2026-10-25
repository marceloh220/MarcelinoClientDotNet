# Marcelino Client (.NET)

[English](README.md) | [Português (Brasil)](README.pt-BR.md)

Standalone Windows desktop client project using .NET and WPF for Marcelino Client application. This is only the client part, for server part and full functionality see [BackupLAN](https://github.com/marceloh220/BackupLAN). The python client version have the sames features and functionality as this .NET client, ensuring consistency across platforms.

## Requirements

- .NET 10 SDK
- Windows with the .NET 10 WPF targeting pack

## Build and run

From this directory:

```powershell
dotnet build .\MarcelinoClient.sln
dotnet run --project .\MarcelinoClient\MarcelinoClient.csproj
```

## Installer and Windows startup

Build a self-contained Windows x64 installer (the target PC does not need .NET installed):

```powershell
.\Installer\build-installer.ps1 -DownloadCompiler
```

The script downloads the pinned portable `Tools.InnoSetup` 6.7.3 NuGet package, checks its SHA-512 against the NuGet catalog, publishes the client and creates `artifacts\installer\MarcelinoClient-Setup-1.0.0-win-x64.exe`. Alternatively, use an installed Inno Setup compiler with `-IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'`; set `-Version 1.0.1` for a different release. If PowerShell blocks scripts, allow the build for the current process only (`Set-ExecutionPolicy -Scope Process Bypass`), not for the entire machine. Inno Setup's compiler displays its license terms; 6.7.3 is marked non-commercial use only, so obtain the appropriate compiler license for commercial use/distribution.

The installer runs without elevation and installs under `%LOCALAPPDATA%\Programs\MarcelinoClient`, with a Start menu shortcut and optional desktop shortcut. The installer is in Brazilian Portuguese; its optional **Iniciar com o Windows (na bandeja)** task (start with Windows in the system tray) is initially unchecked. It registers a quoted executable path with `--tray` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, for the current user's Windows sign-in (not before login). The same **Start with Windows** option can be changed later in the tray menu when the client interface is in English. Startup does not save passwords, log into the NAS or initiate transfers. Windows Startup Apps/Task Manager can also disable autorun.

Only one instance is allowed per Windows session. A normal launch restores an existing instance; a duplicate `--tray` launch exits quietly. Uninstallation removes shortcuts and the autorun entry but preserves `%APPDATA%\marcelinoSrv` preferences and trusted SSH fingerprints. Exit the client from the tray before upgrading/uninstalling. The generated installer is not code-signed; production releases should be signed before distribution. The build/checks do not automatically install or uninstall the application.

## Porting scope

The WPF client includes SMB discovery/authentication and separate tabs for System, Disks, RAID, Internet, DNS/DHCP, Shares, Backup, and Terminal, including incremental backup/restore and an interactive SSH shell.

## Interface language

Use the **Language** selector in the application header (**Idioma** in Portuguese) to switch between Brazilian Portuguese (`pt-BR`) and US English (`en-US`). The options appear in their native language names. Brazilian Portuguese is the default, including when the saved language is missing or unsupported.

The change takes effect immediately, without restarting the application or reconnecting to the NAS. Interface labels, table headers, application-generated SSH status and backup log messages use the selected language. Text received from the server, such as SSH terminal output, is not translated. The installer remains in Brazilian Portuguese regardless of the client language.

The selection is saved immediately in the `language` field of `%APPDATA%\marcelinoSrv\settings.json` and restored on the next launch. Translation resources are [Brazilian Portuguese](MarcelinoClient/Localization/Strings.pt-BR.xaml) and [US English](MarcelinoClient/Localization/Strings.en-US.xaml); both dictionaries must contain the same resource keys.

## Backup and restore

The Backup tab browses the authenticated server's visible SMB shares and subfolders. Double-click a directory to open it, use the up button to return, and select a directory or open it before choosing **Use as destination**. Add local source folders, then choose **Start backup** and confirm the destination. Each source is copied into a subfolder with its original name, including empty directories. Sources with duplicate folder names and overlapping source/destination paths are rejected.

Select a remote file/folder (or leave a directory open), then choose **Restore to PC...**, select a local folder and confirm the final path. Directories are restored into a subfolder with the selected remote folder's name. The incremental restore checkbox is enabled by default.

Incremental comparison matches the Python client: equal file size and a destination modification time no earlier than source time minus two seconds. This is not a checksum comparison. Transfers preserve modification/access timestamps, use up to four workers, and never delete destination files absent from the source. Symbolic links/reparse points are not followed; skips/errors are reported in the log.

Progress and errors appear in the tab. **Cancel** requests cancellation; SMB operations already blocked in Windows may take time to return. Files are copied to temporary siblings and renamed into place only after a complete copy, so cancellation or a failed copy does not truncate the previous destination. Completed files remain after cancellation. Disconnecting or closing cancels transfers and ignores callbacks from the previous session. Backup selections are saved locally along with the other preferences.

## System tray

The native title bar is replaced by the application header, with minimize, maximize/restore and close controls on the right. Drag the non-interactive header area to move the window; double-click it to maximize/restore. Resize remains available at the window edges. Close still sends the app to the tray; use **Exit** to exit. The executable, window and tray share the same multi-resolution NAS icon, also used by application shortcuts.

The application creates a NAS icon in the Windows notification area. Minimizing or closing the window hides it to the tray without ending the authenticated session, monitoring, SSH or active transfers. Double-click the icon or choose **Restore** to show the window again. **Disconnect**, either in the application or the tray menu, ends the server session, closes SSH, clears session data and shows the login screen. Choose **Exit** to close the application, cancel transfers, close SSH/SMB and remove the icon. If tray initialization fails, the normal close behavior remains available. Windows may place the icon in the notification area's overflow menu.

## Preferences

Preferences are stored in `%APPDATA%\marcelinoSrv\settings.json`, using the Python client's field names. The client restores server, username/domain, local backup folders, backup destination, SSH private-key path, light/dark theme, and [interface language](#interface-language). Passwords, SSH passphrases and private-key contents are never included in preferences.

Theme and selection changes are saved immediately. Manually edited server/user/domain/key-path fields are saved when focus leaves the field, before login/logout, and when closing the window. Logout clears the active session but retains preferences. The backup destination is restored after authentication only when its saved server matches the connected host; paths for a different server are not reused. No automatic connection or transfer starts on application launch.

Writes use a temporary sibling file and an atomic rename, preserving unrelated JSON fields used by the Python client. Missing preferences use defaults. An invalid or unreadable preferences file is preserved and an error is shown instead of overwriting it; correct or rename that file and restart to re-enable preference saving.

## NAS monitoring

After SMB authentication, the client reads `\\server\Monitor\.marcelinoSrv.json` using the authenticated Windows SMB session. The user must have read access to the hidden `Monitor` share and the NAS agent must publish the same JSON format used by the Python client. No NAS-side changes are required.

The monitoring tabs display CPU, memory, uptime, network byte counters, hardware, filesystems, physical disks/SMART, RAID, Internet speed-test history, DNS/DHCP events and leases, and Samba permissions. `Monitor` stays hidden from the share browser. Once agent metadata is read, shares configured with `browseable = no` (also `false` or `0`) are hidden from both the browser and permissions table. Visibility is reapplied on each successful refresh, without dropping SMB shares whose configuration is not published by the agent.

The first read starts after login. Automatic refresh defaults to five seconds; the System tab provides interval selection and manual refresh. Reads run off the UI thread without overlapping. Disconnecting or closing stops polling, clears the metrics, and discards late results. If the status file cannot be accessed or contains invalid JSON, the client logs out automatically, stops polling, closes SSH, clears session data and returns to the login screen. The login status message reports the reason for the disconnection; connect again after resolving the cause.

The NAS-side Python services and setup scripts remain outside this project.

## SSH terminal

After SMB login, the Terminal tab connects to the same server and username over SSH on port 22. Leave the private-key field empty to use the login password, or select a private key and enter its optional passphrase. SSH.NET handles authentication and encryption; VtNetCore interprets the interactive `xterm-256color` shell, including ANSI colors, cursor movement, and terminal resizing.

On first connection, verify the displayed SHA-256 host fingerprint with the server administrator before accepting. Accepted keys are pinned in `%APPDATA%\marcelinoSrv\ssh-host-keys.json`, independently of OpenSSH's `known_hosts`. Changed keys are rejected, and an unreadable or corrupt trust file prevents connection. Only remove an outdated pin after independently verifying a legitimate server key change. Passwords and passphrases are not saved to disk.

The SSH button connects, cancels a pending connection, or disconnects an active shell. Disconnecting from SMB or closing the application also closes SSH and clears the retained login credentials. The embedded terminal accepts normal keyboard input and control keys; Ctrl+Shift+C copies the visible screen and Ctrl+Shift+V or the Paste button pastes text. Multiline pastes require confirmation. Mouse reporting and scrollback navigation are not implemented in this initial terminal UI.

## Local checks

Run the JSON/parser, local backup/restore, SSH trust/cancellation, terminal-emulation, localization and WPF tab binding checks on Windows:

```powershell
dotnet run --project .\Checks\MarcelinoClient.Checks.csproj
```

Localization checks cover matching translation keys, immediate language changes and persistence, translated table headers, and SSH status/backup log updates.

The checks transfer files only inside temporary local directories and briefly open a WPF window with synthetic data, then close it. They do not connect to a NAS. Real SMB permissions, backup/restore against Samba, SSH authentication/interactive shell behavior, and NAS agent output must still be verified in the target environment.