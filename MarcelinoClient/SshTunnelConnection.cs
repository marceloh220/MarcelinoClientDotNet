using Renci.SshNet;

namespace MarcelinoClient;

public sealed class SshTunnelConnection : IDisposable
{
    private readonly SshClient _client;
    private readonly PrivateKeyFile? _privateKey;
    private readonly ForwardedPortLocal _forwardedPort;
    private int _disposed;

    private SshTunnelConnection(SshClient client, PrivateKeyFile? privateKey, ForwardedPortLocal forwardedPort)
    {
        _client = client;
        _privateKey = privateKey;
        _forwardedPort = forwardedPort;
    }

    public static async Task<SshTunnelConnection> ConnectAsync(string host, int sshPort, int remotePort, int localPort,
        string username, string password, string keyPath, string passphrase, SshHostKeyStore hostKeys,
        Func<SshHostKey, bool> confirm, CancellationToken cancellationToken)
    {
        var (client, privateKey) = await SshConnection.ConnectClientAsync(host, sshPort, username, password,
            keyPath, passphrase, hostKeys, confirm, cancellationToken);
        ForwardedPortLocal? forwardedPort = null;
        try
        {
            forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)localPort, "127.0.0.1", (uint)remotePort);
            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();
            cancellationToken.ThrowIfCancellationRequested();
            return new SshTunnelConnection(client, privateKey, forwardedPort);
        }
        catch
        {
            if (forwardedPort?.IsStarted == true)
            {
                forwardedPort.Stop();
            }

            client.Dispose();
            privateKey?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_forwardedPort.IsStarted)
            {
                _forwardedPort.Stop();
            }
        }
        finally
        {
            _client.Dispose();
            _privateKey?.Dispose();
        }
    }
}