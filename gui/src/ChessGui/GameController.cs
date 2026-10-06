using Avalonia.Threading;
using ChessCore;
using ChessCore.Uci;

namespace ChessGui;

/// <summary>
/// Owns the game, the engines and engine matches, and decides who acts next.
/// </summary>
/// <remarks>
/// Two engine slots run side by side. Each can play either colour (or both), and each
/// can analyse the current position whenever it isn't playing a move. Nothing moves and
/// no engine calculates until the game is started with <see cref="Start"/>;
/// <see cref="QuitAsync"/> ends the game and every calculation. Outside matches, how long
/// an engine thinks is decided by the engine itself: it is sent a plain "go". Matches use
/// their own time control. Everything except <see cref="EngineLog"/> happens on the UI thread.
/// </remarks>
public sealed class GameController : IAsyncDisposable
{
    /// <summary>How long an engine gets to answer "stop" before Quit restarts it.</summary>
    private static readonly TimeSpan StopGracePeriod = TimeSpan.FromMilliseconds(500);

    private readonly AppSettings settings;
    private int version;

    /// <summary>The games of a parallel match, each with its own engine processes. Empty otherwise.</summary>
    private readonly List<MatchWorker> workers = [];

    private CancellationTokenSource? matchCts;

    /// <summary>The match game the board is showing, kept after the match so its clocks stay readable.</summary>
    private MatchWorker? shownWorker;

    public GameController(AppSettings settings)
    {
        this.settings = settings;
        Slots = [new EngineSlot(0), new EngineSlot(1)];
    }

    /// <summary>Game, engine or status changed; the UI should refresh.</summary>
    public event Action? StateChanged;

    /// <summary>New search output arrived. Can fire many times per second.</summary>
    public event Action? AnalysisChanged;

    /// <summary>UCI traffic of either engine. Raised on a background thread.</summary>
    public event Action<EngineSlot, EngineLogEntry>? EngineLog;

    public AppSettings Settings => settings;

    public Game Game { get; private set; } = new();

    public IReadOnlyList<EngineSlot> Slots { get; }

    public EngineSlot? ThinkingSlot => Slots.FirstOrDefault(s => s.IsThinking);

    public bool IsEngineThinking => ThinkingSlot is not null;

