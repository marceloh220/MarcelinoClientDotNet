using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MarcelinoClient;

public sealed record SmbHost(string Address, string Name)
{
    public override string ToString() => $"{Name} ({Address})";
}

public sealed class SmbClient
{
    private const int SmbPort = 445;
    private const uint ResourceTypeDisk = 1;
    private const uint ConnectTemporary = 4;
    private const uint ShareTypeSpecial = 0x80000000;
    private const uint ErrorMoreData = 234;

    public async Task<IReadOnlyList<SmbHost>> DiscoverHostsAsync(CancellationToken cancellationToken)
    {
        var localAddress = GetLocalAddress();
        if (localAddress is null)
        {
            throw new InvalidOperationException(Localization.Get("LocalIpv4Unavailable"));
        }

        var prefix = string.Join('.', localAddress.GetAddressBytes().Take(3));
        using var concurrencyLimit = new SemaphoreSlim(64);
        var hosts = new ConcurrentBag<SmbHost>();
        var probes = Enumerable.Range(1, 254).Select(async lastOctet =>
        {
            await concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var address = $"{prefix}.{lastOctet}";
                using var client = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    await client.ConnectAsync(address, SmbPort, cancellationToken).AsTask()
                        .WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                catch (Exception error) when (error is SocketException or TimeoutException)
                {
                    return;
                }

                var name = address;
                try
                {
                    name = (await Dns.GetHostEntryAsync(address, cancellationToken)
                        .WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken)).HostName
                        .Split('.')[0];
                }
                catch (Exception error) when (error is SocketException or TimeoutException)
                {
                }

                hosts.Add(new SmbHost(address, name));
            }
            finally
            {
                concurrencyLimit.Release();
            }
        });

        await Task.WhenAll(probes);
        return hosts.OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> ListShares(string host, string username, string password, string domain)
    {
        var resource = new NetResource
        {
            Type = ResourceTypeDisk,
            RemoteName = $"\\\\{host}\\IPC$"
        };
        var qualifiedUsername = string.IsNullOrWhiteSpace(domain)
            ? username
            : $"{domain}\\{username}";

        var connectionStatus = WNetAddConnection2(ref resource, password, qualifiedUsername, ConnectTemporary);
        if (connectionStatus != 0)
        {
            throw new Win32Exception((int)connectionStatus);
        }

        var keepConnection = false;
        try
        {
            var shares = new List<string>();
            uint resumeHandle = 0;
            while (true)
            {
                var status = NetShareEnum(
                    $"\\\\{host}", 1, out var buffer, uint.MaxValue,
                    out var entriesRead, out _, ref resumeHandle);

                try
                {
                    if (status != 0 && status != ErrorMoreData)
                    {
                        throw new Win32Exception((int)status);
                    }

                    var entrySize = Marshal.SizeOf<ShareInfo1>();
                    for (var index = 0; index < entriesRead; index++)
                    {
                        var entryPointer = IntPtr.Add(buffer, checked((int)index * entrySize));
                        var entry = Marshal.PtrToStructure<ShareInfo1>(entryPointer);
                        if ((entry.Type & ShareTypeSpecial) == 0 && (entry.Type & 0xFFFF) == 0 &&
                            !string.IsNullOrWhiteSpace(entry.Name))
                        {
                            shares.Add(entry.Name);
                        }
                    }
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        NetApiBufferFree(buffer);
                    }
                }

                if (status == 0)
                {
                    break;
                }
            }

            var result = shares.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(share => share, StringComparer.OrdinalIgnoreCase).ToArray();
            keepConnection = true;
            return result;
        }
        finally
        {
            if (!keepConnection)
            {
                WNetCancelConnection2(resource.RemoteName!, 0, false);
            }
        }
    }

    public void Disconnect(string host)
    {
        WNetCancelConnection2($"\\\\{host}\\IPC$", 0, false);
    }

    public MonitorStatus ReadStatus(string host)
    {
        using var stream = new FileStream($"\\\\{host}\\Monitor\\.marcelinoSrv.json", FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return MonitorStatus.Parse(reader.ReadToEnd());
    }

    private static IPAddress? GetLocalAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect("10.255.255.255", 1);
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return IPAddress.Loopback;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? RemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Provider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Remark;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", CharSet = CharSet.Unicode)]
    private static extern uint WNetAddConnection2(
        ref NetResource resource, string password, string username, uint flags);

    [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", CharSet = CharSet.Unicode)]
    private static extern uint WNetCancelConnection2(string name, uint flags, bool force);

    [DllImport("Netapi32.dll", EntryPoint = "NetShareEnum", CharSet = CharSet.Unicode)]
    private static extern uint NetShareEnum(
        string serverName, uint level, out IntPtr buffer, uint preferredMaximumLength,
        out uint entriesRead, out uint totalEntries, ref uint resumeHandle);

    [DllImport("Netapi32.dll", EntryPoint = "NetApiBufferFree")]
    private static extern uint NetApiBufferFree(IntPtr buffer);
}