using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChessCore;

namespace ChessGui;

/// <summary>
/// A player's captured pieces, drawn small with the board's own piece shapes: pieces
/// of one kind overlap, kinds are separated by a gap, and a row that is full wraps.
/// </summary>
public sealed class CapturedPiecesView : Control
{
    private const double PieceSize = 24;
    private const double Overlap = 0.5;    // of a piece, between pieces of the same kind
    private const double KindGap = 6;

    private IReadOnlyList<Piece> pieces = [];

    /// <summary>The pieces to draw, already in order (pieces of one kind next to each other).</summary>
    public IReadOnlyList<Piece> Pieces
    {
        get => pieces;
        set
        {
            if (pieces.SequenceEqual(value))
                return;
            pieces = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width;
        var spots = Layout(width);
        double height = spots.Count == 0 ? PieceSize : spots.Max(s => s.Y) + PieceSize;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        var spots = Layout(Bounds.Width);
        for (int i = 0; i < spots.Count; i++)
            PieceArt.Draw(context, pieces[i], new Rect(spots[i], new Size(PieceSize, PieceSize)));
    }

    /// <summary>Where each piece goes: overlapping within a kind, a gap between kinds, wrapping at the edge.</summary>
    private List<Point> Layout(double width)
    {
        var spots = new List<Point>(pieces.Count);
        double x = 0, y = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i > 0)
                x += pieces[i].Type == pieces[i - 1].Type ? PieceSize * (1 - Overlap) : PieceSize + KindGap;
            if (i > 0 && x + PieceSize > width)
            {
                x = 0;
                y += PieceSize + 2;
            }
            spots.Add(new Point(x, y));
        }
        return spots;
    }
}
