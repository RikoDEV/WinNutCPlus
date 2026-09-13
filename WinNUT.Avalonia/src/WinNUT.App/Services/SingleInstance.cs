using System.IO.Pipes;
using System.Threading;

namespace WinNUT.App.Services;

/// <summary>
/// Enforces a single running instance via a named mutex, and signals an already-running
/// instance to bring its window to the foreground via a named pipe. This is a deliberate small
/// UX improvement over the original app (which used WinForms' IsSingleInstance flag, silently
/// blocking a second launch with no foreground activation).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Local\\WinNUT-SingleInstance";
    private const string PipeName = "WinNUT-ActivateExisting";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCts;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Starts listening for activation signals from later launch attempts. Call only when <see cref="IsFirstInstance"/>.</summary>
    public void ListenForActivation(Action onActivationRequested)
    {
        _listenerCts = new CancellationTokenSource();
        _ = ListenLoopAsync(onActivationRequested, _listenerCts.Token);
    }

    private static async Task ListenLoopAsync(Action onActivationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                onActivationRequested();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Ignore transient pipe errors and keep listening.
            }
        }
    }

    /// <summary>Signals the already-running first instance to activate its window. Call only when NOT <see cref="IsFirstInstance"/>.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
        }
        catch
        {
            // Best-effort; if this fails, the second launch simply exits with no visible feedback,
            // matching (at worst) the original app's silent behavior.
        }
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
