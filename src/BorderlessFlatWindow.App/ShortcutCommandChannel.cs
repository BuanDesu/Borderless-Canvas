using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BorderlessFlatWindow.App;

internal enum ShortcutDelivery { NotRunning, Delivered, Unavailable }
internal sealed class ShortcutCommandChannel : IDisposable
{
    private sealed record Request(int Version, string Shortcut);
    private readonly CancellationTokenSource stopped = new();
    private readonly NamedPipeServerStream pipe;
    private readonly Action<string> receive;
    private readonly Task loop;
    private bool disposed;
    private string lastStage = "starting", lastError = "none";
    internal string Diagnostics => $"{loop.Status}; {Volatile.Read(ref lastStage)}; {Volatile.Read(ref lastError)}";
    internal static string DefaultName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent(); using var process = System.Diagnostics.Process.GetCurrentProcess();
            var user = identity.User?.Value ?? throw new InvalidOperationException("无法验证当前用户。");
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(user + ":" + process.SessionId));
            return "BFW.Sticky." + Convert.ToHexString(hash.AsSpan(0, 12));
        }
    }
    internal ShortcutCommandChannel(Action<string> receive, string? name = null)
    {
        this.receive = receive;
        pipe = new(name ?? DefaultName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 4096, 4096);
        loop = Listen();
    }
    private async Task Listen()
    {
        while (!stopped.IsCancellationRequested)
        {
            var connected = false;
            try
            {
                lastStage = "waiting";
                await pipe.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false);
                connected = true;
                lastStage = "reading";
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token); timeout.CancelAfter(3000);
                var header = new byte[4]; await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                var count = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (count is < 1 or > 65536) throw new InvalidDataException("Invalid command size.");
                var data = new byte[count]; await pipe.ReadExactlyAsync(data, timeout.Token).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<Request>(data, new JsonSerializerOptions { MaxDepth = 4, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
                if (request is null || request.Version != 1 || !Path.IsPathFullyQualified(request.Shortcut) || !string.Equals(Path.GetExtension(request.Shortcut), ".lnk", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid shortcut request.");
                receive(request.Shortcut);
                lastStage = "responding";
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or JsonException or ArgumentException or InvalidOperationException)
            { lastError = error.GetType().Name + ": " + error.Message; if (stopped.IsCancellationRequested) return; }
            finally
            {
                // A peer can close early and put PipeStream into Broken (IsConnected=false).
                // Disconnect that completed connection too, so the next request can be accepted.
                if (connected && !stopped.IsCancellationRequested)
                    try { pipe.Disconnect(); } catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { lastError = "disconnect: " + error.Message; }
            }
        }
    }
    internal static async Task<ShortcutDelivery> Send(string shortcut, string? name = null)
    {
        var endpoint = name ?? DefaultName;
        using var client = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var timeout = new CancellationTokenSource(1800);
            await client.ConnectAsync(700, timeout.Token).ConfigureAwait(false);
            var data = JsonSerializer.SerializeToUtf8Bytes(new Request(1, Path.GetFullPath(shortcut)));
            if (data.Length > 65536) return ShortcutDelivery.Unavailable;
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
            await client.WriteAsync(header, timeout.Token).ConfigureAwait(false); await client.WriteAsync(data, timeout.Token).ConfigureAwait(false); await client.FlushAsync(timeout.Token).ConfigureAwait(false);
            var result = new byte[1]; await client.ReadExactlyAsync(result, timeout.Token).ConfigureAwait(false);
            return result[0] == 1 ? ShortcutDelivery.Delivered : ShortcutDelivery.Unavailable;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            var exists = WaitNamedPipe(@"\\.\pipe\" + endpoint, 0);
            return !exists && Marshal.GetLastPInvokeError() == 2 ? ShortcutDelivery.NotRunning : ShortcutDelivery.Unavailable;
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; stopped.Cancel(); pipe.Dispose(); _ = loop.ContinueWith(_ => stopped.Dispose(), TaskScheduler.Default); }
    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WaitNamedPipe(string pipe, uint timeout);
}
