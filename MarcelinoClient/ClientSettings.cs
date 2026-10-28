using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MarcelinoClient;

public sealed record SavedBackupDestination(string Server, string Share, string Path)
{
    public string? ForServer(string server)
    {
        if (!string.Equals(Server, server, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Share) || Share is "." or ".." || Share.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            return null;
        }

        var segments = Path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            return null;
        }

        return $"\\\\{server}\\{Share}" + (segments.Length > 0 ? "\\" + string.Join('\\', segments) : "");
    }

    public static SavedBackupDestination FromPath(string server, string uncPath)
    {
        var prefix = $"\\\\{server}\\";
        if (!uncPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(Localization.Get("DestinationWrongServer"));
        }

        var parts = uncPath[prefix.Length..].Split('\\', 2);
        var destination = new SavedBackupDestination(server, parts[0], parts.Length > 1 ? parts[1] : "");
        return destination.ForServer(server) is not null ? destination : throw new ArgumentException(Localization.Get("InvalidBackupDestination"));
    }
}

public sealed class ClientSettings
{
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public string[] Sources { get; set; } = [];
    public SavedBackupDestination? Destination { get; set; }
    public string SshKeyPath { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public int SshRemotePort { get; set; }
    public int SshLocalPort { get; set; }
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "pt-BR";
}

public sealed class ClientSettingsStore(string path)
{
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "marcelinoSrv", "settings.json");

    public ClientSettings Load()
    {
        var root = ReadRoot();
        var server = root["server"];
        var destination = root["destination"] as JsonObject;
        return new ClientSettings
        {
            Server = server is JsonObject savedServer ? Text(savedServer["ip"]) : Text(server),
            Username = Text(root["username"]),
            Domain = Text(root["domain"]),
            Sources = root["sources"] is JsonArray sources
                ? sources.Select(Text).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [],
            Destination = destination is null ? null : new SavedBackupDestination(
                Text(destination["server"]), Text(destination["share"]), Text(destination["path"])),
            SshKeyPath = Text(root["ssh_key_path"]),
            SshPort = Port(root["ssh_port"], 22),
            SshRemotePort = Port(root["ssh_remote_port"], 0, allowZero: true),
            SshLocalPort = Port(root["ssh_local_port"], 0, allowZero: true),
            Theme = Text(root["theme"]) == "dark" ? "dark" : "light",
            Language = Localization.NormalizeLanguage(Text(root["language"]))
        };
    }

    public void Save(ClientSettings settings)
    {
        var root = ReadRoot();
        var server = root["server"] as JsonObject;
        if (server is null || Text(server["ip"]) != settings.Server)
        {
            root["server"] = new JsonObject { ["ip"] = settings.Server, ["name"] = settings.Server, ["selection"] = settings.Server };
        }

        root["username"] = settings.Username;
        root["domain"] = settings.Domain;
        root["sources"] = new JsonArray(settings.Sources.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        root["destination"] = settings.Destination is { } destination ? new JsonObject
        {
            ["server"] = destination.Server, ["share"] = destination.Share, ["path"] = destination.Path
        } : null;
        root["ssh_key_path"] = settings.SshKeyPath;
        root["ssh_port"] = Port(settings.SshPort, 22);
        root["ssh_remote_port"] = Port(settings.SshRemotePort, 0, allowZero: true);
        root["ssh_local_port"] = Port(settings.SshLocalPort, 0, allowZero: true);
        root["theme"] = settings.Theme == "dark" ? "dark" : "light";
        root["language"] = Localization.NormalizeLanguage(settings.Language);

        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new JsonException(Localization.Get("InvalidSettingsJson"));
    }

    private static string Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text.Trim() : "";

    private static int Port(JsonNode? value, int fallback, bool allowZero = false)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<int>(out var port) &&
            port <= 65535 && (allowZero ? port >= 0 : port >= 1))
        {
            return port;
        }

        return fallback;
    }
}