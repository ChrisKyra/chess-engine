using System.Diagnostics;
using ChessCore;
using ChessCore.Uci;

namespace ChessGui;

/// <summary>One match game in progress, with its own pair of engine processes.</summary>
/// <remarks>
/// A match runs several of these at once. They need their own processes because one
/// engine process searches one position at a time, so the engines loaded on the Engines
/// tab (which play the game on the board) cannot be shared between parallel games.
/// <para>
/// Everything here runs on the UI thread. That is not a bottleneck: all this code does
/// is wait for engine output, and the actual thinking happens inside the engine
/// processes, which is where the parallelism comes from.
/// </para>
/// </remarks>
internal sealed class MatchWorker(
    int index,
    string engine1Path, string? engine1Arguments,
    string engine2Path, string? engine2Arguments) : IAsyncDisposable
{
    private UciEngine? engine1;
    private UciEngine? engine2;

    /// <summary>Runs while an engine thinks, so the board can show its clock counting down.</summary>
    private readonly Stopwatch moveTimer = new();

    /// <summary>Worker 0's game is the one shown on the board; the rest run out of sight.</summary>
    public int Index { get; } = index;

    public Game Game { get; private set; } = new();

    /// <summary>Which colour Engine 1 has in the game being played.</summary>
    public bool Engine1White { get; private set; }

    public string Opening { get; private set; } = "";

    /// <summary>The clocks of the game being played (they only run with a clock time control).</summary>
    public GameClock? Clock { get; private set; }

    /// <summary>
    /// What is left on <paramref name="color"/>'s clock right now, counting the move it is
    /// thinking about; null when the match has no clock.
    /// </summary>
    public long? RemainingMs(PieceColor color)
    {
        if (Clock is not { TimeControl.HasClock: true } clock)
            return null;
        long left = clock.RemainingMs(color);
        if (moveTimer.IsRunning && !Game.Result.IsOver && Game.Position.SideToMove == color)
            left -= moveTimer.ElapsedMilliseconds;
        return Math.Max(0, left);
    }

    /// <summary>Set when the last game ended because an engine returned an illegal move.</summary>
    public string? IllegalMove { get; private set; }

    /// <summary>Starts this worker's two engines and gives them the saved options.</summary>
    public async Task StartEnginesAsync(Func<UciEngine, Task> applySavedOptions)
    {
        engine1 = new UciEngine(engine1Path, engine1Arguments);
        await engine1.StartAsync();
        await applySavedOptions(engine1);

        engine2 = new UciEngine(engine2Path, engine2Arguments);
        await engine2.StartAsync();
        await applySavedOptions(engine2);
    }

    /// <summary>
    /// Plays game number <paramref name="index"/> of the match from its opening to its end
    /// and returns the result. <paramref name="onChanged"/> is called after every move, so
    /// the board can follow the game it is showing.
    /// </summary>
    public async Task<MatchGameRecord> PlayGameAsync(
        MatchRun match, int index, Action<MatchWorker> onChanged, CancellationToken token)
    {
        // Each opening is played twice in a row: Engine 1 has White in the first of the pair.
        var (openingName, openingMoves) = Openings.ForMatchGame(index / 2, match.RandomPlies);
        Engine1White = index % 2 == 0;
        Opening = openingName;
        IllegalMove = null;
        Clock = new GameClock(match.TimeControl);
        Game = new Game(Position.Start);
        foreach (var move in openingMoves)
            Game.Play(move);
        onChanged(this);

        await engine1!.NewGameAsync(token);
        await engine2!.NewGameAsync(token);

        while (!Game.Result.IsOver)
        {
            token.ThrowIfCancellationRequested();

            var position = Game.Position;
            bool engine1ToMove = (position.SideToMove == PieceColor.White) == Engine1White;
            var engine = engine1ToMove ? engine1 : engine2;

            // The match's time control decides the "go" (a plain "go" lets the engine decide).
            // The recorder keeps the last node count and time it reports, for the speed figures.
            var recorder = new SearchInfoRecorder();
            moveTimer.Restart();
            UciBestMove best;
            try
            {
                best = await engine.SearchAsync(Game.ToUciPositionCommand(), Clock.Limits(), recorder, token);
            }
            finally
            {
                moveTimer.Reset();
            }
            (engine1ToMove ? match.Engine1Speed : match.Engine2Speed).Add(recorder);

            // The time from "go" to "bestmove" comes off the clock; past zero the flag falls.
            if (!Clock.Charge(position.SideToMove, best.Elapsed))
            {
                Game.Adjudicate(GameClock.TimeoutResult(position, position.SideToMove));
                break;
            }

            if (position.FindUciMove(best.Move) is not { } move)
            {
                // An illegal move loses the game; the rest of the match carries on.
                Game.Adjudicate(GameResult.Win(position.SideToMove.Opposite(), GameEndReason.IllegalMove));
                IllegalMove = $"{engine.Name} answered 'bestmove {best.Move}' in game {index + 1}, " +
                              $"which is not a legal move there. FEN: {position.ToFen()}";
                break;
            }

            Game.Play(move);
            onChanged(this);
        }

        onChanged(this);
        return new MatchGameRecord(index + 1, Engine1White, Game.Result, Opening, Game.PlyCount);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var engine in new[] { engine1, engine2 })
        {
            if (engine is not null)
                await engine.DisposeAsync();
        }
        engine1 = null;
        engine2 = null;
    }
}
