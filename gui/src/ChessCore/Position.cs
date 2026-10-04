using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ChessCore;

/// <summary>
/// An immutable chess position with full legal move generation.
/// <see cref="Play"/> returns a new position, so a game's history is just a list
/// of positions and taking a move back costs nothing.
/// </summary>
/// <remarks>
/// This is the GUI's referee, not an engine: it favours being obviously correct
/// over being fast. It is verified by perft in the test project.
/// </remarks>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int File, int Rank)[] KnightSteps =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int File, int Rank)[] KingSteps =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int File, int Rank)[] RookDirections = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int File, int Rank)[] BishopDirections = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private static Position? start;

    private readonly Piece[] board = new Piece[64];
    private Move[]? legalMoves;

    private Position()
    {
    }

    public static Position Start => start ??= FromFen(StartFen);

    public PieceColor SideToMove { get; private set; }

    public CastlingRights Castling { get; private set; }

    /// <summary>The square a pawn just skipped over, or <see cref="Square.None"/>.</summary>
    public int EnPassantSquare { get; private set; } = Square.None;

    public int HalfmoveClock { get; private set; }

    public int FullmoveNumber { get; private set; } = 1;

    public Piece this[int square] => board[square];

    public IReadOnlyList<Move> LegalMoves => legalMoves ??= GenerateLegalMoves();

    public bool IsInCheck => IsSquareAttacked(KingSquare(SideToMove), SideToMove.Opposite());

    // ----------------------------------------------------------------------
    // FEN
    // ----------------------------------------------------------------------

    public static Position FromFen(string fen) =>
        TryParseFen(fen, out var position, out var error) ? position : throw new FormatException(error);

    /// <summary>
    /// Parses a FEN. Only the board and side to move are required; castling rights
    /// that don't match the board and impossible en passant squares are dropped.
    /// </summary>
    public static bool TryParseFen(string? fen, [NotNullWhen(true)] out Position? position, out string error)
    {
        position = null;
        var fields = (fen ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            error = "A FEN needs at least the board and the side to move.";
            return false;
        }

        var p = new Position();
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8)
        {
            error = "The board must have 8 ranks separated by '/'.";
            return false;
        }

        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;
            foreach (char c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    file += c - '0';
                }
                else if (Piece.TryFromFenChar(c, out var piece))
                {
                    if (file > 7)
                    {
                        error = $"Rank {rank + 1} has more than 8 squares.";
                        return false;
                    }
                    p.board[Square.FromCoords(file, rank)] = piece;
                    file++;
                }
                else
                {
                    error = $"Unexpected character '{c}' in the board.";
                    return false;
                }
            }
            if (file != 8)
            {
                error = $"Rank {rank + 1} has {file} squares instead of 8.";
                return false;
            }
        }

        switch (fields[1])
        {
            case "w": p.SideToMove = PieceColor.White; break;
            case "b": p.SideToMove = PieceColor.Black; break;
            default:
                error = "The side to move must be 'w' or 'b'.";
                return false;
        }

        if (fields.Length > 2 && fields[2] != "-")
        {
            foreach (char c in fields[2])
            {
                p.Castling |= c switch
                {
                    'K' => CastlingRights.WhiteKingside,
                    'Q' => CastlingRights.WhiteQueenside,
                    'k' => CastlingRights.BlackKingside,
                    'q' => CastlingRights.BlackQueenside,
                    _ => CastlingRights.None,
                };
            }
        }

        if (fields.Length > 3 && fields[3] != "-")
        {
            if (!Square.TryParse(fields[3], out int ep))
            {
                error = $"Invalid en passant square '{fields[3]}'.";
                return false;
            }
            p.EnPassantSquare = ep;
        }

        if (fields.Length > 4)
        {
            if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int halfmove))
            {
                error = $"Invalid halfmove clock '{fields[4]}'.";
                return false;
            }
            p.HalfmoveClock = halfmove;
        }

        if (fields.Length > 5)
        {
            if (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int fullmove) || fullmove < 1)
            {
                error = $"Invalid fullmove number '{fields[5]}'.";
                return false;
            }
            p.FullmoveNumber = fullmove;
        }

        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            var king = new Piece(color, PieceType.King);
            if (p.board.Count(x => x == king) != 1)
            {
                error = $"{color} must have exactly one king.";
                return false;
            }
        }

        for (int file = 0; file < 8; file++)
        {
            if (p.board[file].Type == PieceType.Pawn || p.board[56 + file].Type == PieceType.Pawn)
            {
                error = "Pawns cannot stand on the first or last rank.";
                return false;
            }
        }

        if (p.IsSquareAttacked(p.KingSquare(p.SideToMove.Opposite()), p.SideToMove))
        {
            error = "The side that is not to move is in check.";
            return false;
        }

        p.DropImpossibleRights();
        position = p;
        error = "";
        return true;
    }

    private void DropImpossibleRights()
    {
        var whiteRook = new Piece(PieceColor.White, PieceType.Rook);
        var blackRook = new Piece(PieceColor.Black, PieceType.Rook);
        if (board[4] != new Piece(PieceColor.White, PieceType.King))
            Castling &= ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside);
        if (board[7] != whiteRook)
            Castling &= ~CastlingRights.WhiteKingside;
        if (board[0] != whiteRook)
            Castling &= ~CastlingRights.WhiteQueenside;
        if (board[60] != new Piece(PieceColor.Black, PieceType.King))
            Castling &= ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside);
        if (board[63] != blackRook)
            Castling &= ~CastlingRights.BlackKingside;
        if (board[56] != blackRook)
            Castling &= ~CastlingRights.BlackQueenside;

        if (EnPassantSquare != Square.None)
        {
            // The square must be empty and sit directly behind an enemy pawn that could have just double-pushed.
            int expectedRank = SideToMove == PieceColor.White ? 5 : 2;
            int pawnSquare = SideToMove == PieceColor.White ? EnPassantSquare - 8 : EnPassantSquare + 8;
            if (Square.Rank(EnPassantSquare) != expectedRank
                || !board[EnPassantSquare].IsEmpty
                || board[pawnSquare] != new Piece(SideToMove.Opposite(), PieceType.Pawn))
            {
                EnPassantSquare = Square.None;
            }
        }
    }

    public string ToFen()
    {
        var sb = new StringBuilder(90);
        sb.Append(PlacementFen());
        sb.Append(SideToMove == PieceColor.White ? " w " : " b ");
        sb.Append(CastlingFen());
        sb.Append(' ').Append(Square.Name(EnPassantSquare));
        sb.Append(CultureInfo.InvariantCulture, $" {HalfmoveClock} {FullmoveNumber}");
        return sb.ToString();
    }

    private string PlacementFen()
    {
        var sb = new StringBuilder(72);
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                var piece = board[Square.FromCoords(file, rank)];
                if (piece.IsEmpty)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                    sb.Append((char)('0' + empty));
                empty = 0;
                sb.Append(piece.ToFenChar());
            }
            if (empty > 0)
                sb.Append((char)('0' + empty));
            if (rank > 0)
                sb.Append('/');
        }
        return sb.ToString();
    }

    private string CastlingFen()
    {
        if (Castling == CastlingRights.None)
            return "-";
        var sb = new StringBuilder(4);
        if (Castling.HasFlag(CastlingRights.WhiteKingside)) sb.Append('K');
        if (Castling.HasFlag(CastlingRights.WhiteQueenside)) sb.Append('Q');
        if (Castling.HasFlag(CastlingRights.BlackKingside)) sb.Append('k');
        if (Castling.HasFlag(CastlingRights.BlackQueenside)) sb.Append('q');
        return sb.ToString();
    }

    /// <summary>
    /// Identity for threefold repetition: placement, side to move, castling rights,
    /// and the en passant square only when an en passant capture is actually legal.
    /// </summary>
    public string RepetitionKey()
    {
        bool epCapturePossible = LegalMoves.Any(m => m.To == EnPassantSquare && board[m.From].Type == PieceType.Pawn);
        return $"{PlacementFen()} {(SideToMove == PieceColor.White ? 'w' : 'b')} {CastlingFen()} "
             + (epCapturePossible ? Square.Name(EnPassantSquare) : "-");
    }

    // ----------------------------------------------------------------------
    // Queries
    // ----------------------------------------------------------------------

    public int KingSquare(PieceColor color)
    {
        var king = new Piece(color, PieceType.King);
        return Array.IndexOf(board, king);
    }

    public bool IsLegal(Move move) => LegalMoves.Contains(move);

    /// <summary>
    /// Resolves a UCI move string against the legal moves. Also accepts
    /// king-takes-own-rook castling (e1h1), which some engines emit.
    /// </summary>
    public Move? FindUciMove(string? uci)
    {
        if (!Move.TryParseUci(uci, out var move))
            return null;
        if (IsLegal(move))
            return move;

        var piece = board[move.From];
        if (piece.Type == PieceType.King
            && board[move.To] == new Piece(piece.Color, PieceType.Rook)
            && Square.Rank(move.From) == Square.Rank(move.To))
        {
            var castle = new Move(move.From, move.To > move.From ? move.From + 2 : move.From - 2);
            if (IsLegal(castle))
                return castle;
        }
        return null;
    }

    public bool IsSquareAttacked(int square, PieceColor by)
    {
        if (square == Square.None)
            return false;
        int file = Square.File(square), rank = Square.Rank(square);

        // Pawns capture diagonally forward, so an attacking pawn sits one rank "behind" the square.
        int pawnRank = by == PieceColor.White ? rank - 1 : rank + 1;
        if (IsPieceAt(file - 1, pawnRank, by, PieceType.Pawn) || IsPieceAt(file + 1, pawnRank, by, PieceType.Pawn))
            return true;

        foreach (var (df, dr) in KnightSteps)
            if (IsPieceAt(file + df, rank + dr, by, PieceType.Knight))
                return true;

        foreach (var (df, dr) in KingSteps)
            if (IsPieceAt(file + df, rank + dr, by, PieceType.King))
                return true;

        return IsSlidingAttack(file, rank, by, RookDirections, PieceType.Rook)
            || IsSlidingAttack(file, rank, by, BishopDirections, PieceType.Bishop);
    }

    private bool IsPieceAt(int file, int rank, PieceColor color, PieceType type) =>
        Square.OnBoard(file, rank) && board[Square.FromCoords(file, rank)] == new Piece(color, type);

    private bool IsSlidingAttack(int file, int rank, PieceColor by, (int File, int Rank)[] directions, PieceType slider)
    {
        foreach (var (df, dr) in directions)
        {
            for (int f = file + df, r = rank + dr; Square.OnBoard(f, r); f += df, r += dr)
            {
                var piece = board[Square.FromCoords(f, r)];
                if (piece.IsEmpty)
                    continue;
                if (piece.Color == by && (piece.Type == slider || piece.Type == PieceType.Queen))
                    return true;
                break;
            }
        }
        return false;
    }

    /// <summary>Neither side can possibly checkmate: K v K, K+minor v K, or only same-coloured bishops.</summary>
    public bool IsInsufficientMaterial()
    {
        int knights = 0, bishops = 0, bishopSquareColours = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            switch (board[sq].Type)
            {
                case PieceType.Pawn or PieceType.Rook or PieceType.Queen:
                    return false;
                case PieceType.Knight:
                    knights++;
                    break;
                case PieceType.Bishop:
                    bishops++;
                    bishopSquareColours |= Square.IsLight(sq) ? 2 : 1;
                    break;
            }
        }
        if (knights + bishops <= 1)
            return true;
        return knights == 0 && bishopSquareColours != 3;
    }

    /// <summary>
    /// Whether <paramref name="color"/> has material that could mate. Used when the
    /// other side runs out of time: a lone king or king + one minor piece cannot win.
    /// </summary>
    public bool HasMatingMaterial(PieceColor color)
    {
        int minors = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            var piece = board[sq];
            if (piece.IsEmpty || piece.Color != color)
                continue;
            if (piece.Type is PieceType.Pawn or PieceType.Rook or PieceType.Queen)
                return true;
            if (piece.Type is PieceType.Knight or PieceType.Bishop)
                minors++;
        }
        return minors >= 2;
    }

    // ----------------------------------------------------------------------
    // Move generation
    // ----------------------------------------------------------------------

    private Move[] GenerateLegalMoves()
    {
        var us = SideToMove;
        var pseudo = new List<Move>(64);
        for (int sq = 0; sq < 64; sq++)
        {
            var piece = board[sq];
            if (piece.IsEmpty || piece.Color != us)
                continue;
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(sq, pseudo);
                    break;
                case PieceType.Knight:
                    AddStepMoves(sq, KnightSteps, pseudo);
                    break;
                case PieceType.Bishop:
                    AddSlidingMoves(sq, BishopDirections, pseudo);
                    break;
                case PieceType.Rook:
                    AddSlidingMoves(sq, RookDirections, pseudo);
                    break;
                case PieceType.Queen:
                    AddSlidingMoves(sq, RookDirections, pseudo);
                    AddSlidingMoves(sq, BishopDirections, pseudo);
                    break;
                case PieceType.King:
                    AddStepMoves(sq, KingSteps, pseudo);
                    AddCastlingMoves(sq, pseudo);
                    break;
            }
        }

        // Keep the moves that don't leave our own king attacked.
        var legal = new List<Move>(pseudo.Count);
        foreach (var move in pseudo)
        {
            var next = Apply(move);
            if (!next.IsSquareAttacked(next.KingSquare(us), next.SideToMove))
                legal.Add(move);
        }
        return legal.ToArray();
    }

    private void AddPawnMoves(int from, List<Move> moves)
    {
        var us = SideToMove;
        int file = Square.File(from), rank = Square.Rank(from);
        int forward = us == PieceColor.White ? 1 : -1;
        int nextRank = rank + forward;
        if (!Square.OnBoard(file, nextRank))
            return;

        int one = Square.FromCoords(file, nextRank);
        if (board[one].IsEmpty)
        {
            AddPawnMove(from, one, moves);
            int startRank = us == PieceColor.White ? 1 : 6;
            int two = one + 8 * forward;
            if (rank == startRank && board[two].IsEmpty)
                moves.Add(new Move(from, two));
        }

        for (int df = -1; df <= 1; df += 2)
        {
            if (!Square.OnBoard(file + df, nextRank))
                continue;
            int to = Square.FromCoords(file + df, nextRank);
            var target = board[to];
            if (!target.IsEmpty && target.Color != us)
                AddPawnMove(from, to, moves);
            else if (target.IsEmpty && to == EnPassantSquare)
                moves.Add(new Move(from, to));
        }
    }

    private static void AddPawnMove(int from, int to, List<Move> moves)
    {
        if (Square.Rank(to) is 0 or 7)
        {
            moves.Add(new Move(from, to, PieceType.Queen));
            moves.Add(new Move(from, to, PieceType.Rook));
            moves.Add(new Move(from, to, PieceType.Bishop));
            moves.Add(new Move(from, to, PieceType.Knight));
        }
        else
        {
            moves.Add(new Move(from, to));
        }
    }

    private void AddStepMoves(int from, (int File, int Rank)[] steps, List<Move> moves)
    {
        int file = Square.File(from), rank = Square.Rank(from);
        foreach (var (df, dr) in steps)
        {
            if (!Square.OnBoard(file + df, rank + dr))
                continue;
            int to = Square.FromCoords(file + df, rank + dr);
            if (board[to].IsEmpty || board[to].Color != SideToMove)
                moves.Add(new Move(from, to));
        }
    }

    private void AddSlidingMoves(int from, (int File, int Rank)[] directions, List<Move> moves)
    {
        int file = Square.File(from), rank = Square.Rank(from);
        foreach (var (df, dr) in directions)
        {
            for (int f = file + df, r = rank + dr; Square.OnBoard(f, r); f += df, r += dr)
            {
                int to = Square.FromCoords(f, r);
                var target = board[to];
                if (target.IsEmpty)
                {
                    moves.Add(new Move(from, to));
                    continue;
                }
                if (target.Color != SideToMove)
                    moves.Add(new Move(from, to));
                break;
            }
        }
    }

    private void AddCastlingMoves(int from, List<Move> moves)
    {
        var us = SideToMove;
        if (from != (us == PieceColor.White ? 4 : 60))
            return;
        var kingside = us == PieceColor.White ? CastlingRights.WhiteKingside : CastlingRights.BlackKingside;
        var queenside = us == PieceColor.White ? CastlingRights.WhiteQueenside : CastlingRights.BlackQueenside;
        if ((Castling & (kingside | queenside)) == 0)
            return;

        var them = us.Opposite();
        if (IsSquareAttacked(from, them))
            return;
        var rook = new Piece(us, PieceType.Rook);

        if ((Castling & kingside) != 0
            && board[from + 3] == rook
            && board[from + 1].IsEmpty && board[from + 2].IsEmpty
            && !IsSquareAttacked(from + 1, them) && !IsSquareAttacked(from + 2, them))
        {
            moves.Add(new Move(from, from + 2));
        }

        if ((Castling & queenside) != 0
            && board[from - 4] == rook
            && board[from - 1].IsEmpty && board[from - 2].IsEmpty && board[from - 3].IsEmpty
            && !IsSquareAttacked(from - 1, them) && !IsSquareAttacked(from - 2, them))
        {
            moves.Add(new Move(from, from - 2));
        }
    }

    // ----------------------------------------------------------------------
    // Making moves
    // ----------------------------------------------------------------------

    /// <summary>Returns the position after a legal move. Throws if the move is illegal.</summary>
    public Position Play(Move move)
    {
        if (!IsLegal(move))
            throw new ArgumentException($"Illegal move {move.ToUci()} in position {ToFen()}", nameof(move));
        return Apply(move);
    }

    private Position Apply(Move move)
    {
        var next = new Position
        {
            SideToMove = SideToMove.Opposite(),
            Castling = Castling,
            HalfmoveClock = HalfmoveClock + 1,
            FullmoveNumber = SideToMove == PieceColor.Black ? FullmoveNumber + 1 : FullmoveNumber,
        };
        Array.Copy(board, next.board, 64);

        var piece = board[move.From];
        var captured = board[move.To];
        if (!captured.IsEmpty)
            next.HalfmoveClock = 0;
        next.board[move.From] = Piece.Empty;

        if (piece.Type == PieceType.Pawn)
        {
            next.HalfmoveClock = 0;
            if (move.To == EnPassantSquare && captured.IsEmpty)
                next.board[Square.FromCoords(Square.File(move.To), Square.Rank(move.From))] = Piece.Empty;
            else if (Math.Abs(move.To - move.From) == 16)
                next.EnPassantSquare = (move.From + move.To) / 2;
            if (move.Promotion != PieceType.None)
                piece = new Piece(piece.Color, move.Promotion);
        }
        else if (piece.Type == PieceType.King && Math.Abs(move.To - move.From) == 2)
        {
            bool kingside = move.To > move.From;
            int rookFrom = kingside ? move.From + 3 : move.From - 4;
            int rookTo = kingside ? move.From + 1 : move.From - 1;
            next.board[rookTo] = next.board[rookFrom];
            next.board[rookFrom] = Piece.Empty;
        }

        next.board[move.To] = piece;
        next.Castling &= ~(RightsLostBy(move.From) | RightsLostBy(move.To));
        return next;
    }

    /// <summary>Castling rights that disappear when a piece leaves or lands on this square.</summary>
    private static CastlingRights RightsLostBy(int square) => square switch
    {
        0 => CastlingRights.WhiteQueenside,
        4 => CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside,
        7 => CastlingRights.WhiteKingside,
        56 => CastlingRights.BlackQueenside,
        60 => CastlingRights.BlackKingside | CastlingRights.BlackQueenside,
        63 => CastlingRights.BlackKingside,
        _ => CastlingRights.None,
    };
}
