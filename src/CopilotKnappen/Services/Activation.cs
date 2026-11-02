using System.IO;
using System.IO.Pipes;
using System.Text;

namespace CopilotKnappen.Services;

public enum ActivationKind
{
    /// <summary>Normal launch or Copilot key single tap: toggle the launcher.</summary>
    Toggle,
    /// <summary>Copilot key pressed and held: show the launcher.</summary>
    Show,
    /// <summary>Copilot key released after press-and-hold: nothing to do (the launcher stays open).</summary>
    KeyReleased,
    /// <summary>Open the settings window.</summary>
    Settings,
}

/// <summary>
/// Works out why we were started. The Copilot key provider extension in the package manifest activates the
/// app through the copilotknappen:// protocol with ?state=Tap, ?state=Down or ?state=Up. Packaged apps read
/// that through AppInstance.GetActivatedEventArgs(); a URI passed on the command line is accepted too.
/// </summary>
public static class ActivationParser
{
    public const string ProtocolScheme = "copilotknappen";

    public static ActivationKind FromArguments(IEnumerable<string> args)
    {
        foreach (var raw in args)
        {
            var arg = raw.Trim().Trim('"');
            if (arg.Equals("--settings", StringComparison.OrdinalIgnoreCase) || arg.Equals("/settings", StringComparison.OrdinalIgnoreCase))
                return ActivationKind.Settings;
            if (arg.Equals("--show", StringComparison.OrdinalIgnoreCase))
                return ActivationKind.Show;
            if (arg.StartsWith(ProtocolScheme + ":", StringComparison.OrdinalIgnoreCase))
                return FromUri(arg);
        }

        // Packaged apps can also read the activation through WinRT.
        try
        {
            if (Windows.ApplicationModel.AppInstance.GetActivatedEventArgs() is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs p)
                return FromUri(p.Uri.OriginalString);
        }
        catch
        {
            // Not packaged / API unavailable: a plain launch.
        }

        return ActivationKind.Toggle;
    }

    public static ActivationKind FromUri(string uri)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.Contains("settings")) return ActivationKind.Settings;
        if (lower.Contains("state=down")) return ActivationKind.Show;
        if (lower.Contains("state=up")) return ActivationKind.KeyReleased;
        return ActivationKind.Toggle; // state=Tap or anything else
    }
}

/// <summary>Ensures a single running instance; later launches forward their activation over a named pipe.</summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Id = $"CopilotKnappen-{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Id}", out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public event Action<ActivationKind>? Activated;

    public void StartListening()
    {
        var thread = new Thread(Listen) { IsBackground = true, Name = "Activation pipe" };
        thread.Start();
    }

    private void Listen()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                server.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = reader.ReadLine();
                if (Enum.TryParse<ActivationKind>(line, out var kind)) Activated?.Invoke(kind);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // Client went away (or the pipe is busy); keep listening.
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Sends the activation to the primary instance. Returns false if it could not be reached.</summary>
    public static bool SendToPrimary(ActivationKind kind)
    {
        // We were launched by the shell (key press), so we may hand our foreground rights to the primary instance.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1500);
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine(kind.ToString());
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Thread.Sleep(200);
            }
        }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* released on another thread */ }
        }
        _mutex.Dispose();
    }
}
