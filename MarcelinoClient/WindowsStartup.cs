using System.IO;
using Microsoft.Win32;

namespace MarcelinoClient;

public sealed class WindowsStartup(string executablePath,
    string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    public const string EntryName = "MarcelinoClient";

    public static WindowsStartup ForCurrentApplication() => new(Environment.ProcessPath
        ?? throw new InvalidOperationException(Localization.Get("ExecutableUnavailable")));

    public static string CommandFor(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.IndexOfAny(['"', '\r', '\n']) >= 0 ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(Localization.Get("StartupRequiresAbsoluteExe"));
        }

        return $"\"{executablePath}\" --tray";
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return string.Equals(key?.GetValue(EntryName) as string, CommandFor(executablePath), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(Localization.Get("StartupExecutableMissing"), executablePath);
            }

            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
            key.SetValue(EntryName, CommandFor(executablePath), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }
}