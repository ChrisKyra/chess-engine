namespace ChessCore.Tests;

/// <summary>
/// Perft counts every leaf of the legal move tree to a fixed depth. Matching the
/// published numbers for these positions exercises castling, en passant (including
/// the discovered-check case), promotions, pins and check evasions.
/// </summary>
public class PerftTests
{
    [Theory]
    [InlineData(Position.StartFen, 1, 20)]
    [InlineData(Position.StartFen, 2, 400)]
    [InlineData(Position.StartFen, 3, 8_902)]
    [InlineData(Position.StartFen, 4, 197_281)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2_039)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97_862)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43_238)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 5, 674_624)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9_467)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 4, 422_333)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62_379)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89_890)]
    public void MatchesReferenceCounts(string fen, int depth, long expected)
    {
        Assert.Equal(expected, Perft(Position.FromFen(fen), depth));
    }

    private static long Perft(Position position, int depth)
    {
        var moves = position.LegalMoves;
        if (depth == 1)
            return moves.Count;
        long total = 0;
        foreach (var move in moves)
            total += Perft(position.Play(move), depth - 1);
        return total;
    }
}
