using ChessCore;
using ChessCore.Uci;

namespace ChessGui;

/// <summary>One finished match game.</summary>
public sealed record MatchGameRecord(int Number, bool Engine1White, GameResult Result, string Opening, int Plies)
{
    /// <summary>1 if Engine 1 won, 0 if it lost, 0.5 for a draw.</summary>
    public double Engine1Points => Result.Outcome switch
    {
        GameOutcome.WhiteWins => Engine1White ? 1 : 0,
        GameOutcome.BlackWins => Engine1White ? 0 : 1,
        _ => 0.5,
    };
}

/// <summary>
/// A series of games between Engine 1 and Engine 2. Colours alternate every game and
/// each opening is played by both engines as White, so neither gets easier positions.
/// </summary>
public sealed class MatchRun(int totalGames, int randomPlies, string engine1Name, string engine2Name, string pgnPath, int parallelGames = 1)
{
    private readonly List<MatchGameRecord> games = [];

    /// <summary>Engine 1's points in the first finished game of each pair still waiting for its partner.</summary>
    private readonly Dictionary<int, double> unpairedPoints = [];

    /// <summary>The next game number nobody has started yet.</summary>
    private int started;

    public int TotalGames { get; } = totalGames;

    public int RandomPlies { get; } = randomPlies;

    /// <summary>
    /// Shuffles the order of the openings (see <see cref="Openings.ForMatchGame"/>). Each
    /// match draws a new one, so matches start from different openings.
    /// </summary>
    public int OpeningSeed { get; } = Random.Shared.Next(1, int.MaxValue);

    /// <summary>
    /// How many games are played at the same time, each by its own pair of engine
    /// processes. Only the first of them is shown on the board.
    /// </summary>
    public int ParallelGames { get; } = Math.Max(1, parallelGames);

    public int GamesStarted => started;

    public string Engine1Name { get; } = engine1Name;

    public string Engine2Name { get; } = engine2Name;

    /// <summary>Every finished game is appended here straight away.</summary>
    public string PgnPath { get; } = pgnPath;

    public IReadOnlyList<MatchGameRecord> Games => games;

    public int GamesPlayed => games.Count;

    public bool IsFinished => GamesPlayed >= TotalGames;

    public bool IsRunning { get; internal set; }

    /// <summary>
    /// Paused: each game stops before its next move and waits, with its clocks stopped,
    /// until the match is resumed. Nothing is lost: the games carry on where they were.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>Completed while the match plays; a new, pending one while it is paused.</summary>
    private TaskCompletionSource resumed = Released();

    private static TaskCompletionSource Released()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }

    /// <summary>Stops every game before its next move. A move being thought about is finished first.</summary>
    internal void Pause()
    {
        if (IsPaused)
            return;
        IsPaused = true;
        resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Lets the games carry on.</summary>
    internal void Resume()
    {
        if (!IsPaused)
            return;
        IsPaused = false;
        resumed.TrySetResult();
    }

    /// <summary>Returns at once while the match plays; while it is paused, once it is resumed (or cancelled).</summary>
    internal Task WaitWhilePausedAsync(CancellationToken token) => resumed.Task.WaitAsync(token);

    /// <summary>Engine 1's results; Engine 2's are the mirror image.</summary>
    public MatchScore Engine1 { get; } = new();

    public MatchScore Engine1AsWhite { get; } = new();

    public MatchScore Engine1AsBlack { get; } = new();

    /// <summary>How the games ended (checkmate, repetition, fifty-move rule, ...) and how long they lasted.</summary>
    public GameEndTally Endings { get; } = new();

    /// <summary>How fast Engine 1 searched, over every move it played in this match (all parallel games).</summary>
    public SearchSpeed Engine1Speed { get; } = new();

    /// <summary>How fast Engine 2 searched, over every move it played in this match.</summary>
    public SearchSpeed Engine2Speed { get; } = new();

    /// <summary>How long the engines may think, and whether they play on a clock.</summary>
    public MatchTimeControl TimeControl { get; init; } = new();

    /// <summary>The sequential test run on the results, or null when the match just plays its games.</summary>
    public Sprt? Sprt { get; init; }

    /// <summary>Whether the match ends as soon as <see cref="Sprt"/> reaches a decision.</summary>
    public bool StopOnSprtDecision { get; init; }

    /// <summary>Each engine's "Threads" option for this match (null if it has none), for the speed table.</summary>
    public string? Engine1Threads { get; init; }

    public string? Engine2Threads { get; init; }

    /// <summary>Why the match ended before playing every game, e.g. an SPRT decision; null otherwise.</summary>
    public string? EndReason { get; internal set; }

    public string CurrentOpening { get; internal set; } = "";

    internal bool CurrentEngine1White { get; set; }

    /// <summary>The game being played for this match (players are assigned by the match only while it is on the board).</summary>
    internal Game? CurrentGame { get; set; }

    /// <summary>
    /// Claims the next game to play, or null when every game has been handed out.
    /// Games are numbered from 0; the number decides the opening and the colours, so
    /// each pair of games is the same opening played from both sides.
    /// </summary>
    /// <remarks>
    /// No locking: every caller runs on the UI thread, which is also where each game's
    /// moves are applied. The waiting happens inside the engine processes, not here.
    /// </remarks>
    internal int? TakeNextGame() => started < TotalGames ? started++ : null;

    internal void Add(MatchGameRecord record)
    {
        games.Add(record);
        Engine1.Add(record.Engine1Points);
        (record.Engine1White ? Engine1AsWhite : Engine1AsBlack).Add(record.Engine1Points);
        Endings.Add(record.Result, record.Plies);

        // Games 1 and 2 are the first opening from both sides, 3 and 4 the second, and so
        // on. With games in parallel either one of a pair can finish first.
        int pair = (record.Number - 1) / 2;
        if (unpairedPoints.Remove(pair, out var partner))
            Sprt?.AddPair(partner + record.Engine1Points);
        else
            unpairedPoints[pair] = record.Engine1Points;
    }
}
