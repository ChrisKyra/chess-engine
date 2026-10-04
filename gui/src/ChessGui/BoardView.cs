using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using ChessCore;

namespace ChessGui;

/// <summary>
/// Draws the board and lets the user move by clicking or dragging. It never changes
/// the game: it raises <see cref="MovePlayed"/> with a legal move and waits for the
/// owner to hand it the next <see cref="Position"/>.
/// </summary>
public sealed class BoardView : Control, ICustomHitTest
{
    private static readonly PieceType[] PromotionChoices = [PieceType.Queen, PieceType.Knight, PieceType.Rook, PieceType.Bishop];

    private static readonly IBrush LightSquare = Solid("#EBECD0");
    private static readonly IBrush DarkSquare = Solid("#739552");
    private static readonly IBrush LastMoveTint = Solid("#70F5F16A");
    private static readonly IBrush SelectedTint = Solid("#A0F5F16A");
    private static readonly IBrush MoveDot = Solid("#30000000");
    private static readonly IBrush HintArrow = Solid("#B0E8912D");
    private static readonly IBrush Dim = Solid("#90000000");
    private static readonly IBrush PickerCircle = Solid("#F2F2F2");
    private static readonly IBrush DropOutline = Solid("#D0FFFFFF");
    private static readonly IBrush CheckGlow = new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.Parse("#FFFF2A1A"), 0),
            new GradientStop(Color.Parse("#C0E8331F"), 0.45),
            new GradientStop(Color.Parse("#00E8331F"), 1),
        },
    }.ToImmutable();

    private Position? position;
    private bool flipped;
    private Move? lastMove;
    private Move? hintMove;
    private bool canMove;

    private int selected = Square.None;
    private int pressedSquare = Square.None;
    private bool dragging;
    private bool deselectOnRelease;
    private Point pressPoint;
    private Point pointer;
    private (int From, int To)? pendingPromotion;
    private Cursor? handCursor;

    /// <summary>The user chose a legal move (promotion piece included).</summary>
    public event EventHandler<Move>? MovePlayed;

    public Position? Position
    {
        get => position;
        set
        {
            if (ReferenceEquals(position, value))
                return;
            position = value;
            ResetInteraction();
            InvalidateVisual();
        }
    }

    public bool Flipped
    {
        get => flipped;
        set
        {
            flipped = value;
            InvalidateVisual();
        }
    }

    public Move? LastMove
    {
        get => lastMove;
        set
        {
            lastMove = value;
            InvalidateVisual();
        }
    }

    /// <summary>An arrow to draw, e.g. the engine's current best move.</summary>
    public Move? HintMove
    {
        get => hintMove;
        set
        {
            if (hintMove == value)
                return;
            hintMove = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the user may move pieces for the side to move.</summary>
    public bool CanMove
    {
        get => canMove;
        set
        {
            if (canMove == value)
                return;
            canMove = value;
            if (!value)
                ResetInteraction();
            InvalidateVisual();
        }
    }

    /// <summary>Edge length of the drawn board in device-independent pixels.</summary>
    public double BoardSize => SquareSize * 8;

    private double SquareSize => Math.Floor(Math.Min(Bounds.Width, Bounds.Height) / 8);

    private Point Origin => new(Math.Floor((Bounds.Width - BoardSize) / 2), Math.Floor((Bounds.Height - BoardSize) / 2));

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 480 : availableSize.Height;
        return new Size(w, h);
    }

    // ----------------------------------------------------------------------
    // Rendering
    // ----------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        double s = SquareSize;
        if (s <= 0)
            return;

        for (int sq = 0; sq < 64; sq++)
            context.FillRectangle(Square.IsLight(sq) ? LightSquare : DarkSquare, SquareRect(sq));

        if (lastMove is { } last)
        {
            context.FillRectangle(LastMoveTint, SquareRect(last.From));
            context.FillRectangle(LastMoveTint, SquareRect(last.To));
        }
        if (selected != Square.None)
            context.FillRectangle(SelectedTint, SquareRect(selected));

        DrawCoordinates(context, s);

        var pos = position;
        if (pos is null)
            return;

        if (pos.IsInCheck)
            context.FillRectangle(CheckGlow, SquareRect(pos.KingSquare(pos.SideToMove)));

        if (selected != Square.None)
            DrawTargets(context, pos, s);

        for (int sq = 0; sq < 64; sq++)
        {
            if (dragging && sq == selected)
                continue;
            PieceArt.Draw(context, pos[sq], SquareRect(sq));
        }

        if (hintMove is { } hint && pendingPromotion is null)
            DrawArrow(context, SquareRect(hint.From).Center, SquareRect(hint.To).Center, s);

        if (dragging && selected != Square.None)
        {
            int over = SquareAt(pointer);
            if (over != Square.None && Targets(selected).Contains(over))
                context.DrawRectangle(null, new Pen(DropOutline, Math.Max(2, s * 0.05)), SquareRect(over).Deflate(s * 0.025));
            PieceArt.Draw(context, pos[selected], new Rect(pointer.X - s / 2, pointer.Y - s / 2, s, s));
        }

        if (pendingPromotion is { } promotion)
            DrawPromotionPicker(context, pos.SideToMove, promotion.To, s);
    }

    private void DrawCoordinates(DrawingContext context, double s)
    {
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        double fontSize = Math.Max(9, s * 0.16);

        for (int i = 0; i < 8; i++)
        {
            // File letters along the bottom edge, rank numbers down the left edge.
            int file = flipped ? 7 - i : i;
            int bottomSquare = Square.FromCoords(file, flipped ? 7 : 0);
            var rect = SquareRect(bottomSquare);
            var text = Label((char)('a' + file), typeface, fontSize, Square.IsLight(bottomSquare) ? DarkSquare : LightSquare);
            context.DrawText(text, new Point(rect.Right - text.Width - s * 0.05, rect.Bottom - text.Height - s * 0.01));

            int rank = flipped ? i : 7 - i;
            int leftSquare = Square.FromCoords(flipped ? 7 : 0, rank);
            rect = SquareRect(leftSquare);
            text = Label((char)('1' + rank), typeface, fontSize, Square.IsLight(leftSquare) ? DarkSquare : LightSquare);
            context.DrawText(text, new Point(rect.X + s * 0.05, rect.Y + s * 0.02));
        }
    }

    private void DrawTargets(DrawingContext context, Position pos, double s)
    {
        var ring = new Pen(MoveDot, s * 0.09);
        foreach (int to in Targets(selected))
        {
            var center = SquareRect(to).Center;
            bool capture = !pos[to].IsEmpty || (pos[selected].Type == PieceType.Pawn && to == pos.EnPassantSquare);
            if (capture)
                context.DrawEllipse(null, ring, center, s * 0.455, s * 0.455);
            else
                context.DrawEllipse(MoveDot, null, center, s * 0.16, s * 0.16);
        }
    }

    private static void DrawArrow(DrawingContext context, Point from, Point to, double s)
    {
        var delta = to - from;
        double length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length < 1)
            return;

        var unit = delta / length;
        var normal = new Vector(-unit.Y, unit.X);
        double halfShaft = s * 0.07, halfHead = s * 0.2, headLength = s * 0.38;
        var start = from + unit * (s * 0.2);
        var tip = to;
        var neck = tip - unit * headLength;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start + normal * halfShaft, isFilled: true);
            g.LineTo(neck + normal * halfShaft);
            g.LineTo(neck + normal * halfHead);
            g.LineTo(tip);
            g.LineTo(neck - normal * halfHead);
            g.LineTo(neck - normal * halfShaft);
            g.LineTo(start - normal * halfShaft);
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(HintArrow, null, geometry);
    }

    private void DrawPromotionPicker(DrawingContext context, PieceColor color, int to, double s)
    {
        context.FillRectangle(Dim, new Rect(Origin, new Size(BoardSize, BoardSize)));
        for (int i = 0; i < PromotionChoices.Length; i++)
        {
            var rect = PromotionRect(to, i);
            context.DrawEllipse(PickerCircle, null, rect.Center, s * 0.47, s * 0.47);
            PieceArt.Draw(context, new Piece(color, PromotionChoices[i]), rect.Deflate(s * 0.07));
        }
    }

    private static FormattedText Label(char c, Typeface typeface, double size, IBrush brush) =>
        new(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);

    private static IBrush Solid(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    // ----------------------------------------------------------------------
    // Geometry
    // ----------------------------------------------------------------------

    private Rect SquareRect(int square)
    {
        int column = flipped ? 7 - Square.File(square) : Square.File(square);
        int row = flipped ? Square.Rank(square) : 7 - Square.Rank(square);
        double s = SquareSize;
        var origin = Origin;
        return new Rect(origin.X + column * s, origin.Y + row * s, s, s);
    }

    private int SquareAt(Point point)
    {
        double s = SquareSize;
        if (s <= 0)
            return Square.None;
        var origin = Origin;
        int column = (int)Math.Floor((point.X - origin.X) / s);
        int row = (int)Math.Floor((point.Y - origin.Y) / s);
        if ((uint)column > 7 || (uint)row > 7)
            return Square.None;
        return flipped ? Square.FromCoords(7 - column, row) : Square.FromCoords(column, 7 - row);
    }

    /// <summary>The promotion picker stacks four squares from the promotion square toward the centre.</summary>
    private Rect PromotionRect(int to, int index)
    {
        var rect = SquareRect(to);
        bool atTop = rect.Y - Origin.Y < SquareSize / 2;
        return rect.Translate(new Vector(0, (atTop ? 1 : -1) * index * SquareSize));
    }

    private IEnumerable<int> Targets(int from) =>
        position is null ? [] : position.LegalMoves.Where(m => m.From == from).Select(m => m.To).Distinct();

    // ----------------------------------------------------------------------
    // Input
    // ----------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var pos = position;
        if (pos is null)
            return;

        if (point.Properties.IsRightButtonPressed)
        {
            ResetInteraction();
            InvalidateVisual();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;

        if (pendingPromotion is { } promotion)
        {
            for (int i = 0; i < PromotionChoices.Length; i++)
            {
                if (PromotionRect(promotion.To, i).Contains(point.Position))
                {
                    ResetInteraction();
                    InvalidateVisual();
                    MovePlayed?.Invoke(this, new Move(promotion.From, promotion.To, PromotionChoices[i]));
                    return;
                }
            }
            pendingPromotion = null;   // clicking anywhere else cancels
            InvalidateVisual();
            return;
        }

        if (!canMove)
            return;

        int square = SquareAt(point.Position);
        if (selected != Square.None && square != selected && Targets(selected).Contains(square))
        {
            TryMove(selected, square);
            return;
        }

        var piece = square == Square.None ? Piece.Empty : pos[square];
        if (!piece.IsEmpty && piece.Color == pos.SideToMove)
        {
            deselectOnRelease = selected == square;
            selected = square;
            pressedSquare = square;
            pressPoint = pointer = point.Position;
            dragging = false;
            e.Pointer.Capture(this);
        }
        else
        {
            selected = Square.None;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (pressedSquare != Square.None)
        {
            pointer = point;
            if (!dragging && (Math.Abs(point.X - pressPoint.X) > 4 || Math.Abs(point.Y - pressPoint.Y) > 4))
                dragging = true;
            if (dragging)
                InvalidateVisual();
        }

        int square = SquareAt(point);
        bool overOwnPiece = canMove && position is { } pos && square != Square.None
            && !pos[square].IsEmpty && pos[square].Color == pos.SideToMove;
        var cursor = overOwnPiece || dragging ? handCursor ??= new Cursor(StandardCursorType.Hand) : null;
        if (!ReferenceEquals(Cursor, cursor))
            Cursor = cursor;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressedSquare == Square.None || e.InitialPressMouseButton != MouseButton.Left)
            return;

        int from = pressedSquare;
        bool wasDragging = dragging;
        bool deselect = deselectOnRelease;
        pressedSquare = Square.None;
        dragging = false;
        deselectOnRelease = false;
        e.Pointer.Capture(null);

        if (wasDragging)
        {
            int to = SquareAt(e.GetPosition(this));
            if (to != Square.None && to != from && Targets(from).Contains(to))
            {
                TryMove(from, to);
                return;
            }
        }
        else if (deselect)
        {
            selected = Square.None;
        }
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!dragging)
            return;
        dragging = false;
        pressedSquare = Square.None;
        InvalidateVisual();
    }

    private void TryMove(int from, int to)
    {
        var candidates = position!.LegalMoves.Where(m => m.From == from && m.To == to).ToList();
        if (candidates.Count == 0)
            return;

        if (candidates[0].Promotion != PieceType.None)
        {
            selected = Square.None;
            pendingPromotion = (from, to);
            InvalidateVisual();
            return;
        }

        ResetInteraction();
        InvalidateVisual();
        MovePlayed?.Invoke(this, candidates[0]);
    }

    private void ResetInteraction()
    {
        selected = Square.None;
        pressedSquare = Square.None;
        dragging = false;
        deselectOnRelease = false;
        pendingPromotion = null;
    }
}
