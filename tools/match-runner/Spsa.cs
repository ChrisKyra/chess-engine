using System.Globalization;
using ChessCore;
using ChessCore.Uci;

/// <summary>
/// SPSA tuning of engine parameters (simultaneous perturbation stochastic
/// approximation), the way Stockfish's Fishtest tunes its search.
/// </summary>
/// <remarks>
/// The engine (built to expose its parameters as UCI spin options) plays itself in
/// pairs of games -- one opening, both colours. For every pair each parameter is
/// nudged by ±c in a random direction: one engine plays with θ + cΔ, the other with
/// θ − cΔ. The pair's result R (wins minus losses of the θ + cΔ side, −2..2) then
/// moves every parameter by a / c · R · Δ, towards whichever side did better. Over
/// thousands of pairs the noise averages out and the parameters drift towards
/// stronger values. The step sizes shrink as the run goes on, following Fishtest:
/// c_k = c / (k+1)^0.101 and a_k = a / (A + k + 1)^0.602, with A = N / 10, where c and
/// a are set from each parameter's final perturbation c_end and the learning rate
/// r_end = 0.002 for a run of N pairs.
/// <para>
/// The parameter file lists one parameter per line: <c>NAME start min max c_end</c>,
/// where c_end is the perturbation at the end of the run (a few percent of the range
/// is usual). Lines starting with # are comments.
/// </para>
/// </remarks>
static class Spsa
{
    const double Alpha = 0.602, Gamma = 0.101, REnd = 0.002;

    sealed record Param(string Name, double Min, double Max, double CEnd)
    {
        public double Value;
        public double C, A;
    }

    public static async Task<int> RunAsync(Options o)
    {
        var inv = CultureInfo.InvariantCulture;
        var parameters = new List<Param>();
        foreach (var raw in File.ReadAllLines(o.SpsaFile!))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 5) throw new ArgumentException($"SPSA line needs NAME start min max c_end: {line}");
            parameters.Add(new Param(f[0], double.Parse(f[2], inv), double.Parse(f[3], inv), double.Parse(f[4], inv))
            {
                Value = double.Parse(f[1], inv),
            });
        }

        int pairs = Math.Max(1, o.Games / 2);
        double bigA = 0.1 * pairs;
        foreach (var p in parameters)
        {
            p.C = p.CEnd * Math.Pow(pairs, Gamma);
            p.A = REnd * p.CEnd * p.CEnd * Math.Pow(bigA + pairs, Alpha);
        }

        var gate = new object();
        int next = 0, done = 0;
        double resultSum = 0;
        var started = DateTime.Now;
        var random = new Random(1);

        void Report()
        {
            Console.WriteLine($"[{(DateTime.Now - started).TotalMinutes.ToString("0.0", inv)} min] pairs {done}/{pairs}  " +
                              $"mean R {(done > 0 ? resultSum / done : 0).ToString("+0.000;-0.000", inv)}  " +
                              string.Join("  ", parameters.Select(p => $"{p.Name}={p.Value.ToString("0.#", inv)}")));
        }

        async Task Worker()
        {
            await using var plus = new UciEngine(o.Engine1);
            await using var minus = new UciEngine(o.Engine1);
            await plus.StartAsync();
            await minus.StartAsync();
            foreach (var (name, value) in o.EngineOptions)
            {
                await plus.SetOptionAsync(name, value);
                await minus.SetOptionAsync(name, value);
            }
            var adjudicator = new Adjudicator();

            while (true)
            {
                int k;
                var delta = new int[parameters.Count];
                var theta = new double[parameters.Count];
                var ck = new double[parameters.Count];
                lock (gate)
                {
                    if (next >= pairs) return;
                    k = next++;
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        delta[i] = random.Next(2) == 0 ? -1 : 1;
                        theta[i] = parameters[i].Value;
                        ck[i] = parameters[i].C / Math.Pow(k + 1, Gamma);
                    }
                }

                // The two perturbed parameter sets, rounded to what the engine accepts.
                for (int i = 0; i < parameters.Count; i++)
                {
                    var p = parameters[i];
                    string Value(double v) => Math.Round(Math.Clamp(v, p.Min, p.Max)).ToString(inv);
                    await plus.SetOptionAsync(p.Name, Value(theta[i] + ck[i] * delta[i]));
                    await minus.SetOptionAsync(p.Name, Value(theta[i] - ck[i] * delta[i]));
                }

                // One opening from both sides.
                var (_, opening) = Openings.ForMatchGame(k, o.RandomPlies);
                double plusPoints = 0;
                for (int colour = 0; colour < 2; colour++)
                {
                    bool plusWhite = colour == 0;
                    var game = plusWhite ? await GamePlay.PlayAsync(plus, minus, opening, o, adjudicator)
                                         : await GamePlay.PlayAsync(minus, plus, opening, o, adjudicator);
                    plusPoints += game.Result.Outcome switch
                    {
                        GameOutcome.WhiteWins => plusWhite ? 1 : 0,
                        GameOutcome.BlackWins => plusWhite ? 0 : 1,
                        _ => 0.5,
                    };
                }
                double r = 2 * plusPoints - 2;   // wins minus losses of the θ + cΔ side over the pair

                lock (gate)
                {
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = parameters[i];
                        double ak = p.A / Math.Pow(bigA + k + 1, Alpha);
                        p.Value = Math.Clamp(p.Value + ak / ck[i] * r * delta[i], p.Min, p.Max);
                    }
                    done++;
                    resultSum += r;
                    if (done % Math.Max(1, o.ReportEvery / 2) == 0) Report();
                    if (o.SpsaOutput is not null && done % 100 == 0) Write();
                }
            }
        }

        void Write()
        {
            File.WriteAllLines(o.SpsaOutput!, parameters.Select(p =>
                $"{p.Name} {Math.Round(p.Value).ToString(inv)} {p.Min.ToString(inv)} {p.Max.ToString(inv)} {p.CEnd.ToString(inv)}"));
        }

        Console.WriteLine($"SPSA: {parameters.Count} parameters, {pairs} pairs at {o.TimeControl.Describe()}, {o.Concurrency} at once");
        await Task.WhenAll(Enumerable.Range(0, o.Concurrency).Select(_ => Task.Run(Worker)));
        Report();
        if (o.SpsaOutput is not null) Write();
        Console.WriteLine("final values:");
        foreach (var p in parameters)
            Console.WriteLine($"  {p.Name} = {Math.Round(p.Value).ToString(inv)}");
        return 0;
    }
}
