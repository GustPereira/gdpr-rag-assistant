namespace DocsRag.Api.Rag;

/// <summary>
/// Pure (no I/O) retrieval-quality metrics, kept separate so they can be unit-tested
/// in isolation. Given the list of retrieved sources (in rank order) and the source
/// that SHOULD have been retrieved, they answer "did we find it, and how high?".
/// </summary>
public static class EvalMetrics
{
    /// <summary>
    /// 1-based position of <paramref name="expected"/> in <paramref name="retrieved"/>,
    /// or 0 if it is not present. (Rank 1 = the top result.)
    /// </summary>
    public static int RankOf(IReadOnlyList<string> retrieved, string expected)
    {
        for (var i = 0; i < retrieved.Count; i++)
            if (string.Equals(retrieved[i], expected, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return 0;
    }

    /// <summary>True if the expected source appears anywhere in the retrieved list (hit@k).</summary>
    public static bool Hit(IReadOnlyList<string> retrieved, string expected)
        => RankOf(retrieved, expected) > 0;

    /// <summary>
    /// Reciprocal rank: 1/rank if found, else 0. Averaged across the dataset this
    /// gives MRR (Mean Reciprocal Rank) — rewards ranking the right source higher.
    /// </summary>
    public static double ReciprocalRank(IReadOnlyList<string> retrieved, string expected)
    {
        var rank = RankOf(retrieved, expected);
        return rank == 0 ? 0.0 : 1.0 / rank;
    }
}
