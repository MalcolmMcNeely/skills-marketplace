namespace Harness;

public sealed record CaseResult(string CaseId, IReadOnlyList<RunScore> Runs)
{
    public int Valid => Runs.Count(r => r.CountsForFiring);
    public int Passed => Runs.Count(r => r.FiringPassed);
    public double Rate => Valid == 0 ? 0 : (double)Passed / Valid;
}

public sealed record PoolResult(
    IReadOnlyList<CaseResult> Cases,
    int PooledValid,
    int PooledPassed,
    double PooledRate,
    int GateK,
    bool PooledGatePassed,
    IReadOnlyList<string> ZeroFloorBreaches,
    SpendTotal Cost)
{
    public bool Passed => PooledGatePassed && ZeroFloorBreaches.Count == 0;
}

public static class Pooling
{
    public const int MinRunsForZeroFloor = 5;

    /// <summary>Rule 1 pooled rate at or above the calibrated floor, AND rule 2 no case at zero.</summary>
    public static PoolResult Pool(IReadOnlyList<CaseResult> cases, double pGood, double alpha = 0.05)
    {
        var valid = cases.Sum(c => c.Valid);
        var passed = cases.Sum(c => c.Passed);
        var gate = GateK(valid, pGood, alpha);
        var breaches = cases
            .Where(c => c.Valid >= MinRunsForZeroFloor && c.Passed == 0)
            .Select(c => c.CaseId)
            .ToList();

        return new PoolResult(cases, valid, passed,
            valid == 0 ? 0 : (double)passed / valid,
            gate, passed >= gate, breaches,
            SpendTotal.Of(cases.SelectMany(c => c.Runs).Select(r => r.Cost)));
    }

    /// <summary>
    /// Wilson score interval. #12 output 1 asks for p_good with an interval, and baseline-test-first.md
    /// already reports one, so the two numbers are read the same way. Wilson and not normal-approximate,
    /// because a rate near 0 or 1 on 60 runs breaks the normal one: 0 of 15 gave an upper bound of 0.204
    /// where the normal approximation gives 0.
    /// </summary>
    public static (double Low, double High) Wilson(int successes, int n, double z = 1.96)
    {
        if (n == 0) return (0, 1);
        var p = (double)successes / n;
        var z2 = z * z;
        var denom = 1 + z2 / n;
        var centre = (p + z2 / (2.0 * n)) / denom;
        var half = z / denom * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n));
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>gate_k = max { k : P(Binom(N, p) &lt; k) &lt;= alpha }. The 5th percentile of the healthy distribution.</summary>
    public static int GateK(int n, double p, double alpha = 0.05)
    {
        if (n == 0) return 0;
        double cumulative = 0;
        var best = 0;
        for (var k = 0; k <= n; k++)
        {
            if (cumulative <= alpha) best = k; else break;
            cumulative += Pmf(n, k, p);
        }
        return best;
    }

    public static double Pmf(int n, int k, double p)
    {
        var logC = LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
        var log = logC + k * Math.Log(p) + (n - k) * Math.Log(1 - p);
        return Math.Exp(log);
    }

    private static double LogGamma(double x)
    {
        double[] c = [76.18009172947146, -86.50532032941677, 24.01409824083091,
                      -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5];
        var y = x;
        var tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        var ser = 1.000000000190015;
        for (var j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}
