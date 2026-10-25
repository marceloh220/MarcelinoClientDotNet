using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace MarcelinoClient;

public sealed record SshHostKey(string Host, string Algorithm, string Fingerprint);

public sealed class SshHostKeyStore(string path)
{
    public bool Verify(string host, string algorithm, byte[] key, Func<SshHostKey, bool> confirm)
    {
        var entries = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? throw new InvalidDataException(Localization.Get("InvalidSshKeyStore"))
            : new Dictionary<string, string>();
        var identity = host.ToLowerInvariant();
        var value = $"{algorithm} {Convert.ToBase64String(key)}";
        if (entries.TryGetValue(identity, out var known))
        {
            if (!string.Equals(known, value, StringComparison.Ordinal))
            {
                throw new SshConnectionException(Localization.Get("SshHostKeyChanged"));
            }

            return true;
        }

        var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
        if (!confirm(new SshHostKey(host, algorithm, fingerprint)))
        {
            return false;
        }

        entries.Add(identity, value);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"ssh-host-keys.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return true;
    }
}

public sealed class SshConnection : IDisposable
{
    private readonly SshClient _client;
    private readonly PrivateKeyFile? _privateKey;
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(128);
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disposed;
    public ShellStream Shell { get; }

    private SshConnection(SshClient client, ShellStream shell, PrivateKeyFile? privateKey)
    {
        _client = client;
        Shell = shell;
        _privateKey = privateKey;
    }

    public static Task<SshConnection> ConnectAsync(string host, string username, string password,
        string keyPath, string passphrase, int columns, int rows, SshHostKeyStore hostKeys,
        Func<SshHostKey, bool> confirm, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        PrivateKeyFile? privateKey = null;
        SshClient? client = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrWhiteSpace(keyPath))
            {
                privateKey = string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(keyPath) : new PrivateKeyFile(keyPath, passphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(username, privateKey));
            }

            methods.Add(new PasswordAuthenticationMethod(username, password));
            var info = new ConnectionInfo(host, 22, username, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(8) };
            client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
            client.HostKeyReceived += (_, args) =>
            {
                args.CanTrust = false;
                cancellationToken.ThrowIfCancellationRequested();
                args.CanTrust = hostKeys.Verify(host, args.HostKeyName, args.HostKey, key =>
                    !cancellationToken.IsCancellationRequested && confirm(key) && !cancellationToken.IsCancellationRequested);
            };
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var shell = client.CreateShellStream("xterm-256color", (uint)columns, (uint)rows, 0, 0, 32768);
            if (cancellationToken.IsCancellationRequested)
            {
                shell.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new SshConnection(client, shell, privateKey);
        }
        catch
        {
            client?.Dispose();
            privateKey?.Dispose();
            throw;
        }
    }, cancellationToken);

    public void Send(byte[] data)
    {
        if (data.Length > 0 && !_outgoing.Writer.TryWrite(data))
        {
            throw new IOException(Localization.Get("SshInputQueueFull"));
        }
    }

    public async Task WriteInputAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        await foreach (var data in _outgoing.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
        {
            await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                await Shell.WriteAsync(data, 0, data.Length, linked.Token).ConfigureAwait(false);
                await Shell.FlushAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    public async Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _outgoing.Writer.TryComplete();
        _closed.Cancel();
        Shell.Dispose();
        _client.Dispose();
        _privateKey?.Dispose();
    }
}