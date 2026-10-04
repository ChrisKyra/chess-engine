using ChessCore.Uci;

namespace ChessCore.Tests;

public class AdjudicatorTests
{
    private static UciInfo Cp(int cp) => new() { ScoreCentipawns = cp };

    // Plays `scores` (each from White's point of view) as alternating White/Black searches.
    private static Adjudicator Feed(params int[] whiteScores)
    {
        var adjudicator = new Adjudicator();
        for (int i = 0; i < whiteScores.Length; i++)
        {
            var mover = i % 2 == 0 ? PieceColor.White : PieceColor.Black;
            adjudicator.Record(mover, Cp(mover == PieceColor.White ? whiteScores[i] : -whiteScores[i]));
        }
        return adjudicator;
    }

    [Fact]
    public void BothEnginesAgreeingOnAWinEndsTheGame()
    {
        Assert.Null(Feed(1200, 1100, 1300, 1050, 1500).Verdict(40));
        Assert.Equal(GameResult.Win(PieceColor.White, GameEndReason.Adjudication), Feed(1200, 1100, 1300, 1050, 1500, 1400).Verdict(40));
        Assert.Equal(GameResult.Win(PieceColor.Black, GameEndReason.Adjudication), Feed(-1200, -1100, -1300, -1050, -1500, -1400).Verdict(40));
    }

    [Fact]
    public void OneEngineDisagreeingKeepsTheGameGoing()
    {
        Assert.Null(Feed(1200, 1100, 1300, 900, 1500, 1400).Verdict(40));
    }

    [Fact]
    public void MateScoresCountAsDecided()
    {
        var adjudicator = new Adjudicator();
        for (int i = 0; i < 6; i++)
            adjudicator.Record(i % 2 == 0 ? PieceColor.White : PieceColor.Black,
                               i % 2 == 0 ? new UciInfo { ScoreMate = 5 } : new UciInfo { ScoreMate = -5 });
        Assert.Equal(GameResult.Win(PieceColor.White, GameEndReason.Adjudication), adjudicator.Verdict(60));
    }

    [Fact]
    public void LevelScoresDrawOnlyLateEnough()
    {
        var level = Feed(5, -3, 0, 8, -10, 2, 0, 0, 1, -1);
        Assert.Null(level.Verdict(79));
        Assert.Equal(GameResult.Drawn(GameEndReason.Adjudication), level.Verdict(80));
        Assert.Null(Feed(5, -3, 0, 8, -10, 2, 0, 30, 1, -1).Verdict(100));
    }

    [Fact]
    public void ASearchWithoutAScoreBreaksTheRun()
    {
        var adjudicator = Feed(1200, 1100, 1300, 1050, 1500);
        adjudicator.Record(PieceColor.Black, null);
        Assert.Null(adjudicator.Verdict(40));
    }
}
