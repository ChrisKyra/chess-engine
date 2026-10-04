using System.Globalization;
using System.Text;

namespace ChessCore;

/// <summary>Standard Algebraic Notation ("Nbd7", "exd6", "O-O", "e8=Q+").</summary>
public static class San
{
    public static string Format(Position position, Move move)
    {
        var piece = position[move.From];
        var after = position.Play(move);   // also validates the move
        var sb = new StringBuilder(8);

        if (piece.Type == PieceType.King && Math.Abs(move.To - move.From) == 2)
        {
            sb.Append(move.To > move.From ? "O-O" : "O-O-O");
        }
        else if (piece.Type == PieceType.Pawn)
        {
            bool capture = move.To == position.EnPassantSquare || !position[move.To].IsEmpty;
            if (capture)
                sb.Append((char)('a' + Square.File(move.From))).Append('x');
            sb.Append(Square.Name(move.To));
            if (move.Promotion != PieceType.None)
                sb.Append('=').Append(Piece.Letter(move.Promotion));
        }
        else
        {
            sb.Append(Piece.Letter(piece.Type));
            AppendDisambiguation(sb, position, move, piece);
            if (!position[move.To].IsEmpty)
                sb.Append('x');
            sb.Append(Square.Name(move.To));
        }

        if (after.IsInCheck)
            sb.Append(after.LegalMoves.Count == 0 ? '#' : '+');
        return sb.ToString();
    }

    private static void AppendDisambiguation(StringBuilder sb, Position position, Move move, Piece piece)
    {
        bool ambiguous = false, sharesFile = false, sharesRank = false;
        foreach (var other in position.LegalMoves)
        {
            if (other.To != move.To || other.From == move.From || position[other.From] != piece)
                continue;
            ambiguous = true;
            sharesFile |= Square.File(other.From) == Square.File(move.From);
            sharesRank |= Square.Rank(other.From) == Square.Rank(move.From);
        }

        if (!ambiguous)
            return;
        if (!sharesFile)
            sb.Append((char)('a' + Square.File(move.From)));
        else if (!sharesRank)
            sb.Append((char)('1' + Square.Rank(move.From)));
        else
            sb.Append(Square.Name(move.From));
    }

    /// <summary>
    /// Formats UCI moves as numbered SAN, e.g. "12. Nf3 d5 13. c4" or "12... d5 13. c4".
    /// If a move is illegal the rest of the line is appended unchanged, so a buggy
    /// engine PV is still visible rather than silently dropped.
    /// </summary>
    public static string FormatLine(Position start, IEnumerable<string> uciMoves)
    {
        var tokens = new List<string>();
        var position = start;
        var remaining = uciMoves.ToList();

        for (int i = 0; i < remaining.Count; i++)
        {
            if (position.FindUciMove(remaining[i]) is not { } move)
            {
                tokens.Add("(illegal: " + string.Join(' ', remaining.Skip(i)) + ")");
                break;
            }
            if (position.SideToMove == PieceColor.White)
                tokens.Add(position.FullmoveNumber.ToString(CultureInfo.InvariantCulture) + ".");
            else if (i == 0)
                tokens.Add(position.FullmoveNumber.ToString(CultureInfo.InvariantCulture) + "...");
            tokens.Add(Format(position, move));
            position = position.Play(move);
        }
        return string.Join(' ', tokens);
    }
}
