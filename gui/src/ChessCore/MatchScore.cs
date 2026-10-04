namespace ChessCore;

/// <summary>Wins, draws and losses from one player's point of view, with a score and an Elo estimate.</summary>
public sealed class MatchScore
{
    public int Wins { get; private set; }

    public int Draws { get; private set; }

    public int Losses { get; private set; }

    public int Games => Wins + Draws + Losses;

    /// <summary>Points per game, 0..1 (a draw is half a point).</summary>
    public double Score => Games == 0 ? 0 : (Wins + 0.5 * Draws) / Games;

    /// <summary>Records a game: 1 for a win, 0.5 for a draw, 0 for a loss.</summary>
    public void Add(double points)
    {
        if (points >= 1)
            Wins++;
        else if (points <= 0)
            Losses++;
        else
            Draws++;
    }

    /// <summary>
    /// The Elo difference the score implies, with a 95% confidence margin. Null when it
    /// can't be computed: no games yet, or a 0% / 100% score (infinitely stronger).
    /// </summary>
    public (double Elo, double Margin)? EloEstimate()
    {
        double s = Score;
        if (Games == 0 || s <= 0 || s >= 1)
            return null;

        // Standard error of the mean score over the per-game results.
        double variance = (Wins * Square(1 - s) + Draws * Square(0.5 - s) + Losses * Square(s)) / Games;
        double error = Math.Sqrt(variance / Games);
        const double Epsilon = 1e-9;
        double low = EloFromScore(Math.Max(s - 1.96 * error, Epsilon));
        double high = EloFromScore(Math.Min(s + 1.96 * error, 1 - Epsilon));
        return (EloFromScore(s), (high - low) / 2);
    }

    private static double EloFromScore(double score) => -400 * Math.Log10(1 / score - 1);

    private static double Square(double x) => x * x;
}
