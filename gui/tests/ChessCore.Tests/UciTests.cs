using ChessCore.Uci;

namespace ChessCore.Tests;

public class UciTests
{
    [Fact]
    public void ParsesInfoLine()
    {
        var info = UciInfo.Parse("info depth 13 seldepth 20 multipv 1 score cp -15 lowerbound nodes 963442 nps 4769514 hashfull 12 time 202 pv e7e5 g1f3 b8c6");
        Assert.NotNull(info);
        Assert.Equal(13, info.Depth);
        Assert.Equal(20, info.SelectiveDepth);
        Assert.Equal(-15, info.ScoreCentipawns);
        Assert.True(info.IsLowerBound);
        Assert.Equal(963442, info.Nodes);
        Assert.Equal(4769514, info.NodesPerSecond);
        Assert.Equal(202, info.TimeMs);
        Assert.Equal(new[] { "e7e5", "g1f3", "b8c6" }, info.Pv);
        Assert.Equal("+0.15", info.FormatScore(PieceColor.Black));
        Assert.Equal("-0.15", info.FormatScore(PieceColor.White));
    }

    [Fact]
    public void ParsesMateScoreAndInfoString()
    {
        var mate = UciInfo.Parse("info depth 5 score mate -3 pv a1a2");
        Assert.Equal(-3, mate!.ScoreMate);
        Assert.Equal("#-3", mate.FormatScore(PieceColor.White));
        Assert.Equal("#3", mate.FormatScore(PieceColor.Black));

        var text = UciInfo.Parse("info string hello   there world");
        Assert.Equal("hello there world", text!.Text);
        Assert.False(text.HasScore);

        Assert.Null(UciInfo.Parse("bestmove e2e4"));
    }

    [Theory]
    [InlineData("option name Hash type spin default 64 min 1 max 4096", "Hash", UciOptionType.Spin, "64", 1L, 4096L)]
    [InlineData("option name Clear Hash type button", "Clear Hash", UciOptionType.Button, null, null, null)]
    [InlineData("option name UCI_ShowWDL type check default false", "UCI_ShowWDL", UciOptionType.Check, "false", null, null)]
    [InlineData("option name NalimovPath type string default <empty>", "NalimovPath", UciOptionType.String, "", null, null)]
    public void ParsesOptions(string line, string name, UciOptionType type, string? defaultValue, long? min, long? max)
    {
        var option = UciOption.Parse(line);
        Assert.NotNull(option);
        Assert.Equal(name, option.Name);
        Assert.Equal(type, option.Type);
        Assert.Equal(defaultValue, option.Default);
        Assert.Equal(min, option.Min);
        Assert.Equal(max, option.Max);
    }

    [Fact]
    public void ParsesComboChoices()
    {
        var option = UciOption.Parse("option name Style type combo default Normal var Solid var Normal var Very Risky");
        Assert.Equal(new[] { "Solid", "Normal", "Very Risky" }, option!.Choices);
    }

    [Fact]
    public void BuildsGoCommand()
    {
        Assert.Equal("go movetime 1500", new SearchLimits { MoveTimeMs = 1500 }.ToGoCommand());
        Assert.Equal("go depth 12", new SearchLimits { Depth = 12 }.ToGoCommand());
        Assert.Equal(
            "go wtime 60000 btime 59000 winc 1000 binc 1000",
            new SearchLimits { WhiteTimeMs = 60000, BlackTimeMs = 59000, WhiteIncrementMs = 1000, BlackIncrementMs = 1000 }.ToGoCommand());
    }
}
