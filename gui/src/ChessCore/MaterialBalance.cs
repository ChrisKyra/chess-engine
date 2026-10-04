namespace ChessCore;

/// <summary>
/// What each side has captured in a game, and who is ahead in material, for the
/// panel beside the board.
/// </summary>
public static class MaterialBalance
{
    /// <summary>The usual piece values in pawns: 1, 3, 3, 5, 9 (the king counts nothing).</summary>
    public static int Value(PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight or PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0,
    };

    /// <summary>
    /// The pieces each side has taken so far, cheapest first: <c>ByWhite</c> holds
    /// black pieces, <c>ByBlack</c> white ones. A pawn taken en passant counts as a
    /// pawn, and a piece that was promoted counts as what it became.
    /// </summary>
    public static (IReadOnlyList<Piece> ByWhite, IReadOnlyList<Piece> ByBlack) Captures(Game game)
    {
        var byWhite = new List<Piece>();
        var byBlack = new List<Piece>();
        for (int ply = 0; ply < game.PlyCount; ply++)
        {
            var before = game.PositionBefore(ply);
            var move = game.Moves[ply];
            var taken = before[move.To];
            // A pawn moving diagonally onto an empty square took en passant.
            if (taken.IsEmpty && before[move.From].Type == PieceType.Pawn && Square.File(move.From) != Square.File(move.To))
                taken = new Piece(before.SideToMove.Opposite(), PieceType.Pawn);
            if (taken.IsEmpty)
                continue;
            (before.SideToMove == PieceColor.White ? byWhite : byBlack).Add(taken);
        }
        byWhite.Sort(ByValue);
        byBlack.Sort(ByValue);
        return (byWhite, byBlack);
    }

    /// <summary>
    /// White's material minus Black's on the board, in pawns: positive when White is
    /// ahead. Counted from the pieces standing there, so promotions are included.
    /// </summary>
    public static int Balance(Position position)
    {
        int balance = 0;
        for (int square = 0; square < 64; square++)
        {
            var piece = position[square];
            if (!piece.IsEmpty)
                balance += piece.Color == PieceColor.White ? Value(piece.Type) : -Value(piece.Type);
        }
        return balance;
    }

    // Cheapest first, and pieces of one kind together.
    private static int ByValue(Piece a, Piece b) =>
        a.Type == b.Type ? 0 : Value(a.Type) != Value(b.Type) ? Value(a.Type).CompareTo(Value(b.Type)) : a.Type.CompareTo(b.Type);
}
