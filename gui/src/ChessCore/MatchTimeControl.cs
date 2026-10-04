using System.Globalization;
using ChessCore.Uci;

namespace ChessCore;

public enum TimeControlKind
{
    /// <summary>A plain "go": each engine decides for itself how long to think.</summary>
    EngineDecides,

    /// <summary>"go movetime N": the same fixed time for every move.</summary>
    MoveTime,

    /// <summary>A chess clock per side with an increment: "go wtime .. btime .. winc .. binc ..".</summary>
    Clock,

    /// <summary>"go nodes N": a fixed amount of search per move, independent of the CPU.</summary>
    Nodes,
}

/// <summary>How long the engines in a match may think, and the "go" command that tells them.</summary>
public sealed record MatchTimeControl
{
    public TimeControlKind Kind { get; init; } = TimeControlKind.Clock;

    /// <summary>For <see cref="TimeControlKind.MoveTime"/>.</summary>
    public int MoveTimeMs { get; init; } = 500;

    /// <summary>Starting time on each clock, for <see cref="TimeControlKind.Clock"/>.</summary>
    public long BaseMs { get; init; } = 10_000;

    /// <summary>Added to a side's clock after each of its moves, for <see cref="TimeControlKind.Clock"/>.</summary>
    public long IncrementMs { get; init; } = 100;

    /// <summary>For <see cref="TimeControlKind.Nodes"/>.</summary>
    public long Nodes { get; init; } = 100_000;

    public bool HasClock => Kind == TimeControlKind.Clock;

    /// <summary>The "go" limits for the side to move, given what is left on both clocks.</summary>
    public SearchLimits ToLimits(long whiteRemainingMs, long blackRemainingMs) => Kind switch
    {
        TimeControlKind.MoveTime => new SearchLimits { MoveTimeMs = MoveTimeMs },
        TimeControlKind.Nodes => new SearchLimits { Nodes = Nodes },
        TimeControlKind.Clock => new SearchLimits
        {
            WhiteTimeMs = Math.Max(0, whiteRemainingMs),
            BlackTimeMs = Math.Max(0, blackRemainingMs),
            WhiteIncrementMs = IncrementMs,
            BlackIncrementMs = IncrementMs,
        },
        _ => new SearchLimits(),
    };

    /// <summary>"10+0.1", "500 ms per move", "100k nodes per move", or "engine decides".</summary>
    public string Describe() => Kind switch
    {
        TimeControlKind.MoveTime => $"{MoveTimeMs.ToString(CultureInfo.InvariantCulture)} ms per move",
        TimeControlKind.Clock => $"{Seconds(BaseMs)}+{Seconds(IncrementMs)}",
        TimeControlKind.Nodes => $"{Uci.SearchSpeed.Format(Nodes)} nodes per move",
        _ => "engine decides",
    };

    /// <summary>The PGN TimeControl tag ("10+0.1" in seconds), or null when there is no clock.</summary>
    public string? PgnTag => Kind == TimeControlKind.Clock ? $"{Seconds(BaseMs)}+{Seconds(IncrementMs)}" : null;

    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// The two clocks of one game. A side's thinking time is taken off its clock when it
/// moves and the increment added; a side whose time runs out loses, unless the opponent
/// has nothing left to mate with, which is a draw.
/// </summary>
public sealed class GameClock(MatchTimeControl timeControl)
{
    private readonly long[] remaining = [timeControl.BaseMs, timeControl.BaseMs];

    public MatchTimeControl TimeControl { get; } = timeControl;

    public long RemainingMs(PieceColor color) => remaining[(int)color];

    /// <summary>The "go" limits for the next search.</summary>
    public SearchLimits Limits() => TimeControl.ToLimits(remaining[0], remaining[1]);

    /// <summary>
    /// Charges <paramref name="color"/> for a move that took <paramref name="elapsed"/>.
    /// Returns false when that took longer than it had: the flag fell.
    /// </summary>
    public bool Charge(PieceColor color, TimeSpan elapsed)
    {
        if (!TimeControl.HasClock)
            return true;
        long left = remaining[(int)color] - (long)elapsed.TotalMilliseconds;
        if (left < 0)
        {
            remaining[(int)color] = 0;
            return false;
        }
        remaining[(int)color] = left + TimeControl.IncrementMs;
        return true;
    }

    /// <summary>The result when <paramref name="flagged"/> runs out of time in <paramref name="position"/>.</summary>
    public static GameResult TimeoutResult(Position position, PieceColor flagged) =>
        position.HasMatingMaterial(flagged.Opposite())
            ? GameResult.Win(flagged.Opposite(), GameEndReason.Timeout)
            : GameResult.Drawn(GameEndReason.TimeoutVsInsufficientMaterial);
}