    /// <summary>The game on the board has been started and not quit. Until then nothing moves or calculates.</summary>
    public bool IsStarted { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>The current or most recent engine match, if any.</summary>
    public MatchRun? Match { get; private set; }

    public bool IsMatchRunning => Match is { IsRunning: true };

    /// <summary>
    /// A match is running, or a started game isn't finished. Engines can't be swapped
    /// then, because an engine's program decides how it plays and thinks.
    /// </summary>
    public bool IsGameInProgress => IsMatchRunning || (IsStarted && !Game.Result.IsOver);

    public string? Message { get; private set; }

    public bool MessageIsError { get; private set; }

    /// <summary>
    /// The user may move for the side to move in a started game when that side is
    /// theirs, or belongs to an engine slot with nothing loaded.
    /// </summary>
    public bool CanHumanMove =>
        IsStarted && !Game.Result.IsOver && !IsEngineThinking && SlotFor(Game.Position.SideToMove)?.Engine is null;

    /// <summary>Who plays a colour: the match decides for its own games, the settings otherwise.</summary>
    public PlayerKind PlayerFor(PieceColor color)
    {
        if (Match is { } match && ReferenceEquals(match.CurrentGame, Game))
            return (color == PieceColor.White) == match.CurrentEngine1White ? PlayerKind.Engine1 : PlayerKind.Engine2;
        return color == PieceColor.White ? settings.WhitePlayer : settings.BlackPlayer;
    }

    public EngineSlot? SlotFor(PieceColor color) => PlayerFor(color) switch
    {
        PlayerKind.Engine1 => Slots[0],
        PlayerKind.Engine2 => Slots[1],
        _ => null,
    };

    public string PlayerName(PieceColor color) => SlotFor(color)?.Name ?? "Human";

    public EngineSettings SlotSettings(EngineSlot slot) => settings.Engines[slot.Index];

    // ----------------------------------------------------------------------
    // Engine lifetime
    // ----------------------------------------------------------------------

    public async Task LoadEngineAsync(EngineSlot slot, string path, string? arguments)
    {
        if (slot.IsStarting || RefuseDuringGame())
            return;
        await UnloadEngineAsync(slot);

        var engine = new UciEngine(path, arguments);
        engine.Log += entry => EngineLog?.Invoke(slot, entry);
        engine.Exited += code => Dispatcher.UIThread.Post(() => OnEngineExited(slot, engine, code));

        slot.IsStarting = true;
        ShowMessage($"Starting {Path.GetFileName(path)} as {slot.Label}…");
        try
        {
            await engine.StartAsync();
            await ApplySavedOptionsAsync(engine);
            await engine.NewGameAsync();
        }
        catch (Exception ex)
        {
            // Anything from a missing file to a non-UCI program lands here; show it and carry on.
            slot.IsStarting = false;
            await engine.DisposeAsync();
            ShowMessage($"{slot.Label}: {DescribeStartFailure(path, ex)}", error: true);
            return;
        }

        slot.IsStarting = false;
        slot.Engine = engine;
        var saved = SlotSettings(slot);
        saved.Path = path;
        saved.Arguments = arguments;
        settings.Save();
        ShowMessage($"{slot.Label}: loaded {engine.Name}" + (engine.Author is { } author ? $" by {author}" : ""));
        OnPositionChanged();   // in a started game it may be this engine's turn, or it may start analysing
    }

    public async Task UnloadEngineAsync(EngineSlot slot)
    {
        var engine = slot.Engine;
        if (engine is null || RefuseDuringGame())
            return;
        slot.Engine = null;
        StopSearch(slot);
        StopAnalysis(slot);
        slot.Info = null;
        slot.InfoPosition = null;
        Changed();
        await engine.DisposeAsync();
    }

    private bool RefuseDuringGame()
    {
        if (!IsGameInProgress)
            return false;
        ShowMessage(IsMatchRunning
            ? "Engines can't be changed during a match. Quit the match first."
            : "Engines can't be changed during a game. Quit the game first.", error: true);
        return true;
    }

    private void OnEngineExited(EngineSlot slot, UciEngine engine, int? exitCode)
    {
        if (slot.Engine != engine)
            return;   // we unloaded or restarted it ourselves
        slot.Engine = null;
        StopSearch(slot);
        StopAnalysis(slot);
        var code = exitCode is { } c ? $" (exit code {c})" : "";
        var text = $"{engine.Name} ({slot.Label}) stopped unexpectedly{code}. The UCI log may say why.";
        if (IsMatchRunning)
            StopMatch("Match stopped: " + text);
        else
            ShowMessage(text, error: true);
        _ = engine.DisposeAsync();
    }

    private static string DescribeStartFailure(string path, Exception ex)
    {
        // Gatekeeper kills unnotarized downloads on launch; the process just dies with SIGKILL.
        if (OperatingSystem.IsMacOS() && ex.Message.Contains("code 137", StringComparison.Ordinal))
        {
            return $"{ex.Message} macOS blocked it, which usually means it was downloaded from the internet " +
                   $"and hasn't been allowed yet. In Terminal run:  xattr -d com.apple.quarantine \"{path}\"  and load it again.";
        }
        return ex.Message;
    }

    public string? SavedOptionValue(EngineSlot slot, UciOption option) =>
        slot.Engine is { } engine
        && settings.EngineOptions.TryGetValue(engine.ExecutablePath, out var saved)
        && saved.TryGetValue(option.Name, out var value)
            ? value
            : null;

    public async Task SetEngineOptionAsync(EngineSlot slot, UciOption option, string? value)
    {
        if (slot.Engine is not { } engine)
            return;

        if (option.Type != UciOptionType.Button && value is not null)
        {
            if (!settings.EngineOptions.TryGetValue(engine.ExecutablePath, out var saved))
                settings.EngineOptions[engine.ExecutablePath] = saved = [];
            saved[option.Name] = value;
            settings.Save();
        }

        // An analysing engine only listens again once stopped, so pause its analysis around the change.
        StopAnalysis(slot);
        try
        {
            await engine.SetOptionAsync(option.Name, value);
        }
        catch (UciEngineException ex)
        {
            ShowMessage($"{slot.Label}: {ex.Message}", error: true);
        }
        UpdateAnalysis();
        Changed();
    }

    private async Task ApplySavedOptionsAsync(UciEngine engine)
    {
        if (!settings.EngineOptions.TryGetValue(engine.ExecutablePath, out var saved))
            return;
        foreach (var option in engine.Options)
        {
            if (option.Type != UciOptionType.Button && saved.TryGetValue(option.Name, out var value))
                await engine.SetOptionAsync(option.Name, value);
        }
    }

    public void SendRawCommand(EngineSlot slot, string command)
    {
        if (slot.Engine is not { } engine)
        {
            ShowMessage($"Load {slot.Label} first.", error: true);
            return;
        }
        try
        {
            engine.SendCommand(command);
        }
        catch (UciEngineException ex)
        {
            ShowMessage($"{slot.Label}: {ex.Message}", error: true);
        }
    }

    // ----------------------------------------------------------------------
    // Game flow
    // ----------------------------------------------------------------------

    /// <summary>Sets up a new game on the board. It doesn't begin until <see cref="Start"/>.</summary>
    public void NewGame(Position? start = null)
    {
        if (IsMatchRunning)
        {
            ShowMessage("Quit the match before setting up a new game.", error: true);
            return;
        }
        IsPaused = false;
        Message = null;
        StartGame(start ?? Position.Start);
    }

    /// <summary>
    /// Begins the game on the board: only now can moves be made and engines calculate.
    /// If the game on the board was quit or has finished, a fresh one starts from the same position.
    /// </summary>
    public void Start()
    {
        if (IsStarted || IsMatchRunning || Slots.Any(s => s.IsStarting))
            return;
        if (Game.PlyCount > 0 || Game.Result.IsOver)
            StartGame(Game.StartPosition);
        IsStarted = true;
        IsPaused = false;
        Message = null;
        OnPositionChanged();
    }

    /// <summary>
    /// Stops the game (or match) and every engine calculation. Each engine is sent "stop";
    /// one that is still busy shortly afterwards would keep calculating, so it is restarted.
    /// </summary>
    public async Task QuitAsync(string? reason = null, bool reasonIsError = true)
    {
        var match = Match is { IsRunning: true } running ? running : null;
        if (match is not null)
            match.IsRunning = false;
        IsStarted = false;
        IsPaused = false;
        foreach (var slot in Slots)
        {
            StopSearch(slot);
            StopAnalysis(slot);
        }
        await StopWorkersAsync();
        // The games waiting on a pause were cancelled with the workers; clear the flag.
        match?.Resume();

        var summary = reason
            ?? (match is not null ? $"Match stopped after {match.GamesPlayed} of {match.TotalGames} games." : "Game stopped.");
        ShowMessage(summary, error: reason is not null && reasonIsError);

        var restarted = await Task.WhenAll(Slots.Select(ForceIdleAsync));
        var names = Slots.Where((_, i) => restarted[i]).Select(s => s.Label).ToList();
        if (names.Count > 0)
            ShowMessage($"{summary} {string.Join(" and ", names)} kept calculating after \"stop\", so it was restarted.", error: reason is not null && reasonIsError);
    }

    /// <summary>Waits briefly for an engine to finish; if it doesn't, kills and reloads it. Returns whether it was restarted.</summary>
    private async Task<bool> ForceIdleAsync(EngineSlot slot)
    {
        if (slot.Engine is not { IsBusy: true } engine)
            return false;
        try
        {
            await engine.WaitForIdleAsync().WaitAsync(StopGracePeriod);
            return false;
        }
        catch (TimeoutException)
        {
        }
        if (slot.Engine != engine)
            return false;

        var path = engine.ExecutablePath;
        var arguments = string.IsNullOrEmpty(engine.Arguments) ? null : engine.Arguments;
        slot.Engine = null;
        slot.Info = null;
        slot.InfoPosition = null;
        engine.Kill();
        await engine.DisposeAsync();
        await LoadEngineAsync(slot, path, arguments);
        return true;
    }

    private void StartGame(Position start)
    {
        foreach (var slot in Slots)
        {
            StopSearch(slot);
            StopAnalysis(slot);
        }

        Game = new Game(start);
        IsStarted = false;   // waits for Start
        version++;

        foreach (var slot in Slots)
        {
            slot.Info = null;
            slot.InfoPosition = null;
            if (slot.Engine is { } engine)
                slot.Ready = SendNewGameAsync(slot, engine);
        }
        OnPositionChanged();
    }

    public void HumanMove(Move move)
    {
        if (CanHumanMove && Game.Position.IsLegal(move))
            ApplyMove(move);
    }

    /// <summary>
    /// Takes back moves until a human is to move again, so "undo" against an engine
    /// removes both the engine's reply and your move.
    /// </summary>
    public void Undo()
    {
        if (!IsStarted || Game.PlyCount == 0 || IsMatchRunning)
            return;

        foreach (var slot in Slots)
            StopSearch(slot);
        Game.Undo();

        bool humanPlaying = PlayerFor(PieceColor.White) == PlayerKind.Human || PlayerFor(PieceColor.Black) == PlayerKind.Human;
        if (humanPlaying)
        {
            while (Game.PlyCount > 0 && SlotFor(Game.Position.SideToMove)?.Engine is not null)
                Game.Undo();
        }
        else
        {
            IsPaused = true;   // otherwise the engines would immediately replay the move
        }

        version++;
        Message = null;
        OnPositionChanged();
    }

    /// <summary>
    /// Makes an engine move for whichever side is to move (that side's engine, or else
    /// any loaded engine), or tells the thinking engine to move now.
    /// </summary>
    public void EngineMoveNow()
    {
        // The board shows a match game, which the loaded engines must not move in.
        if (IsMatchRunning)
            return;
        if (ThinkingSlot is { } thinking)
        {
            SendRawCommand(thinking, "stop");
            return;
        }
        var slot = SlotFor(Game.Position.SideToMove) is { Engine: not null } own
            ? own
            : Slots.FirstOrDefault(s => s.Engine is not null);
        if (slot is not null)
            MaybeStartEngine(slot);
    }

    public void SetPaused(bool paused)
    {
        // Match games run on their own engine processes and clocks, which don't pause.
        if (IsPaused == paused || !IsStarted || IsMatchRunning)
            return;
        IsPaused = paused;
        if (paused)
        {
            foreach (var slot in Slots)
                StopSearch(slot);
        }
        OnPositionChanged();
    }

    public void SetPlayers(PlayerKind white, PlayerKind black)
    {
        if (IsMatchRunning || (settings.WhitePlayer == white && settings.BlackPlayer == black))
            return;
        settings.WhitePlayer = white;
        settings.BlackPlayer = black;
        settings.Save();

        if (ThinkingSlot is { } thinking && SlotFor(Game.Position.SideToMove) != thinking)
            StopSearch(thinking);
        OnPositionChanged();
    }

    public void SetAnalyse(EngineSlot slot, bool analyse)
    {
        SlotSettings(slot).Analyse = analyse;
        settings.Save();
        if (!analyse)
            StopAnalysis(slot);
        UpdateAnalysis();
        Changed();
    }

    public void ShowMessage(string? text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
        Changed();
    }

    public string ToPgn() =>
        Game.ToPgn(PlayerName(PieceColor.White), PlayerName(PieceColor.Black));

    private void ApplyMove(Move move)
    {
        Game.Play(move);
        version++;
        OnPositionChanged();
    }

    private void OnPositionChanged()
    {
        var result = Game.Result;
        if (result.IsOver)
        {
            foreach (var slot in Slots)
                StopSearch(slot);
        }
        UpdateAnalysis();
        Changed();
        MaybeStartEngine();
    }

    private void Changed() => StateChanged?.Invoke();

    // ----------------------------------------------------------------------
    // Engine matches
    // ----------------------------------------------------------------------

    /// <summary>
    /// Starts a match of <paramref name="games"/> games between Engine 1 and Engine 2 at
    /// <paramref name="timeControl"/>. <paramref name="parallelGames"/> games are played at
    /// the same time, each by its own pair of engine processes started from the loaded
    /// engines' programs, and the board follows the first of them. The results are tested
    /// with <paramref name="sprt"/> as they come in; with <paramref name="stopOnSprtDecision"/>
    /// the match ends as soon as the test decides, so the number of games is a maximum.
    /// </summary>
    public void StartMatch(int games, int randomPlies, int parallelGames, MatchTimeControl timeControl,
                           SprtSettings sprt, bool stopOnSprtDecision)
    {
        if (IsMatchRunning)
            return;
        if (Slots.Any(s => s.Engine is null))
        {
            ShowMessage("Load both Engine 1 and Engine 2 (Engines tab) before starting a match.", error: true);
            return;
        }

        // The loaded engines sit the match out; stop anything they are doing on the board.
        IsStarted = false;
        IsPaused = false;
        foreach (var slot in Slots)
        {
            StopSearch(slot);
            StopAnalysis(slot);
        }

        var folder = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "matches");
        var file = Path.Combine(folder, $"match-{DateTime.Now:yyyy-MM-dd-HHmmss}.pgn");
        var match = new MatchRun(Math.Max(1, games), Math.Max(0, randomPlies), Slots[0].Name, Slots[1].Name, file,
                                 Math.Max(1, parallelGames))
        {
            IsRunning = true,
            TimeControl = timeControl,
            Sprt = new Sprt(sprt),
            StopOnSprtDecision = stopOnSprtDecision,
            Engine1Threads = OptionValue(Slots[0].Engine!, "Threads"),
            Engine2Threads = OptionValue(Slots[1].Engine!, "Threads"),
        };
        Match = match;
        shownWorker = null;
        _ = RunMatchAsync(match);
    }

