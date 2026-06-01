namespace DocsRag.Api.Models;

/// <summary>One labelled evaluation example: a question + the source that should answer it.</summary>
public record EvalCase(string Question, string ExpectedSource);

/// <summary>Per-question evaluation result.</summary>
public record EvalCaseResult(
    string Question,
    string ExpectedSource,
    bool Hit,
    int Rank,                       // 1-based position of the expected source (0 = missed)
    double TopScore,                // similarity of the #1 retrieved chunk
    IReadOnlyList<string> Retrieved,
    double RetrievalMs,
    bool? Faithful,                 // null unless judging was requested
    string? JudgeReason);

/// <summary>Aggregate evaluation report returned by /eval.</summary>
public record EvalSummary(
    int Total,
    double HitRate,                 // fraction of questions whose expected source was retrieved
    double Mrr,                     // mean reciprocal rank
    double AvgRetrievalMs,
    double? FaithfulnessRate,       // null unless judging was requested
    bool Judged,
    int TopK,
    IReadOnlyList<EvalCaseResult> Cases);
