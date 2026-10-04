namespace ChessCore.Tests;

public class MaterialBalanceTests
{
    private static Game Play(params string[] uci)
    {
        var game = new Game();
        foreach (var text in uci)
        {
            Assert.True(Move.TryParseUci(text, out var parsed));
            game.Play(game.Position.FindUciMove(parsed.ToUci())!.Value);
        }
        return game;
    }

    [Fact]
    public void LevelAtTheStartWithNothingCaptured()
    {
        var game = new Game();
        var (byWhite, byBlack) = MaterialBalance.Captures(game);
        Assert.Empty(byWhite);
        Assert.Empty(byBlack);
        Assert.Equal(0, MaterialBalance.Balance(game.Position));
    }

    [Fact]
    public void CountsCapturesForTheSideThatMadeThem()
    {
        // 1.e4 d5 2.exd5 Qxd5 3.Nc3 Qxg2?? 4.Bxg2: White took a pawn and the queen, Black two pawns.
        var game = Play("e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5g2", "f1g2");
        var (byWhite, byBlack) = MaterialBalance.Captures(game);
        Assert.Equal([PieceType.Pawn, PieceType.Queen], byWhite.Select(p => p.Type));
        Assert.All(byWhite, p => Assert.Equal(PieceColor.Black, p.Color));
        Assert.Equal([PieceType.Pawn, PieceType.Pawn], byBlack.Select(p => p.Type));
        Assert.Equal(9 + 1 - 2, MaterialBalance.Balance(game.Position));
    }

    [Fact]
    public void EnPassantTakesAPawn()
    {
        var game = Play("e2e4", "a7a6", "e4e5", "d7d5", "e5d6");
        var (byWhite, _) = MaterialBalance.Captures(game);
        Assert.Equal([new Piece(PieceColor.Black, PieceType.Pawn)], byWhite);
        Assert.Equal(1, MaterialBalance.Balance(game.Position));
    }

    [Fact]
    public void PromotionCountsAsThePieceItBecomes()
    {
        var position = Position.FromFen("7k/P7/8/8/8/8/8/K7 w - - 0 1");
        var game = new Game(position);
        game.Play(position.FindUciMove("a7a8q")!.Value);
        Assert.Equal(9, MaterialBalance.Balance(game.Position));
    }
}