    /// <summary>The value a loaded engine uses for an option: the saved one, else its default; null if it has no such option.</summary>
    public string? EngineOptionValue(EngineSlot slot, string name) =>
        slot.Engine is { } engine ? OptionValue(engine, name) : null;

    private string? OptionValue(UciEngine engine, string name)
    {
        var option = engine.Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (option is null)
            return null;
        return settings.EngineOptions.TryGetValue(engine.ExecutablePath, out var saved)
               && saved.TryGetValue(option.Name, out var value)
            ? value
            : option.Default;
    }

    /// <summary>
    /// Runs a match. Each game gets its own two engine processes, started from the same
    /// programs as the loaded engines and given the same options; the loaded engines
    /// themselves stay idle.
    /// </summary>
    private async Task RunMatchAsync(MatchRun match)
    {
        var first = Slots[0].Engine!;
        var second = Slots[1].Engine!;
        string engine1Path = first.ExecutablePath;
        string? engine1Args = string.IsNullOrEmpty(first.Arguments) ? null : first.Arguments;
        string engine2Path = second.ExecutablePath;
        string? engine2Args = string.IsNullOrEmpty(second.Arguments) ? null : second.Arguments;

        matchCts = new CancellationTokenSource();
        var token = matchCts.Token;
        for (int i = 0; i < match.ParallelGames; i++)
            workers.Add(new MatchWorker(i, engine1Path, engine1Args, engine2Path, engine2Args));

        IsStarted = true;
        ShowMessage($"Starting {match.ParallelGames * 2} engine processes for {match.ParallelGames} " +
                    (match.ParallelGames == 1 ? "game at a time…" : "games at once…"));
        Changed();

        try
        {
            await Task.WhenAll(workers.Select(w => w.StartEnginesAsync(ApplySavedOptionsAsync)));
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not start the engines for the match: {ex.Message}", error: true);
            match.IsRunning = false;
            IsStarted = false;
            await StopWorkersAsync();
            Changed();
            return;
        }

        ShowMessage($"Match started: {match.Engine1Name} vs {match.Engine2Name}, {match.TotalGames} games at " +
                    $"{match.TimeControl.Describe()}, {match.ParallelGames} at a time.");

        // Each worker takes the next game whenever it finishes one, so a game that
        // ends early doesn't leave a process idle.
        await Task.WhenAll(workers.Select(w => RunWorkerAsync(match, w, token)).ToList());

        IsStarted = false;
        await StopWorkersAsync();
        Changed();
    }

