using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ChessCore.Uci;

/// <summary>
/// Runs a UCI engine as a child process and talks to it over stdin/stdout.
/// </summary>
/// <remarks>
/// Output is read on a background thread and <see cref="Log"/>/<see cref="Exited"/>
/// are raised there. Searches and setup exchanges are serialised: a new search
/// waits until the engine has answered the previous one with "bestmove", so a
/// late reply can never be mistaken for the answer to a newer position.
/// </remarks>
public sealed class UciEngine : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    // An engine busy searching doesn't read stdin, so it wouldn't notice the GUI closing
    // and could keep a CPU core busy forever. Every started engine is killed on exit.
    private static readonly HashSet<Process> RunningProcesses = [];

    static UciEngine()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllRunningProcesses();
    }

    private readonly object stateLock = new();
    private readonly object writeLock = new();
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private readonly List<UciOption> options = [];
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process? process;
    private TaskCompletionSource? pendingUciOk;
    private TaskCompletionSource? pendingReadyOk;
    private PendingSearch? pendingSearch;
    private volatile bool hasExited;
    private int disposed;

    public UciEngine(string executablePath, string? arguments = null)
    {
        ExecutablePath = executablePath;
        Arguments = arguments ?? "";
        Name = Path.GetFileNameWithoutExtension(executablePath);
    }

    /// <summary>Every line sent or received, for a protocol console. Raised on a background thread.</summary>
    public event Action<EngineLogEntry>? Log;

    /// <summary>The process ended (crash, "quit", or killed). Raised on a background thread.</summary>
    public event Action<int?>? Exited;

    public string ExecutablePath { get; }

    public string Arguments { get; }

    /// <summary>From "id name"; the file name until the engine says otherwise.</summary>
    public string Name { get; private set; }

    public string? Author { get; private set; }

    public IReadOnlyList<UciOption> Options
    {
        get
        {
            lock (stateLock)
                return options.ToArray();
        }
    }

    public bool HasExited => hasExited;

    /// <summary>A search or setup exchange is in progress (the engine hasn't answered it yet).</summary>
    public bool IsBusy => exchangeGate.CurrentCount == 0;

    /// <summary>Ends the engine process at once, e.g. when it keeps searching after "stop".</summary>
    public void Kill()
    {
        try
        {
            if (process is { } p && !hasExited)
                p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>Starts the process and performs the uci / isready handshake.</summary>
    public async Task StartAsync(TimeSpan? handshakeTimeout = null, CancellationToken cancellationToken = default)
    {
        if (process is not null)
            throw new InvalidOperationException("The engine has already been started.");

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = Arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Engines often look for files (opening books, networks) next to the executable.
        if (File.Exists(ExecutablePath))
            startInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)) ?? "";

        var p = new Process { StartInfo = startInfo };
        try
        {
            p.Start();
        }
        catch (Win32Exception ex)
        {
            p.Dispose();
            throw new UciEngineException($"Could not start '{ExecutablePath}': {ex.Message}", ex);
        }

        lock (RunningProcesses)
            RunningProcesses.Add(p);
        p.StandardInput.NewLine = "\n";
        p.StandardInput.AutoFlush = true;
        process = p;

        var uciOk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateLock)
            pendingUciOk = uciOk;
        StartReaders(p);

        var timeout = handshakeTimeout ?? TimeSpan.FromSeconds(10);
        try
        {
            Send("uci");
            await WaitForReplyAsync(uciOk.Task, timeout, "uci", "uciok", cancellationToken).ConfigureAwait(false);
            await IsReadyCoreAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends "ucinewgame" and waits for the engine to be ready.</summary>
    public async Task NewGameAsync(CancellationToken cancellationToken = default)
    {
        await exchangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Send("ucinewgame");
            await IsReadyCoreAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            exchangeGate.Release();
        }
    }

    /// <summary>Sends "setoption"; pass a null value for button options.</summary>
    public async Task SetOptionAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        await exchangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Send(value is null ? $"setoption name {name}" : $"setoption name {name} value {value}");
            await IsReadyCoreAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            exchangeGate.Release();
        }
    }

    /// <summary>
    /// Sets up a position, searches, and returns the engine's move. Cancelling sends
    /// "stop" and returns immediately; the engine still has to answer with "bestmove"
    /// before the next search is started.
    /// </summary>
    public async Task<UciBestMove> SearchAsync(
        string positionCommand,
        SearchLimits limits,
        IProgress<UciInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await exchangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var search = new PendingSearch(progress);
        try
        {
            lock (stateLock)
            {
                ThrowIfExited();
                pendingSearch = search;
            }
            Send(positionCommand);
            Send(limits.ToGoCommand());
            search.Clock.Start();
        }
        catch
        {
            lock (stateLock)
                pendingSearch = null;
            exchangeGate.Release();
            throw;
        }

        // Only a bestmove (or the process dying) frees the engine for the next exchange.
        _ = search.Completion.Task.ContinueWith(
            _ => exchangeGate.Release(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        using var registration = cancellationToken.Register(() =>
        {
            if (!search.Completion.Task.IsCompleted)
                TrySend("stop");
        });
        return await search.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes once no search or setup exchange is in progress, e.g. when an engine
    /// that ignores "stop" is still finishing an abandoned search.
    /// </summary>
    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        await exchangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        exchangeGate.Release();
    }

    /// <summary>Sends a raw line, e.g. from a debug console.</summary>
    public void SendCommand(string line) => Send(line);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
            return;
        var p = process;
        if (p is null)
            return;

        if (!hasExited)
        {
            TrySend("stop");
            TrySend("quit");
            try
            {
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Already gone.
                }
            }
        }

        try
        {
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        lock (RunningProcesses)
            RunningProcesses.Remove(p);
        p.Dispose();
    }

    private static void KillAllRunningProcesses()
    {
        Process[] processes;
        lock (RunningProcesses)
            processes = RunningProcesses.ToArray();
        foreach (var p in processes)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }
        }
    }

    private void StartReaders(Process p)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await p.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                    HandleLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
            await OnOutputClosedAsync(p).ConfigureAwait(false);
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await p.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                    RaiseLog(EngineLogDirection.StdErr, line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        });
    }

    private void HandleLine(string line)
    {
        RaiseLog(EngineLogDirection.FromEngine, line);

        var trimmed = line.Trim();
        int space = trimmed.IndexOfAny([' ', '\t']);
        var command = space < 0 ? trimmed : trimmed[..space];
        var rest = space < 0 ? "" : trimmed[(space + 1)..].TrimStart();

        switch (command)
        {
            case "id" when rest.StartsWith("name ", StringComparison.Ordinal):
                Name = rest[5..].Trim();
                break;
            case "id" when rest.StartsWith("author ", StringComparison.Ordinal):
                Author = rest[7..].Trim();
                break;
            case "option":
                if (UciOption.Parse(trimmed) is { } option)
                    lock (stateLock)
                        options.Add(option);
                break;
            case "uciok":
                Complete(ref pendingUciOk);
                break;
            case "readyok":
                Complete(ref pendingReadyOk);
                break;
            case "info":
                PendingSearch? current;
                lock (stateLock)
                    current = pendingSearch;
                if (current?.Progress is { } progress && UciInfo.Parse(trimmed) is { } info)
                    progress.Report(info);
                break;
            case "bestmove":
                var tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                PendingSearch? finished;
                lock (stateLock)
                {
                    finished = pendingSearch;
                    pendingSearch = null;
                }
                finished?.Completion.TrySetResult(new UciBestMove(
                    tokens.Length > 0 ? tokens[0] : "(none)",
                    tokens.Length > 2 && tokens[1] == "ponder" ? tokens[2] : null,
                    finished.Clock.Elapsed));
                break;
        }
    }

    private void Complete(ref TaskCompletionSource? pending)
    {
        TaskCompletionSource? tcs;
        lock (stateLock)
        {
            tcs = pending;
            pending = null;
        }
        tcs?.TrySetResult();
    }

    private async Task OnOutputClosedAsync(Process p)
    {
        int? exitCode = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            exitCode = p.ExitCode;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
        }

        var error = new UciEngineException(exitCode is { } code
            ? $"The engine exited (code {code})."
            : "The engine closed its output.");

        TaskCompletionSource? uciOk, readyOk;
        PendingSearch? search;
        lock (stateLock)
        {
            hasExited = true;
            (uciOk, pendingUciOk) = (pendingUciOk, null);
            (readyOk, pendingReadyOk) = (pendingReadyOk, null);
            (search, pendingSearch) = (pendingSearch, null);
        }
        uciOk?.TrySetException(error);
        readyOk?.TrySetException(error);
        search?.Completion.TrySetException(error);
        exited.TrySetResult();

        try
        {
            Exited?.Invoke(exitCode);
        }
        catch (Exception)
        {
            // A misbehaving subscriber must not take the reader down with it.
        }
    }

    private Task IsReadyCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs;
        lock (stateLock)
        {
            ThrowIfExited();
            tcs = pendingReadyOk ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Send("isready");
        return WaitForReplyAsync(tcs.Task, timeout, "isready", "readyok", cancellationToken);
    }

    private static async Task WaitForReplyAsync(Task reply, TimeSpan timeout, string command, string expected, CancellationToken cancellationToken)
    {
        try
        {
            await reply.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new UciEngineException(
                $"The engine did not answer '{command}' with '{expected}' within {timeout.TotalSeconds:0} seconds.");
        }
    }

    private void Send(string line)
    {
        var p = process ?? throw new InvalidOperationException("The engine has not been started.");
        lock (writeLock)
        {
            ThrowIfExited();
            RaiseLog(EngineLogDirection.ToEngine, line);
            try
            {
                p.StandardInput.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                throw new UciEngineException("The engine is not running.", ex);
            }
        }
    }

    private void TrySend(string line)
    {
        try
        {
            Send(line);
        }
        catch (Exception ex) when (ex is UciEngineException or InvalidOperationException)
        {
        }
    }

    private void ThrowIfExited()
    {
        if (hasExited)
            throw new UciEngineException("The engine is not running.");
    }

    private void RaiseLog(EngineLogDirection direction, string text)
    {
        try
        {
            Log?.Invoke(new EngineLogEntry(direction, text));
        }
        catch (Exception)
        {
            // Logging must never break the protocol.
        }
    }

    private sealed class PendingSearch(IProgress<UciInfo>? progress)
    {
        public TaskCompletionSource<UciBestMove> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IProgress<UciInfo>? Progress { get; } = progress;

        /// <summary>Started when "go" has been sent; read when "bestmove" arrives.</summary>
        public Stopwatch Clock { get; } = new();
    }
}
