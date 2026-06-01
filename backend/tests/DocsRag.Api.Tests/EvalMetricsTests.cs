using DocsRag.Api.Rag;
using Xunit;

namespace DocsRag.Api.Tests;

// Unit tests for the retrieval metrics used by /eval.
public class EvalMetricsTests
{
    private static readonly string[] Retrieved = ["art-15.md", "art-17.md", "art-21.md", "art-80.md"];

    [Fact]
    public void RankOf_FoundAtTop_ReturnsOne()
        => Assert.Equal(1, EvalMetrics.RankOf(Retrieved, "art-15.md"));

    [Fact]
    public void RankOf_FoundLower_ReturnsItsPosition()
        => Assert.Equal(3, EvalMetrics.RankOf(Retrieved, "art-21.md"));

    [Fact]
    public void RankOf_NotFound_ReturnsZero()
        => Assert.Equal(0, EvalMetrics.RankOf(Retrieved, "art-99.md"));

    [Fact]
    public void RankOf_IsCaseInsensitive()
        => Assert.Equal(2, EvalMetrics.RankOf(Retrieved, "ART-17.MD"));

    [Fact]
    public void Hit_TrueWhenPresent_FalseWhenAbsent()
    {
        Assert.True(EvalMetrics.Hit(Retrieved, "art-17.md"));
        Assert.False(EvalMetrics.Hit(Retrieved, "art-99.md"));
    }

    [Theory]
    [InlineData("art-15.md", 1.0)]      // rank 1 -> 1/1
    [InlineData("art-17.md", 0.5)]      // rank 2 -> 1/2
    [InlineData("art-21.md", 1.0 / 3)]  // rank 3 -> 1/3
    [InlineData("art-99.md", 0.0)]      // missing -> 0
    public void ReciprocalRank_MatchesPosition(string expected, double score)
        => Assert.Equal(score, EvalMetrics.ReciprocalRank(Retrieved, expected), precision: 6);
}