    /// <summary>Plays game after game until the match is finished or stopped.</summary>
    private async Task RunWorkerAsync(MatchRun match, MatchWorker worker, CancellationToken token)
    {
        while (match.IsRunning && !token.IsCancellationRequested)
        {
            if (match.TakeNextGame() is not { } index)
                return;

            try
            {
                var record = await worker.PlayGameAsync(match, index, OnWorkerChanged, token);
                if (!match.IsRunning)
                    return;
                RecordWorkerGame(match, worker, record);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is UciEngineException or ObjectDisposedException)
            {
                if (match.IsRunning)
                    StopMatch($"Match stopped: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>The board follows the first game; the others are only counted.</summary>
    private void OnWorkerChanged(MatchWorker worker)
    {
        if (worker.Index != 0 || Match is not { IsRunning: true } match)
            return;
        shownWorker = worker;
        Game = worker.Game;
        match.CurrentGame = worker.Game;
        match.CurrentEngine1White = worker.Engine1White;
        match.CurrentOpening = worker.Opening;
        Changed();
    }

    /// <summary>
    /// What is left on <paramref name="color"/>'s clock in the match game on the board,
    /// counting down while it thinks; null when the board shows no game played on a clock.
    /// </summary>
    public long? MatchClockMs(PieceColor color) =>
        shownWorker is { } worker && ReferenceEquals(worker.Game, Game) ? worker.RemainingMs(color) : null;

    private void RecordWorkerGame(MatchRun match, MatchWorker worker, MatchGameRecord record)
    {
        match.Add(record);
        SaveMatchGame(match, record, worker.Game);
        if (worker.IllegalMove is { } problem)
            ShowMessage(problem, error: true);

        if (match.IsFinished)
        {
            match.IsRunning = false;
            var score = match.Engine1;
            ShowMessage($"Match finished. {match.Engine1Name} (Engine 1): {score.Wins} wins, {score.Losses} losses, {score.Draws} draws.");
        }
        else if (match.StopOnSprtDecision && match.Sprt is { } sprt && sprt.Decision != SprtDecision.Continue)
        {
            // The games still being played are abandoned: the test has its answer.
            var elo1 = sprt.Settings.Elo1.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            match.EndReason = sprt.Decision == SprtDecision.H1Accepted
                ? $"SPRT accepted H1: Engine 1 is at least {elo1} Elo stronger"
                : $"SPRT accepted H0: Engine 1 is not {elo1} Elo stronger";
            _ = QuitAsync($"Match stopped after {match.GamesPlayed} games. {match.EndReason} ({sprt.DescribeLlr()}).",
                          reasonIsError: false);
        }
        Changed();
    }

    /// <summary>Cancels the match games and shuts their engine processes down.</summary>
    private async Task StopWorkersAsync()
    {
        if (workers.Count == 0)
            return;

        matchCts?.Cancel();
        var running = workers.ToList();
        workers.Clear();
        await Task.WhenAll(running.Select(w => w.DisposeAsync().AsTask()));
        matchCts?.Dispose();
        matchCts = null;
    }

    /// <summary>
    /// Pauses or resumes the running match. Pausing lets every engine finish the move it
    /// is thinking about, then holds each game before its next move, clocks stopped, until
    /// it is resumed; the games, results and engine processes are all kept.
    /// </summary>
    public void SetMatchPaused(bool paused)
    {
        if (Match is not { IsRunning: true } match || match.IsPaused == paused)
            return;
        if (paused)
            match.Pause();
        else
            match.Resume();
        ShowMessage(paused
            ? $"Match paused after {match.GamesPlayed} of {match.TotalGames} games. The engines finish the move they are thinking about, then wait."
            : "Match resumed.");
        Changed();
    }

    /// <summary>Ends the match like Quit does; the results so far are kept.</summary>
    public void StopMatch(string? reason = null)
    {
        if (IsMatchRunning)
            _ = QuitAsync(reason);
    }

    /// <summary>
    /// Appends one finished game to the match's PGN file. Games can finish in any order
    /// when several are running, so each one names its own players from the record.
    /// </summary>
    private void SaveMatchGame(MatchRun match, MatchGameRecord record, Game game)
    {
        var (white, black) = record.Engine1White
            ? (match.Engine1Name, match.Engine2Name)
            : (match.Engine2Name, match.Engine1Name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(match.PgnPath)!);
            var pgn = game.ToPgn(
                white,
                black,
                eventName: $"{match.Engine1Name} vs {match.Engine2Name}",
                round: record.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                timeControl: match.TimeControl.PgnTag);
            File.AppendAllText(match.PgnPath, pgn + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Could not save match game {record.Number}: {ex.Message}", error: true);
        }
    }

    // ----------------------------------------------------------------------
    // Playing moves
    // ----------------------------------------------------------------------

    /// <summary>Starts the side to move's engine, or <paramref name="forced"/> regardless of whose turn it is.</summary>
    private void MaybeStartEngine(EngineSlot? forced = null)
    {
        if (!IsStarted || IsEngineThinking || Game.Result.IsOver)
            return;
        var slot = forced ?? SlotFor(Game.Position.SideToMove);
        if (slot?.Engine is not { } engine || slot.IsStarting)
            return;
        if (forced is null && IsPaused)
            return;

        StopAnalysis(slot);
        _ = RunSearchAsync(slot, engine);
    }

    private async Task RunSearchAsync(EngineSlot slot, UciEngine engine)
    {
        using var cts = new CancellationTokenSource();
        slot.SearchCts = cts;
        int startVersion = version;
        var position = Game.Position;
        var positionCommand = Game.ToUciPositionCommand();
        slot.Info = null;
        slot.InfoPosition = position;
        Changed();

        // Progress<T> posts back to the UI thread it was created on.
        var progress = new Progress<UciInfo>(info =>
        {
            if (slot.SearchCts != cts || info.MultiPv is > 1)
                return;
            slot.Info = Merge(slot.Info, info);
            AnalysisChanged?.Invoke();
        });

        try
        {
            await slot.Ready;
            // Wait out any analysis or abandoned search this engine is still finishing.
            await engine.WaitForIdleAsync(cts.Token);
            if (slot.SearchCts != cts)
                return;

            // A plain "go": the engine itself decides how long to think.
            var best = await engine.SearchAsync(positionCommand, new SearchLimits(), progress, cts.Token);
            if (slot.SearchCts != cts || version != startVersion || slot.Engine != engine)
                return;
            slot.SearchCts = null;

            if (position.FindUciMove(best.Move) is not { } move)
            {
                var text = $"{engine.Name} ({slot.Label}) answered 'bestmove {best.Move}', which is not a legal move here. FEN: {position.ToFen()}";
                IsPaused = true;
                ShowMessage(text + " Engines paused.", error: true);
                return;
            }
            ApplyMove(move);
        }
        catch (OperationCanceledException)
        {
        }
        catch (UciEngineException ex)
        {
            if (slot.Engine == engine && !engine.HasExited)
                ShowMessage($"{slot.Label}: {ex.Message}", error: true);
        }
        finally
        {
            if (slot.SearchCts == cts)
            {
                slot.SearchCts = null;
                UpdateAnalysis();
                Changed();
            }
        }
    }

    private async Task SendNewGameAsync(EngineSlot slot, UciEngine engine)
    {
        try
        {
            await engine.NewGameAsync();
        }
        catch (UciEngineException ex)
        {
            if (!engine.HasExited)
                ShowMessage($"{slot.Label}: {ex.Message}", error: true);
        }
    }

    private static void StopSearch(EngineSlot slot)
    {
        var cts = slot.SearchCts;
        if (cts is null)
            return;
        slot.SearchCts = null;
        cts.Cancel();   // sends "stop"
    }

    // ----------------------------------------------------------------------
    // Background analysis
    // ----------------------------------------------------------------------

    /// <summary>
    /// Brings every slot's analysis in line with the current state: in a started game,
    /// engines with "Analyse" on search the current position whenever they aren't playing
    /// a move. Analysis is off during matches so neither engine thinks on the other's time.
    /// </summary>
    private void UpdateAnalysis()
    {
        var position = Game.Position;
        var toPlay = IsPaused ? null : SlotFor(position.SideToMove);

        foreach (var slot in Slots)
        {
            bool wanted = IsStarted
                && SlotSettings(slot).Analyse
                && slot.Engine is not null
                && !slot.IsStarting
                && !slot.IsThinking
                && slot != toPlay
                && !IsMatchRunning
                && !Game.Result.IsOver;

            if (slot.IsAnalysing && (!wanted || !ReferenceEquals(slot.InfoPosition, position)))
                StopAnalysis(slot);
            if (wanted && !slot.IsAnalysing)
                _ = RunAnalysisAsync(slot, slot.Engine!);
        }
    }

    private async Task RunAnalysisAsync(EngineSlot slot, UciEngine engine)
    {
        using var cts = new CancellationTokenSource();
        slot.AnalysisCts = cts;
        var position = Game.Position;
        var positionCommand = Game.ToUciPositionCommand();
        slot.Info = null;
        slot.InfoPosition = position;

        var progress = new Progress<UciInfo>(info =>
        {
            if (slot.AnalysisCts != cts || info.MultiPv is > 1)
                return;
            slot.Info = Merge(slot.Info, info);
            AnalysisChanged?.Invoke();
        });

        try
        {
            await slot.Ready;
            await engine.WaitForIdleAsync(cts.Token);
            if (slot.AnalysisCts != cts)
                return;
            // Runs until StopAnalysis sends "stop". An engine that ignores "stop" never finishes.
            await engine.SearchAsync(positionCommand, new SearchLimits { Infinite = true }, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (UciEngineException ex)
        {
            if (slot.Engine == engine && !engine.HasExited)
                ShowMessage($"{slot.Label}: {ex.Message}", error: true);
        }
        finally
        {
            // Not restarted here: an engine that returns from "go infinite" early would spin.
            if (slot.AnalysisCts == cts)
            {
                slot.AnalysisCts = null;
                Changed();
            }
        }
    }

    private static void StopAnalysis(EngineSlot slot)
    {
        var cts = slot.AnalysisCts;
        if (cts is null)
            return;
        slot.AnalysisCts = null;
        cts.Cancel();   // sends "stop"
    }

    /// <summary>
    /// Engines send full lines (depth, score, pv) and short progress lines (nodes, nps).
    /// Keep the last full line and refresh its counters from the short ones.
    /// </summary>
    private static UciInfo Merge(UciInfo? previous, UciInfo next)
    {
        if (previous is null)
            return next;
        if (next.Pv.Count > 0 || next.HasScore)
            return next.Pv.Count > 0 ? next : next with { Pv = previous.Pv };
        return previous with
        {
            Depth = next.Depth ?? previous.Depth,
            SelectiveDepth = next.SelectiveDepth ?? previous.SelectiveDepth,
            Nodes = next.Nodes ?? previous.Nodes,
            NodesPerSecond = next.NodesPerSecond ?? previous.NodesPerSecond,
            TimeMs = next.TimeMs ?? previous.TimeMs,
            HashFull = next.HashFull ?? previous.HashFull,
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Match is { } match)
            match.IsRunning = false;
        IsStarted = false;
        await StopWorkersAsync();
        var engines = new List<UciEngine>();
        foreach (var slot in Slots)
        {
            StopSearch(slot);
            StopAnalysis(slot);
            if (slot.Engine is { } engine)
                engines.Add(engine);
            slot.Engine = null;
        }
        await Task.WhenAll(engines.Select(e => e.DisposeAsync().AsTask()));
    }
}
