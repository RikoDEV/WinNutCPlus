using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WinNutCPlus.Core.Tests;

/// <summary>
/// A minimal in-process NUT protocol (upsd) server for integration-testing NutSocket/UpsDevice
/// against a real TCP connection, instead of only unit-testing internal logic in isolation.
/// Supports the subset of the protocol WinNutCPlus actually uses: VER, NETVER, USERNAME, PASSWORD,
/// LOGIN, LOGOUT, GET VAR, GET DESC, LIST VAR.
/// </summary>
public sealed class MockNutServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _clientTasks = new();
    private readonly List<TcpClient> _activeClients = new();
    private readonly object _sync = new();
    private Task? _acceptLoop;

    public ConcurrentDictionary<string, string> Variables { get; } = new();
    public ConcurrentDictionary<string, string> Descriptions { get; } = new();
    public string UpsName { get; set; } = "testups";
    public bool RequireLogin { get; set; }
    public string ExpectedUsername { get; set; } = "admin";
    public string ExpectedPassword { get; set; } = "secret";

    /// <summary>When true, the next accepted client connection is immediately closed without responding — simulates a dead/broken connection.</summary>
    public bool DropNextConnection { get; set; }

    public int Port { get; }

    /// <summary>Forcibly closes all currently-connected client sockets, simulating a dropped/broken connection.</summary>
    public void CloseAllConnections()
    {
        TcpClient[] clients;
        lock (_sync) clients = _activeClients.ToArray();

        foreach (var client in clients)
        {
            try { client.Close(); } catch { /* ignore */ }
        }

        lock (_sync) _activeClients.Clear();
    }

    public MockNutServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Start()
    {
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (DropNextConnection)
            {
                DropNextConnection = false;
                client.Close();
                continue;
            }

            lock (_sync) _activeClients.Add(client);
            var task = HandleClientAsync(client, cancellationToken);
            lock (_sync) _clientTasks.Add(task);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try
        {
            await HandleClientCoreAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) _activeClients.Remove(client);
        }
    }

    private async Task HandleClientCoreAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

        string? pendingUsername = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (line is null) return;

            var parts = line.Split(' ', 4);
            switch (parts[0])
            {
                case "VER":
                    await writer.WriteLineAsync("Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/").ConfigureAwait(false);
                    break;

                case "NETVER":
                    await writer.WriteLineAsync("1.2").ConfigureAwait(false);
                    break;

                case "USERNAME":
                    pendingUsername = parts.Length > 1 ? parts[1] : null;
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                    break;

                case "PASSWORD":
                    var password = parts.Length > 1 ? parts[1] : null;
                    if (RequireLogin && (pendingUsername != ExpectedUsername || password != ExpectedPassword))
                    {
                        await writer.WriteLineAsync("ERR ACCESS-DENIED").ConfigureAwait(false);
                    }
                    else
                    {
                        await writer.WriteLineAsync("OK").ConfigureAwait(false);
                    }
                    break;

                case "LOGIN":
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                    break;

                case "LOGOUT":
                    await writer.WriteLineAsync("OK Goodbye").ConfigureAwait(false);
                    return;

                case "GET" when parts.Length >= 2 && parts[1] == "VAR":
                    await HandleGetVarAsync(writer, parts).ConfigureAwait(false);
                    break;

                case "GET" when parts.Length >= 2 && parts[1] == "DESC":
                    await HandleGetDescAsync(writer, parts).ConfigureAwait(false);
                    break;

                case "LIST" when parts.Length >= 2 && parts[1] == "VAR":
                    await HandleListVarAsync(writer).ConfigureAwait(false);
                    break;

                default:
                    await writer.WriteLineAsync("ERR UNKNOWN-COMMAND").ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task HandleGetVarAsync(TextWriter writer, string[] parts)
    {
        // "GET VAR <upsname> <varname>" splits into exactly 4 tokens (no embedded spaces in a request).
        var varName = parts.Length > 3 ? parts[3] : string.Empty;

        if (Variables.TryGetValue(varName, out var value))
        {
            await writer.WriteLineAsync($"VAR {UpsName} {varName} \"{value}\"").ConfigureAwait(false);
        }
        else
        {
            await writer.WriteLineAsync("ERR VAR-NOT-SUPPORTED").ConfigureAwait(false);
        }
    }

    private async Task HandleGetDescAsync(TextWriter writer, string[] parts)
    {
        var varName = parts.Length > 3 ? parts[3] : string.Empty;
        var desc = Descriptions.GetValueOrDefault(varName, varName);
        await writer.WriteLineAsync($"DESC {UpsName} {varName} \"{desc}\"").ConfigureAwait(false);
    }

    private async Task HandleListVarAsync(TextWriter writer)
    {
        await writer.WriteLineAsync($"BEGIN LIST VAR {UpsName}").ConfigureAwait(false);
        foreach (var (key, value) in Variables)
        {
            await writer.WriteLineAsync($"VAR {UpsName} {key} \"{value}\"").ConfigureAwait(false);
        }
        await writer.WriteLineAsync($"END LIST VAR {UpsName}").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        Task[] tasks;
        lock (_sync) tasks = _clientTasks.ToArray();

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
