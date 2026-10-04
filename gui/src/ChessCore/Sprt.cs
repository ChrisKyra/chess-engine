using System.Globalization;

namespace ChessCore;

public enum SprtDecision
{
    /// <summary>Not enough evidence yet either way: keep playing.</summary>
    Continue,

    /// <summary>Engine 1 is at most Elo0 stronger (H0): the change did not help enough.</summary>
    H0Accepted,

    /// <summary>Engine 1 is at least Elo1 stronger (H1): the change helps.</summary>
    H1Accepted,
}

/// <summary>
/// The hypotheses and error rates of a sequential probability ratio test. H0 says Engine 1
/// is <see cref="Elo0"/> stronger than Engine 2, H1 that it is <see cref="Elo1"/> stronger;
/// <see cref="Alpha"/> is the chance of accepting H1 when H0 is true, <see cref="Beta"/> the
/// chance of accepting H0 when H1 is true.
/// </summary>
public sealed record SprtSettings
{
    public double Elo0 { get; init; }

    public double Elo1 { get; init; } = 10;

    public double Alpha { get; init; } = 0.05;

    public double Beta { get; init; } = 0.05;

    /// <summary>The log-likelihood ratio at or below which H0 is accepted.</summary>
    public double LowerBound => Math.Log(Beta / (1 - Alpha));

    /// <summary>The log-likelihood ratio at or above which H1 is accepted.</summary>
    public double UpperBound => Math.Log((1 - Beta) / Alpha);

    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        return $"H0 {Elo0.ToString("0.#", inv)} Elo, H1 {Elo1.ToString("0.#", inv)} Elo, α {Alpha.ToString("0.###", inv)}, β {Beta.ToString("0.###", inv)}";
    }
}

/// <summary>
/// A sequential probability ratio test on game pairs (pentanomial model), the way
/// Fishtest and fastchess test engine changes: instead of fixing the number of games in
/// advance, it plays until the results are clear enough to decide between "the change
/// gains at least Elo1" and "it gains at most Elo0".
/// </summary>
/// <remarks>
/// Each pair is one opening played from both sides, so it scores 0, ½, 1, 1½ or 2 points
/// for Engine 1. Counting pairs instead of single games removes the advantage an opening
/// gives one colour, which would otherwise show up as noise. The log-likelihood ratio is
/// the generalised SPRT approximation for logistic Elo:
/// LLR = N · (s1 − s0) · (2·mean − s0 − s1) / (2·variance), with the mean and variance of
/// the per-pair score (as a fraction of the 2 points) and s0, s1 the expected scores at
/// Elo0 and Elo1.
/// </remarks>
public sealed class Sprt(SprtSettings settings)
{
    private readonly int[] pairs = new int[5];

    public SprtSettings Settings { get; } = settings;

    /// <summary>Completed pairs by Engine 1's points: [0] lost both … [4] won both.</summary>
    public IReadOnlyList<int> PairCounts => pairs;

    public int Pairs => pairs.Sum();

    /// <summary>Records a finished pair from Engine 1's points in its two games (0 to 2).</summary>
    public void AddPair(double engine1Points)
    {
        int index = (int)Math.Round(engine1Points * 2);
        pairs[Math.Clamp(index, 0, 4)]++;
    }

    public double Llr
    {
        get
        {
            int n = Pairs;
            if (n == 0)
                return 0;
            double mean = 0;
            for (int i = 0; i < 5; i++)
                mean += pairs[i] * (i / 4.0);
            mean /= n;
            double variance = 0;
            for (int i = 0; i < 5; i++)
                variance += pairs[i] * Square(i / 4.0 - mean);
            variance /= n;
            // Every pair alike says nothing about the spread yet.
            if (variance <= 0)
                return 0;

            double s0 = ExpectedScore(Settings.Elo0);
            double s1 = ExpectedScore(Settings.Elo1);
            return n * (s1 - s0) * (2 * mean - s0 - s1) / (2 * variance);
        }
    }

    public SprtDecision Decision
    {
        get
        {
            double llr = Llr;
            if (llr >= Settings.UpperBound)
                return SprtDecision.H1Accepted;
            if (llr <= Settings.LowerBound)
                return SprtDecision.H0Accepted;
            return SprtDecision.Continue;
        }
    }

    /// <summary>"LLR 1.23 (−2.94, 2.94)".</summary>
    public string DescribeLlr()
    {
        var inv = CultureInfo.InvariantCulture;
        return $"LLR {Llr.ToString("0.00", inv)} ({Settings.LowerBound.ToString("0.00", inv)}, {Settings.UpperBound.ToString("0.00", inv)})";
    }

    /// <summary>The expected score of a player that is <paramref name="elo"/> stronger.</summary>
    public static double ExpectedScore(double elo) => 1 / (1 + Math.Pow(10, -elo / 400));

    private static double Square(double x) => x * x;
}
