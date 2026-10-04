namespace ChessCore.Tests;

public class RulesTests
{
    private static Game PlayUci(string moves, string fen = Position.StartFen)
    {
        var game = new Game(Position.FromFen(fen));
        foreach (var uci in moves.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            game.Play(game.Position.FindUciMove(uci) ?? throw new Exception($"illegal {uci}"));
        return game;
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2")]
    [InlineData("8/8/8/8/8/8/6k1/4K3 b - - 37 90")]
    public void FenRoundTrips(string fen)
    {
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }

    [Theory]
    [InlineData("", "needs at least")]
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1", "exactly one king")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN w KQkq - 0 1", "7 squares")]
    [InlineData("4k3/8/8/8/8/8/8/4K2P w - - 0 1", "first or last rank")]
    [InlineData("4k2R/8/8/8/8/8/8/4K3 w - - 0 1", "not to move is in check")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 x - - 0 1", "'w' or 'b'")]
    public void RejectsBadFen(string fen, string expectedMessage)
    {
        Assert.False(Position.TryParseFen(fen, out _, out var error));
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void FenDropsImpossibleCastlingAndEnPassant()
    {
        var p = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w KQkq e6 0 1");
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", p.ToFen());
    }

    [Fact]
    public void UciPositionCommandUsesStartposOrFen()
    {
        Assert.Equal("position startpos", new Game().ToUciPositionCommand());
        Assert.Equal("position startpos moves e2e4 e7e5", PlayUci("e2e4 e7e5").ToUciPositionCommand());

        const string fen = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";
        Assert.Equal($"position fen {fen} moves e2e4", PlayUci("e2e4", fen).ToUciPositionCommand());
    }

    [Theory]
    [InlineData(Position.StartFen, "g1f3", "Nf3")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "O-O-O")]
    [InlineData("4k3/8/8/8/8/8/4K3/R6R w - - 0 1", "a1d1", "Rad1")]
    [InlineData("4k3/8/8/8/8/8/4K3/R6R w - - 0 1", "h1f1", "Rhf1")]
    [InlineData("4k3/8/8/R7/8/8/8/R3K3 w - - 0 1", "a1a3", "R1a3")]
    [InlineData("4k3/8/8/8/8/2N3N1/8/2N1K3 w - - 0 1", "c3e2", "Nc3e2")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e5d6", "exd6")]
    [InlineData("8/3P4/8/8/8/8/k7/4K3 w - - 0 1", "d7d8q", "d8=Q")]
    [InlineData("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8n", "a8=N")]
    [InlineData("3k4/8/3K4/8/8/8/8/7R w - - 0 1", "h1h8", "Rh8#")]
    [InlineData("4k3/8/8/8/8/8/8/4K2R w - - 0 1", "h1h8", "Rh8+")]
    public void FormatsSan(string fen, string uci, string expected)
    {
        var position = Position.FromFen(fen);
        var move = position.FindUciMove(uci);
        Assert.NotNull(move);
        Assert.Equal(expected, San.Format(position, move.Value));
    }

    [Fact]
    public void FormatsPvLineAndFlagsIllegalTail()
    {
        var position = Position.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
        Assert.Equal("1... e5 2. Nf3 Nc6", San.FormatLine(position, ["e7e5", "g1f3", "b8c6"]));
        Assert.Equal("1... e5 (illegal: e5e4 g1f3)", San.FormatLine(position, ["e7e5", "e5e4", "g1f3"]));
    }

    [Fact]
    public void AcceptsKingTakesRookCastlingFromEngines()
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.Equal(new Move(4, 6), position.FindUciMove("e1h1"));
        Assert.Equal(new Move(4, 2), position.FindUciMove("e1a1"));
        Assert.Null(position.FindUciMove("e1e3"));
        Assert.Null(position.FindUciMove("junk"));
    }

    [Fact]
    public void PromotionNeedsAPiece()
    {
        var position = Position.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        Assert.Null(position.FindUciMove("b7b8"));
        Assert.NotNull(position.FindUciMove("b7b8q"));
    }

    [Fact]
    public void DetectsCheckmate()
    {
        var game = PlayUci("f2f3 e7e5 g2g4 d8h4");
        Assert.Equal(GameResult.Win(PieceColor.Black, GameEndReason.Checkmate), game.Result);
        Assert.Equal("g4 Qh4#", string.Join(' ', game.SanMoves.Skip(2)));
        Assert.Throws<InvalidOperationException>(() => game.Play(new Move(8, 16)));
    }

    [Fact]
    public void DetectsStalemate()
    {
        var game = new Game(Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));
        Assert.Equal(GameResult.Drawn(GameEndReason.Stalemate), game.Result);
    }

    [Fact]
    public void DetectsThreefoldRepetitionAndUndoClearsIt()
    {
        var game = PlayUci("g1f3 g8f6 f3g1 f6g8 g1f3 g8f6 f3g1 f6g8");
        Assert.Equal(GameEndReason.ThreefoldRepetition, game.Result.Reason);
        game.Undo();
        Assert.False(game.Result.IsOver);
    }

    [Fact]
    public void EnPassantSquareWithoutCaptureDoesNotBreakRepetition()
    {
        // After 1. e4 the FEN has an e3 square, but no black pawn can take, so the
        // position repeats once the knights have gone out and back twice.
        var game = PlayUci("e2e4 g8f6 g1f3 f6g8 f3g1 g8f6 g1f3 f6g8 f3g1");
        Assert.Equal(GameEndReason.ThreefoldRepetition, game.Result.Reason);
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/8/4KN2 w - - 0 1", true)]
    [InlineData("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1", true)]   // bishops on the same colour
    [InlineData("4k1b1/8/8/8/8/8/8/2B1K3 w - - 0 1", false)] // opposite colours
    [InlineData("4k3/8/8/8/8/8/8/3NKN2 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false)]
    public void DetectsInsufficientMaterial(string fen, bool expected)
    {
        Assert.Equal(expected, Position.FromFen(fen).IsInsufficientMaterial());
    }

    [Fact]
    public void DetectsFiftyMoveRule()
    {
        var game = PlayUci("a1a2", "4k3/8/8/8/8/8/8/R3K3 w - - 99 80");
        Assert.Equal(GameEndReason.FiftyMoveRule, game.Result.Reason);
    }

    [Fact]
    public void PgnIncludesHeadersMovesAndResult()
    {
        var game = PlayUci("f2f3 e7e5 g2g4 d8h4");
        var pgn = game.ToPgn("Me", "My \"Engine\"", new DateTime(2026, 9, 14));
        Assert.Contains("[Date \"2026.09.14\"]", pgn);
        Assert.Contains("[Black \"My \\\"Engine\\\"\"]", pgn);
        Assert.Contains("[Result \"0-1\"]", pgn);
        Assert.EndsWith("1. f3 e5 2. g4 Qh4# 0-1\n", pgn);
        Assert.DoesNotContain("[FEN", pgn);
    }
}
