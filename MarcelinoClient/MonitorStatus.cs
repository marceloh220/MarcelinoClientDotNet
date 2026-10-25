using System.Globalization;
using System.Text.Json;

namespace MarcelinoClient;

public sealed record FileSystemMetric(string Mountpoint, string Device, string Format, string Usage, string Free);
public sealed record DiskMetric(string Device, string Model, string Capacity, string Temperature, string Smart);
public sealed record RaidMetric(string Name, string Level, string Status, string Devices, string Activity);
public sealed record LeaseMetric(string Ip, string Hostname, string Mac, string Expires);
public sealed record ShareMetric(string Name, string Path, string Users, string ReadOnly, string WriteAccess);

public sealed class MonitorStatus
{
    public string Overview { get; private init; } = "";
    public string Cpu { get; private init; } = Localization.Get("NoData");
    public string CpuDetails { get; private init; } = Localization.Get("NoData");
    public string Memory { get; private init; } = Localization.Get("NoData");
    public string MemoryDetails { get; private init; } = Localization.Get("NoData");
    public string Uptime { get; private init; } = Localization.Get("NoData");
    public string Network { get; private init; } = Localization.Get("NoData");
    public KeyValuePair<string, string>[] Hardware { get; private init; } = [];
    public FileSystemMetric[] FileSystems { get; private init; } = [];
    public DiskMetric[] Disks { get; private init; } = [];
    public string StorageStatus { get; private init; } = "";
    public string RaidStatus { get; private init; } = "";
    public RaidMetric[] Arrays { get; private init; } = [];
    public string DnsStatus { get; private init; } = "";
    public LeaseMetric[] Leases { get; private init; } = [];
    public string AllowedEvents { get; private init; } = "";
    public string BlockedEvents { get; private init; } = "";
    public string LocalIp { get; private init; } = "";
    public string PublicIp { get; private init; } = "";
    public string InternetStatus { get; private init; } = "";
    public string InternetTests { get; private init; } = "";
    public ShareMetric[] Shares { get; private init; } = [];
    public string[] HiddenShares { get; private init; } = [];
    public string ShareStatus { get; private init; } = "";

    public string[] FilterBrowseableShares(IEnumerable<string> shares)
    {
        var hidden = new HashSet<string>(HiddenShares, StringComparer.OrdinalIgnoreCase) { "Monitor" };
        return shares.Where(share => !hidden.Contains(share)).ToArray();
    }

    public static MonitorStatus Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var memory = Field(root, "memory");
        var network = Field(root, "network");
        if (root.ValueKind != JsonValueKind.Object || memory.ValueKind != JsonValueKind.Object ||
            network.ValueKind != JsonValueKind.Object || Number(root, "cpu_percent") is null ||
            Number(root, "uptime_seconds") is null || Number(memory, "percent") is null)
        {
            throw new JsonException(Localization.Get("InvalidStatusJson"));
        }

        var system = Field(root, "system_info");
        var packages = Field(system, "package_count");
        var raid = Field(root, "raid");
        var dns = Field(root, "dns_dhcp");
        var internet = Field(root, "internet");
        var tests = Items(internet, "tests").Reverse().Select(FormatSpeedTest).ToArray();
        var shares = Items(root, "samba_shares")
            .Where(share => !IsNo(Text(share, "browseable", "yes")) &&
                !string.Equals(Text(share, "name"), "Monitor", StringComparison.OrdinalIgnoreCase))
            .OrderBy(share => Text(share, "name"), StringComparer.OrdinalIgnoreCase)
            .Select(share =>
            {
                var readOnly = Text(share, "read_only", "");
                var writeList = Text(share, "write_list", "");
                return new ShareMetric(Text(share, "name"), Text(share, "path"),
                    Text(share, "valid_users", IsYes(Text(share, "guest_ok", "")) ? Localization.Get("AllGuests") : Localization.Get("SambaDefault")),
                    IsYes(readOnly) ? Localization.Get("Yes") : IsNo(readOnly) ? Localization.Get("No") : Localization.Get("SambaDefault"),
                    IsNo(readOnly) ? Localization.Get("AllUsers") : string.IsNullOrWhiteSpace(writeList) ? Localization.Get("Nobody") : writeList);
            }).ToArray();

