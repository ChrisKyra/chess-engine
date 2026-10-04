using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ChessCore;

namespace ChessGui;

/// <summary>
/// Vector chess pieces designed in a 45×45 box, so they stay crisp at any square size
/// and don't depend on the system having a font with chess glyphs.
/// </summary>
internal static class PieceArt
{
    private const double DesignSize = 45;

    private static readonly IBrush LightFill = new ImmutableSolidColorBrush(Color.Parse("#FAFAFA"));
    private static readonly IBrush DarkFill = new ImmutableSolidColorBrush(Color.Parse("#2B2B2B"));
    private static readonly IImmutableBrush Ink = new ImmutableSolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IImmutableBrush Highlight = new ImmutableSolidColorBrush(Color.Parse("#E8E8E8"));
    private static readonly IPen Outline = new ImmutablePen(Ink, 1.5, lineJoin: PenLineJoin.Round);
    private static readonly IPen DarkDetail = new ImmutablePen(Ink, 1.3, lineCap: PenLineCap.Round);
    private static readonly IPen LightDetail = new ImmutablePen(Highlight, 1.3, lineCap: PenLineCap.Round);

    private static Dictionary<PieceType, Shape>? shapes;

    public static void Draw(DrawingContext context, Piece piece, Rect square)
    {
        if (piece.IsEmpty)
            return;

        shapes ??= CreateShapes();
        var shape = shapes[piece.Type];
        bool white = piece.Color == PieceColor.White;
        double scale = square.Width / DesignSize;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(square.X, square.Y)))
        {
            context.DrawGeometry(white ? LightFill : DarkFill, Outline, shape.Body);
            if (shape.Details is not null)
                context.DrawGeometry(null, white ? DarkDetail : LightDetail, shape.Details);
            if (shape.Dots is not null)
                context.DrawGeometry(white ? Ink : Highlight, null, shape.Dots);
        }
    }

    // Geometry needs the platform's render interface, so it is built on first use, not at type load.
    private static Dictionary<PieceType, Shape> CreateShapes() => new()
    {
        [PieceType.Pawn] = new Shape(
            "M22.5 9.5 a4.5 4.5 0 0 0 -3.1 7.75 C17.2 18.5 16 20 16 21.5 h3.2 C18.8 26.5 15.5 29.5 12.5 32 v6.5 h20 V32 " +
            "C29.5 29.5 26.2 26.5 25.8 21.5 H29 C29 20 27.8 18.5 25.6 17.25 A4.5 4.5 0 0 0 22.5 9.5 Z"),

        [PieceType.Rook] = new Shape(
            "M9.5 39 H35.5 V35.5 H33 V32 L30.5 29 V17.5 L33 15 V9 H28.5 V11.5 H25 V9 H20 V11.5 H16.5 V9 H12 V15 " +
            "L14.5 17.5 V29 L12 32 V35.5 H9.5 Z",
            details: "M12 15 H33 M14.5 17.5 H30.5 M14.5 29 H30.5 M12 32 H33 M12 35.5 H33"),

        [PieceType.Knight] = new Shape(
            "M14 39 H35 C35.5 29 34.5 20.5 30.5 15 C28 11.5 25 10.2 22 10 L20.8 6.5 L18.3 10 L15.5 7.8 L15 11.8 " +
            "C11.5 14.5 9 19 8 23.5 C7.6 25.8 9.5 27.4 11.6 26.3 L14.2 24.9 C15.8 25.8 17.6 25.3 19 24 L23.8 21.3 " +
            "C23 27 17 29.5 14 33.5 Z",
            details: "M28 13.5 C31.8 18.5 33 26 32.8 36",
            dots: "M17.5 15.8 a1.3 1.3 0 1 1 -2.6 0 a1.3 1.3 0 1 1 2.6 0 Z M10.8 23.3 a0.6 0.6 0 1 1 -1.2 0 a0.6 0.6 0 1 1 1.2 0 Z"),

        [PieceType.Bishop] = new Shape(
            "M25 8 a2.5 2.5 0 1 1 -5 0 a2.5 2.5 0 1 1 5 0 Z " +
            "M22.5 10.8 C16.5 15 13.8 20.5 15 26 C15.8 29.3 18 31 18 31 H27 C27 31 29.2 29.3 30 26 C31.2 20.5 28.5 15 22.5 10.8 Z " +
            "M17 31 H28 V34 H17 Z " +
            "M12 34 H33 C34.5 34 35.5 35.5 35.5 37 V39 H9.5 V37 C9.5 35.5 10.5 34 12 34 Z",
            details: "M25.5 16.5 L20 22"),

        [PieceType.Queen] = new Shape(
            "M10.2 12.5 a2.2 2.2 0 1 1 -4.4 0 a2.2 2.2 0 1 1 4.4 0 Z M17.4 9.8 a2.2 2.2 0 1 1 -4.4 0 a2.2 2.2 0 1 1 4.4 0 Z " +
            "M24.7 8.5 a2.2 2.2 0 1 1 -4.4 0 a2.2 2.2 0 1 1 4.4 0 Z M32 9.8 a2.2 2.2 0 1 1 -4.4 0 a2.2 2.2 0 1 1 4.4 0 Z " +
            "M39.2 12.5 a2.2 2.2 0 1 1 -4.4 0 a2.2 2.2 0 1 1 4.4 0 Z " +
            "M10.5 28 L8.3 14.6 L15 23.5 L15.3 11.9 L20.3 22.5 L22.5 10.7 L24.7 22.5 L29.7 11.9 L30 23.5 L36.7 14.6 L34.5 28 Z " +
            "M10.5 28 C14 30 31 30 34.5 28 L33 33 C28 34.5 17 34.5 12 33 Z " +
            "M12 33 C17 34.5 28 34.5 33 33 L34.5 38.5 C28 40 17 40 10.5 38.5 Z"),

        [PieceType.King] = new Shape(
            "M21 4 H24 V7.5 H27.5 V10.5 H24 V14 H21 V10.5 H17.5 V7.5 H21 Z " +
            "M11.5 31 C5.5 25 7 16.5 14 16.5 C18 16.5 21 19.5 22.5 23 C24 19.5 27 16.5 31 16.5 C38 16.5 39.5 25 33.5 31 Z " +
            "M22.5 14 C19.5 14 18 17 19.2 19.8 L22.5 25 L25.8 19.8 C27 17 25.5 14 22.5 14 Z " +
            "M11.5 31 C16 33 29 33 33.5 31 L32.5 35 C27 36.5 18 36.5 12.5 35 Z " +
            "M12.5 35 C18 36.5 27 36.5 32.5 35 L34 39 C27 40.5 18 40.5 11 39 Z"),
    };

    private sealed class Shape(string body, string? details = null, string? dots = null)
    {
        // "F1" selects the non-zero fill rule so overlapping sub-paths stay solid.
        public Geometry Body { get; } = StreamGeometry.Parse("F1 " + body);

        public Geometry? Details { get; } = details is null ? null : StreamGeometry.Parse(details);

        public Geometry? Dots { get; } = dots is null ? null : StreamGeometry.Parse("F1 " + dots);
    }
}
