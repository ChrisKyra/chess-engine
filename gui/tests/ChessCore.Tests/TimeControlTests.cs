namespace ChessCore.Tests;

public class TimeControlTests
{
    [Fact]
    public void EachTimeControlSendsItsOwnGo()
    {
        var clock = new MatchTimeControl { Kind = TimeControlKind.Clock, BaseMs = 10_000, IncrementMs = 100 };
        Assert.Equal("go wtime 9500 btime 8000 winc 100 binc 100", clock.ToLimits(9500, 8000).ToGoCommand());
        Assert.Equal("go movetime 500", (clock with { Kind = TimeControlKind.MoveTime }).ToLimits(0, 0).ToGoCommand());
        Assert.Equal("go nodes 100000", (clock with { Kind = TimeControlKind.Nodes }).ToLimits(0, 0).ToGoCommand());
        Assert.Equal("go", (clock with { Kind = TimeControlKind.EngineDecides }).ToLimits(0, 0).ToGoCommand());
    }

    [Fact]
    public void DescribesTheTimeControlAndTagsPgnOnlyWithAClock()
    {
        var clock = new MatchTimeControl { Kind = TimeControlKind.Clock, BaseMs = 10_000, IncrementMs = 100 };
        Assert.Equal("10+0.1", clock.Describe());
        Assert.Equal("10+0.1", clock.PgnTag);
        Assert.Equal("500 ms per move", (clock with { Kind = TimeControlKind.MoveTime }).Describe());
        Assert.Null((clock with { Kind = TimeControlKind.MoveTime }).PgnTag);
    }

    [Fact]
    public void ClockChargesThinkingTimeAndAddsTheIncrement()
    {
        var clock = new GameClock(new MatchTimeControl { Kind = TimeControlKind.Clock, BaseMs = 1000, IncrementMs = 100 });
        Assert.True(clock.Charge(PieceColor.White, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(800, clock.RemainingMs(PieceColor.White));
        Assert.Equal(1000, clock.RemainingMs(PieceColor.Black));
        Assert.Equal("go wtime 800 btime 1000 winc 100 binc 100", clock.Limits().ToGoCommand());

        // Using exactly what is left is allowed; going past it is not.
        Assert.True(clock.Charge(PieceColor.White, TimeSpan.FromMilliseconds(800)));
        Assert.Equal(100, clock.RemainingMs(PieceColor.White));
        Assert.False(clock.Charge(PieceColor.White, TimeSpan.FromMilliseconds(101)));
        Assert.Equal(0, clock.RemainingMs(PieceColor.White));
    }

    [Fact]
    public void WithoutAClockNothingIsCharged()
    {
        var clock = new GameClock(new MatchTimeControl { Kind = TimeControlKind.MoveTime });
        Assert.True(clock.Charge(PieceColor.White, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void RunningOutOfTimeLosesUnlessTheOpponentCannotMate()
    {
        var rookUp = Position.FromFen("7k/8/8/8/8/8/8/R6K w - - 0 1");
        Assert.Equal(GameResult.Win(PieceColor.White, GameEndReason.Timeout), GameClock.TimeoutResult(rookUp, PieceColor.Black));
        Assert.Equal(GameResult.Drawn(GameEndReason.TimeoutVsInsufficientMaterial), GameClock.TimeoutResult(rookUp, PieceColor.White));
    }

    [Fact]
    public void TimePerMoveIsTotalTimeOverMoves()
    {
        var speed = new ChessCore.Uci.SearchSpeed();
        Assert.Equal("—", speed.DescribeTimePerMove());
        speed.Add(1_000_000, 300);
        speed.Add(2_000_000, 500);
        Assert.Equal(400, speed.MillisecondsPerMove!.Value, 6);
        Assert.Equal("400 ms", speed.DescribeTimePerMove());
        speed.Add(10_000_000, 2_000);
        Assert.Equal("933 ms", speed.DescribeTimePerMove());
        speed.Add(10_000_000, 3_200);
        Assert.Equal("1.50 s", speed.DescribeTimePerMove());
    }
}

public class SprtTests
{
    [Fact]
    public void BoundsFollowFromTheErrorRates()
    {
        var settings = new SprtSettings();
        Assert.Equal(-2.944, settings.LowerBound, 3);
        Assert.Equal(2.944, settings.UpperBound, 3);
    }

    [Fact]
    public void NoEvidenceMeansNoDecision()
    {
        var sprt = new Sprt(new SprtSettings());
        Assert.Equal(0, sprt.Llr);
        Assert.Equal(SprtDecision.Continue, sprt.Decision);

        // Every pair drawn: no spread, so still nothing to go on.
        for (int i = 0; i < 10; i++)
            sprt.AddPair(1);
        Assert.Equal(SprtDecision.Continue, sprt.Decision);
    }

    [Fact]
    public void AClearlyStrongerEngineAcceptsH1()
    {
        var sprt = new Sprt(new SprtSettings { Elo0 = 0, Elo1 = 10 });
        // About 60% per pair: roughly +70 Elo.
        for (int i = 0; i < 200 && sprt.Decision == SprtDecision.Continue; i++)
            sprt.AddPair(new[] { 2, 1.5, 1, 1, 0.5, 1.5, 1, 2, 0.5, 1 }[i % 10]);
        Assert.Equal(SprtDecision.H1Accepted, sprt.Decision);
        Assert.True(sprt.Pairs < 200);
    }

    [Fact]
    public void EqualEnginesAcceptH0()
    {
        var sprt = new Sprt(new SprtSettings { Elo0 = 0, Elo1 = 10 });
        for (int i = 0; i < 5000 && sprt.Decision == SprtDecision.Continue; i++)
            sprt.AddPair(new[] { 2, 0, 1, 1.5, 0.5, 1, 1, 1 }[i % 8]);
        Assert.Equal(SprtDecision.H0Accepted, sprt.Decision);
    }

    [Fact]
    public void LlrMatchesTheFormula()
    {
        // 10 pairs at 0 points, 20 at ½, 40 at 1, 20 at 1½, 10 at 2: mean exactly ½.
        var sprt = new Sprt(new SprtSettings { Elo0 = 0, Elo1 = 10 });
        foreach (var (points, count) in new[] { (0.0, 10), (0.5, 20), (1.0, 40), (1.5, 20), (2.0, 10) })
            for (int i = 0; i < count; i++)
                sprt.AddPair(points);

        double variance = (10 * 0.25 + 20 * 0.0625 + 20 * 0.0625 + 10 * 0.25) / 100;
        double s0 = 0.5, s1 = Sprt.ExpectedScore(10);
        Assert.Equal(100 * (s1 - s0) * (1 - s0 - s1) / (2 * variance), sprt.Llr, 9);
        Assert.Equal(new[] { 10, 20, 40, 20, 10 }, sprt.PairCounts);
    }
}
