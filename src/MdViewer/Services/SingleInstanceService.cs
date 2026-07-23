using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace MdViewer.Services;

/// <summary>
/// Ensures only one MdViewer process runs; extra launches forward paths to the existing window.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Local\\MdViewer.SingleInstance.Mutex";
    private const string PipeName = "MdViewer.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;

    public bool IsPrimaryInstance => _ownsMutex;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out _ownsMutex);
    }

    public void StartServer(Dispatcher dispatcher, Action<string[]> onMessage)
    {
        if (!_ownsMutex) return;

        _listenCts = new CancellationTokenSource();
        var token = _listenCts.Token;
        _listenTask = Task.Run(() => ListenLoop(dispatcher, onMessage, token), token);
    }

    public static bool TrySendToExisting(IEnumerable<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 1500);
            var payload = JsonSerializer.Serialize(args.ToArray());
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(BitConverter.GetBytes(bytes.Length));
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to contact existing MdViewer instance: {ex.Message}");
            return false;
        }
    }

    private static async Task ListenLoop(Dispatcher dispatcher, Action<string[]> onMessage, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);

                var lengthBytes = new byte[4];
                if (await ReadExactAsync(server, lengthBytes, token) != 4) continue;
                var length = BitConverter.ToInt32(lengthBytes, 0);
                if (length is <= 0 or > 1024 * 1024) continue;

                var payload = new byte[length];
                if (await ReadExactAsync(server, payload, token) != length) continue;

                var json = Encoding.UTF8.GetString(payload);
                var args = JsonSerializer.Deserialize<string[]>(json) ?? [];
                _ = dispatcher.BeginInvoke(new Action(() => onMessage(args)));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Single-instance pipe listen error: {ex.Message}");
                try { await Task.Delay(250, token); } catch { break; }
            }
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token);
            if (read == 0) return offset;
            offset += read;
        }

        return offset;
    }

    public void Dispose()
    {
        try { _listenCts?.Cancel(); } catch { /* ignore */ }
        try { _listenTask?.Wait(500); } catch { /* ignore */ }
        _listenCts?.Dispose();

        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }

        _mutex.Dispose();
    }
}

public static class WindowActivation
{
    public static void BringToFront(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
