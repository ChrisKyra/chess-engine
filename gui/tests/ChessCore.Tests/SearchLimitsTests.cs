using ChessCore.Uci;

namespace ChessCore.Tests;

public class SearchLimitsTests
{
    [Fact]
    public void NoLimitsIsAPlainGoSoTheEngineDecides()
    {
        Assert.Equal("go", new SearchLimits().ToGoCommand());
    }

    [Fact]
    public void InfiniteSearchForAnalysis()
    {
        Assert.Equal("go infinite", new SearchLimits { Infinite = true }.ToGoCommand());
    }
}