        return new MonitorStatus
        {
            Overview = Localization.Get("OverviewFormat", Text(root, "hostname"), Text(system, "os_name", Text(root, "os")), Text(root, "sampled_at")),
            Cpu = $"{Number(root, "cpu_percent"):0.#}%",
            CpuDetails = Localization.Get("CpuDetailsFormat", Text(root, "cpu_count"), Frequency(Number(root, "cpu_frequency_mhz")), Temperature(Number(root, "cpu_temperature_c"))),
            Memory = $"{Number(memory, "percent"):0.#}%",
            MemoryDetails = Localization.Get("MemoryDetailsFormat", Bytes(Number(memory, "used_bytes")), Bytes(Number(memory, "total_bytes"))),
            Uptime = Duration(Number(root, "uptime_seconds")!.Value),
            Network = Localization.Get("NetworkFormat", Bytes(Number(network, "bytes_received")), Bytes(Number(network, "bytes_sent"))),
            Hardware = new Dictionary<string, string>
            {
                [Localization.Get("OperatingSystem")] = Text(system, "os_name", Text(root, "os")),
                [Localization.Get("Kernel")] = Text(system, "kernel", Text(root, "os_release")),
                [Localization.Get("ComputerModel")] = Text(system, "computer_model"),
                [Localization.Get("Processor")] = Text(system, "cpu_model"),
                [Localization.Get("PhysicalLogicalCoresLabel")] = Localization.Get("PhysicalLogicalCoresFormat", Text(system, "cpu_physical_count", "0"), Text(system, "cpu_logical_count", Text(root, "cpu_count"))),
                ["GPU"] = string.Join(", ", Items(system, "gpu_models").Select(value => value.ToString())) is { Length: > 0 } gpu ? gpu : Localization.Get("Unavailable"),
                [Localization.Get("InstalledPackages")] = Number(packages, "count") is { } count ? Localization.Get("PackageCountFormat", count, Text(packages, "manager", Localization.Get("Packages"))) : Text(packages, "manager"),
                [Localization.Get("DefaultFont")] = Text(system, "font"),
                ["Shell"] = Text(system, "shell")
            }.ToArray(),
            FileSystems = Items(root, "filesystems").Select(entry => new FileSystemMetric(
                Text(entry, "mountpoint"), Text(entry, "device"), Text(entry, "filesystem"),
                Percent(Number(entry, "percent")), Bytes(Number(entry, "free_bytes")))).ToArray(),
            Disks = Items(root, "physical_disks").Select(entry => new DiskMetric(
                Text(entry, "name"), Text(entry, "model"), Bytes(Number(entry, "capacity_bytes")),
                Temperature(Number(entry, "temperature_c")), Text(entry, "smart_status"))).ToArray(),
            StorageStatus = string.Join(" · ", new[] { Text(root, "share_discovery_error", ""), Text(root, "smartctl_error", "") }
                .Where(value => value.Length > 0)),
            RaidStatus = Localization.Get("RaidStatusFormat", Text(raid, "status")),
            Arrays = Items(raid, "arrays").Select(entry => new RaidMetric(Text(entry, "name"), Text(entry, "level"),
                Text(entry, "status"), $"{Text(entry, "active_devices", Localization.Get("NoData"))}/{Text(entry, "total_devices", Localization.Get("NoData"))}",
                $"{Text(entry, "sync_action", Localization.Get("NoData"))} {Percent(Number(entry, "progress_percent"), "")}".Trim())).ToArray(),
            DnsStatus = Localization.Get("DnsStatusFormat", Text(dns, "service_state"), Enabled(dns, "dns_enabled"), Enabled(dns, "dhcp_enabled"),
                Text(dns, "lease_count", "0"), Text(dns, "access_log_error", Text(dns, "journal_error", Text(dns, "access_log_path", "")))),
            Leases = Items(dns, "leases").Select(entry => new LeaseMetric(Text(entry, "ip"), Text(entry, "hostname", Localization.Get("NoHostname")),
                Text(entry, "mac"), Text(entry, "expires_at", Localization.Get("NoExpiry")))).ToArray(),
            AllowedEvents = Events(dns, "allowed_events"),
            BlockedEvents = Events(dns, "blocked_events"),
            LocalIp = Text(internet, "local_ip"),
            PublicIp = Text(internet, "remote_ip"),
            InternetStatus = Text(internet, "log_error", Localization.Get("InternetTestsSummary", tests.Length)),
            InternetTests = tests.Length > 0 ? string.Join(Environment.NewLine, tests) : Localization.Get("NoInternetTests"),
            Shares = shares,
            HiddenShares = Items(root, "samba_shares")
                .Where(share => IsNo(Text(share, "browseable", "yes")))
                .Select(share => Text(share, "name", "").Trim())
                .Where(name => name.Length > 0).ToArray(),
            ShareStatus = Field(root, "samba_shares").ValueKind == JsonValueKind.Array
                ? Localization.Get("ShareConfiguredCount", shares.Length)
                : Localization.Get("SambaPermissionsMissing")
        };
    }

    private static JsonElement Field(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static JsonElement[] Items(JsonElement parent, string name) =>
        Field(parent, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToArray() : [];

    private static string Text(JsonElement parent, string name, string? fallback = null) =>
        Field(parent, name) is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value &&
        !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : fallback ?? Localization.Get("Unavailable");

    private static double? Number(JsonElement parent, string name) =>
        Field(parent, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var number) &&
        double.IsFinite(number) ? number : null;

    private static bool IsYes(string value) => value.Trim().ToLowerInvariant() is "yes" or "true" or "1";
    private static bool IsNo(string value) => value.Trim().ToLowerInvariant() is "no" or "false" or "0";
    private static string Enabled(JsonElement parent, string name) => IsYes(Text(parent, name, "")) ? Localization.Get("Enabled") : Localization.Get("Disabled");
    private static string Percent(double? value, string? fallback = null) => value is null ? fallback ?? Localization.Get("Unavailable") : $"{value:0.#}%";
    private static string Temperature(double? value) => value is null ? Localization.Get("Unavailable") : $"{value:0.#} °C";
    private static string Frequency(double? value) => value is null or <= 0 ? Localization.Get("NotAvailableShort") : value >= 1000 ? $"{value / 1000:0.00} GHz" : $"{value:0} MHz";

    private static string Bytes(double? value)
    {
        if (value is null or < 0)
        {
            return Localization.Get("Unavailable");
        }

        var size = value.Value;
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size.ToString(index == 0 ? "0" : "0.0", CultureInfo.CurrentCulture)} {units[index]}";
    }

    private static string Duration(double seconds)
    {
        var minutes = Math.Floor(Math.Max(0, seconds) / 60);
        var days = Math.Floor(minutes / 1440);
        var hours = Math.Floor(minutes / 60) % 24;
        return days > 0 ? Localization.Get("DurationDaysFormat", days, hours) : hours > 0
            ? Localization.Get("DurationHoursFormat", hours, minutes % 60)
            : Localization.Get("DurationMinutesFormat", minutes);
    }

    private static string Events(JsonElement dns, string name)
    {
        var lines = Items(dns, name).Select(entry => $"{Text(entry, "timestamp", "")} [{Text(entry, "kind", "dns").ToUpperInvariant()}] {Text(entry, "message", "")}");
        return lines.Any() ? string.Join(Environment.NewLine, lines) : Localization.Get("NoEvents");
    }

    private static string FormatSpeedTest(JsonElement entry)
    {
        var timestamp = Text(entry, "timestamp", "").Replace('T', ' ');
        if (Field(entry, "error").ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return Localization.Get("SpeedTestErrorFormat", timestamp, Text(entry, "error"));
        }

        if (Number(entry, "download_mbps") is not { } download || Number(entry, "upload_mbps") is not { } upload)
        {
            return Localization.Get("InvalidSpeedTestFormat", timestamp);
        }

        return Localization.Get("SpeedTestFormat", timestamp, download, upload, Number(entry, "ping_ms"), Text(entry, "server", Localization.Get("UnknownServer")));
    }
}