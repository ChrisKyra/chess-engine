namespace ChessCore;

public enum PieceColor : byte
{
    White,
    Black,
}

public enum PieceType : byte
{
    None,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
}

public static class PieceColorExtensions
{
    public static PieceColor Opposite(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}

/// <summary>What stands on a square. <c>default</c> is an empty square.</summary>
public readonly record struct Piece(PieceColor Color, PieceType Type)
{
    public static Piece Empty => default;

    public bool IsEmpty => Type == PieceType.None;

    /// <summary>Upper-case SAN letter for a piece type ("N", "B", ...); empty for pawns.</summary>
    public static string Letter(PieceType type) => type switch
    {
        PieceType.Knight => "N",
        PieceType.Bishop => "B",
        PieceType.Rook => "R",
        PieceType.Queen => "Q",
        PieceType.King => "K",
        _ => "",
    };

    public char ToFenChar()
    {
        char c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => '.',
        };
        return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
    }

    public static bool TryFromFenChar(char c, out Piece piece)
    {
        var type = TypeFromLetter(c);
        piece = type == PieceType.None
            ? Empty
            : new Piece(char.IsUpper(c) ? PieceColor.White : PieceColor.Black, type);
        return type != PieceType.None;
    }

    internal static PieceType TypeFromLetter(char c) => char.ToLowerInvariant(c) switch
    {
        'p' => PieceType.Pawn,
        'n' => PieceType.Knight,
        'b' => PieceType.Bishop,
        'r' => PieceType.Rook,
        'q' => PieceType.Queen,
        'k' => PieceType.King,
        _ => PieceType.None,
    };
}

/// <summary>Squares are ints 0..63 with a1 = 0, h1 = 7, a8 = 56, h8 = 63.</summary>
public static class Square
{
    public const int None = -1;

    public static int File(int square) => square & 7;

    public static int Rank(int square) => square >> 3;

    public static int FromCoords(int file, int rank) => rank * 8 + file;

    public static bool OnBoard(int file, int rank) => (uint)file < 8 && (uint)rank < 8;

    public static bool IsLight(int square) => ((File(square) + Rank(square)) & 1) == 1;

    public static string Name(int square) =>
        square == None ? "-" : $"{(char)('a' + File(square))}{(char)('1' + Rank(square))}";

    public static bool TryParse(ReadOnlySpan<char> text, out int square)
    {
        square = None;
        if (text.Length != 2)
            return false;
        int file = text[0] - 'a', rank = text[1] - '1';
        if (!OnBoard(file, rank))
            return false;
        square = FromCoords(file, rank);
        return true;
    }
}

/// <summary>
/// A move as the GUI and UCI see it: from, to, and an optional promotion piece.
/// Castling is the king moving two squares; en passant is a pawn moving to the en passant square.
/// </summary>
public readonly record struct Move(int From, int To, PieceType Promotion = PieceType.None)
{
    public string ToUci()
    {
        var text = Square.Name(From) + Square.Name(To);
        return Promotion == PieceType.None ? text : text + Piece.Letter(Promotion).ToLowerInvariant();
    }

    public static bool TryParseUci(string? text, out Move move)
    {
        move = default;
        text = text?.Trim();
        if (text is null || text.Length is not (4 or 5))
            return false;
        if (!Square.TryParse(text.AsSpan(0, 2), out int from) || !Square.TryParse(text.AsSpan(2, 2), out int to))
            return false;

        var promotion = PieceType.None;
        if (text.Length == 5)
        {
            promotion = Piece.TypeFromLetter(text[4]);
            if (promotion is PieceType.None or PieceType.Pawn or PieceType.King)
                return false;
        }
        move = new Move(from, to, promotion);
        return true;
    }

    public override string ToString() => ToUci();
}
