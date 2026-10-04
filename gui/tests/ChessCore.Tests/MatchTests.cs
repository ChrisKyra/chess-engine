namespace ChessCore.Tests;

public class MatchTests
{
    [Fact]
    public void EveryOpeningIsLegalAndNamedOnce()
    {
        Assert.NotEmpty(Openings.All);
        foreach (var (_, moves) in Openings.All)
            Assert.Equal(moves.Split(' ').Length, Openings.Parse(moves).Count);
        Assert.Equal(Openings.All.Count, Openings.All.Select(o => o.Name).Distinct().Count());
    }

    [Fact]
    public void EveryOpeningEndsQuietlyWithWhiteToMoveInADifferentPosition()
    {
        var seen = new Dictionary<string, string>();
        foreach (var (name, uci) in Openings.All)
        {
            var position = Openings.Parse(uci).Aggregate(Position.Start, (p, m) => p.Play(m));
            Assert.True(position.SideToMove == PieceColor.White, $"{name} ends with Black to move");
            Assert.False(position.IsInCheck, $"{name} ends in check");
            var key = position.RepetitionKey();
            Assert.False(seen.ContainsKey(key), $"{name} reaches the same position as {seen.GetValueOrDefault(key)}");
            seen[key] = name;
        }
        Assert.True(Openings.All.Count >= 250);
    }

    [Fact]
    public void MatchOpeningsAreReproducibleAndLeaveTheGameGoing()
    {
        for (int pair = 0; pair < 120; pair++)
        {
            var first = Openings.ForMatchGame(pair, 4);
            var again = Openings.ForMatchGame(pair, 4);
            Assert.Equal(first.Moves, again.Moves);
            Assert.Equal(first.Name, again.Name);

            var game = new Game();
            foreach (var move in first.Moves)
                game.Play(move);
            Assert.False(game.Result.IsOver);
        }
    }

    [Fact]
    public void WithoutRandomMovesTheBookLineIsUsedAsIs()
    {
        var (name, moves) = Openings.ForMatchGame(0, 0);
        Assert.Equal(Openings.All[0].Name, name);
        Assert.Equal(Openings.Parse(Openings.All[0].Moves), moves);
    }

    [Fact]
    public void CountsResultsAndEstimatesElo()
    {
        var score = new MatchScore();
        foreach (var points in new[] { 1.0, 1.0, 0.5, 0.0, 1.0, 0.5 })
            score.Add(points);

        Assert.Equal((3, 2, 1, 6), (score.Wins, score.Draws, score.Losses, score.Games));
        Assert.Equal(4.0 / 6, score.Score, 6);

        var (elo, margin) = score.EloEstimate()!.Value;
        Assert.Equal(-400 * Math.Log10(1 / (4.0 / 6) - 1), elo, 6);   // about +120
        Assert.True(margin > 0);
    }

    [Fact]
    public void EvenScoreIsZeroElo()
    {
        var score = new MatchScore();
        score.Add(1);
        score.Add(0);
        score.Add(0.5);
        Assert.Equal(0, score.EloEstimate()!.Value.Elo, 6);
    }

    [Fact]
    public void EloIsUndefinedWithoutGamesOrForAPerfectScore()
    {
        var score = new MatchScore();
        Assert.Null(score.EloEstimate());
        score.Add(1);
        score.Add(1);
        Assert.Null(score.EloEstimate());
    }

    [Fact]
    public void TalliesHowGamesEnded()
    {
        var tally = new GameEndTally();
        Assert.Equal("", tally.Describe());

        tally.Add(GameResult.Win(PieceColor.White, GameEndReason.Checkmate), 81);
        tally.Add(GameResult.Drawn(GameEndReason.ThreefoldRepetition), 60);
        tally.Add(GameResult.Drawn(GameEndReason.FiftyMoveRule), 140);
        tally.Add(GameResult.Drawn(GameEndReason.FiftyMoveRule), 120);
        tally.Add(GameResult.Ongoing, 10);   // ignored

        Assert.Equal((4, 1, 3), (tally.Games, tally.Decisive, tally.Draws));
        Assert.Equal(2, tally.Count(GameEndReason.FiftyMoveRule));
        Assert.Equal(0, tally.Count(GameEndReason.Stalemate));
        Assert.Equal(40.5, tally.AverageMoves(false));
        Assert.Equal(320 / 2.0 / 3, tally.AverageMoves(true)!.Value, 6);

        var lines = tally.Describe().Split('\n');
        Assert.Equal("Decisive 1: 1 checkmate", lines[0]);
        Assert.Equal("Drawn 3: 1 repetition · 2 fifty-move rule", lines[1]);
        Assert.StartsWith("Average length 50 moves", lines[2]);
    }

    [Fact]
    public void ListsEveryWayGamesEndedWithTotals()
    {
        var tally = new GameEndTally();
        tally.Add(GameResult.Win(PieceColor.White, GameEndReason.Checkmate), 81);
        tally.Add(GameResult.Drawn(GameEndReason.FiftyMoveRule), 140);
        tally.Add(GameResult.Drawn(GameEndReason.Stalemate), 90);

        var rows = tally.Rows().Select(r => (r.Label, r.Count, r.IsTotal)).ToList();
        var expected = new List<(string, int, bool)>
        {
            ("Decisive", 1, true), ("Checkmate", 1, false),
            ("Drawn", 2, true), ("Repetition", 0, false), ("Fifty-move rule", 1, false), ("Stalemate", 1, false),
        };
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void AveragesSearchSpeedOverTotalNodesAndTime()
    {
        var speed = new ChessCore.Uci.SearchSpeed();
        Assert.Equal("—", speed.Describe());

        speed.Add(1_000_000, 500);           // 2M nodes/s
        speed.Add(3_000_000, 1500);          // 2M nodes/s
        speed.Add(null, 1000, 4_000_000);    // nps and time only: 4M nodes in 1 s
        speed.Add(500, 0);                   // no time: skipped
        speed.Add(null, null);               // nothing: skipped

        Assert.Equal(3, speed.Searches);
        Assert.Equal(8_000_000L, speed.Nodes);
        Assert.Equal(8_000_000 / 3.0, speed.NodesPerSecond!.Value, 3);
        Assert.Equal("2.67M", speed.Describe());
        Assert.Equal(8_000_000 / 3.0, speed.NodesPerMove!.Value, 3);
        Assert.Equal("2.67M", speed.DescribeNodesPerMove());
        Assert.Equal("845.2k", ChessCore.Uci.SearchSpeed.Format(845_200));
    }

    [Fact]
    public void RecorderKeepsTheLastCountsAnEngineReported()
    {
        var forwarded = new List<string>();
        var recorder = new ChessCore.Uci.SearchInfoRecorder(new ListProgress(forwarded));
        foreach (var line in new[]
        {
            "info depth 1 score cp 20 nodes 30 time 1 pv e2e4",
            "info depth 9 score cp 25 nodes 600000 nps 2000000 time 300 pv e2e4 e7e5",
            "info string thinking",
        })
            recorder.Report(ChessCore.Uci.UciInfo.Parse(line)!);

        Assert.Equal((600000L, 300L, 2000000L), (recorder.Nodes!.Value, recorder.TimeMs!.Value, recorder.NodesPerSecond!.Value));
        Assert.Equal(3, forwarded.Count);
    }

    private sealed class ListProgress(List<string> lines) : IProgress<ChessCore.Uci.UciInfo>
    {
        public void Report(ChessCore.Uci.UciInfo value) => lines.Add(value.Text ?? "info");
    }
}
