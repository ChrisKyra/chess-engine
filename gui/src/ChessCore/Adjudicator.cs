using ChessCore.Uci;

namespace ChessCore;

/// <summary>
/// Ends engine games early once both engines agree how they will end, the way testing
/// frameworks do, so a test does not spend its time on decided positions:
/// <list type="bullet">
/// <item>a <b>win</b> when the last <see cref="WinPlies"/> scores (both engines, alternating)
/// all give the same side at least <see cref="WinScore"/> centipawns;</item>
/// <item>a <b>draw</b> from ply <see cref="DrawFromPly"/> on, when the last
/// <see cref="DrawPlies"/> scores are all within <see cref="DrawScore"/> of zero.</item>
/// </list>
/// Scores are recorded as the engines report them (from the side to move's point of view)
/// and compared from White's. A mate score counts as a decided score.
/// </summary>
public sealed class Adjudicator
{
    private readonly List<int> whiteScores = [];

    public int WinScore { get; init; } = 1000;

    public int WinPlies { get; init; } = 6;

    public int DrawScore { get; init; } = 10;

    public int DrawPlies { get; init; } = 10;

    public int DrawFromPly { get; init; } = 80;

    /// <summary>Starts again for a new game.</summary>
    public void Reset() => whiteScores.Clear();

    /// <summary>
    /// Records the final score of a search by the side to move (<paramref name="mover"/>).
    /// A search that reported no score breaks the run: nothing is agreed about it.
    /// </summary>
    public void Record(PieceColor mover, UciInfo? lastInfo)
    {
        int? score = lastInfo switch
        {
            { ScoreMate: { } mate } => mate > 0 ? 30000 : -30000,
            { ScoreCentipawns: { } cp } => cp,
            _ => null,
        };
        whiteScores.Add(score is { } s ? (mover == PieceColor.White ? s : -s) : int.MinValue);
    }

    /// <summary>The adjudicated result after <paramref name="plyCount"/> plies, or null to play on.</summary>
    public GameResult? Verdict(int plyCount)
    {
        if (whiteScores.Count >= WinPlies)
        {
            var last = whiteScores.TakeLast(WinPlies).ToList();
            if (last.All(s => s != int.MinValue && s >= WinScore))
                return GameResult.Win(PieceColor.White, GameEndReason.Adjudication);
            if (last.All(s => s != int.MinValue && s <= -WinScore))
                return GameResult.Win(PieceColor.Black, GameEndReason.Adjudication);
        }
        if (plyCount >= DrawFromPly && whiteScores.Count >= DrawPlies
            && whiteScores.TakeLast(DrawPlies).All(s => s != int.MinValue && Math.Abs(s) <= DrawScore))
            return GameResult.Drawn(GameEndReason.Adjudication);
        return null;
    }
}
