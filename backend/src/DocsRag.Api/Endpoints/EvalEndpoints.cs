using System.Diagnostics;
using System.Text.Json;
using DocsRag.Api.Models;
using DocsRag.Api.Rag;

namespace DocsRag.Api.Endpoints;

/// <summary>
/// M6: a small evaluation harness. Runs the golden-set questions through retrieval
/// and reports quality metrics (hit-rate, MRR, latency) and — optionally — an
/// LLM-as-judge faithfulness score. This is what turns "it seems to work" into
/// numbers you can track when you change the model, chunking, or top-k.
/// </summary>
public static class EvalEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void MapEvalEndpoints(this WebApplication app)
    {
        // GET /eval?topK=4&judge=false&limit=0
        //   topK  — how many chunks to retrieve per question (defaults to config).
        //   judge — when true, also generates an answer and grades its faithfulness
        //           with the LLM. Off by default: on CPU it is slow (~1-2 min/question).
        //   limit — evaluate only the first N questions (0 = all). Handy for a quick
        //           smoke test of the judge path without running the whole set.
        app.MapGet("/eval", async (
            IEmbeddingService embeddings,
            VectorStore store,
            IGenerationService generation,
            IConfiguration config,
            int? topK,
            bool? judge,
            int? limit,
            CancellationToken ct) =>
        {
            var k = topK ?? config.GetValue("Rag:TopK", 4);
            var doJudge = judge ?? false;
            var cases = limit is > 0 ? EvalDataset.Gdpr.Take(limit.Value).ToList() : EvalDataset.Gdpr;
            var results = new List<EvalCaseResult>();

            foreach (var c in cases)
            {
                // --- Retrieval (timed) ---
                var sw = Stopwatch.StartNew();
                var queryVector = await embeddings.EmbedOneAsync(c.Question, ct);
                var hits = await store.SearchAsync(queryVector, k, ct);
                sw.Stop();

                var retrieved = hits.Select(h => h.Source).ToList();
                var rank = EvalMetrics.RankOf(retrieved, c.ExpectedSource);

                // --- Optional faithfulness judging ---
                bool? faithful = null;
                string? reason = null;
                if (doJudge && hits.Count > 0)
                {
                    var raw = await generation.GenerateAsync(c.Question, hits, ct);
                    var answer = ExtractAnswer(raw);
                    var verdict = await generation.CompleteAsync(
                        Prompts.JudgeSystem,
                        Prompts.BuildJudgeMessage(c.Question, hits, answer),
                        ct);
                    (faithful, reason) = ParseJudge(verdict);
                }

                results.Add(new EvalCaseResult(
                    Question: c.Question,
                    ExpectedSource: c.ExpectedSource,
                    Hit: rank > 0,
                    Rank: rank,
                    TopScore: hits.Count > 0 ? hits[0].Score : 0,
                    Retrieved: retrieved,
                    RetrievalMs: Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                    Faithful: faithful,
                    JudgeReason: reason));
            }

            var judged = results.Where(r => r.Faithful.HasValue).ToList();

            var summary = new EvalSummary(
                Total: results.Count,
                HitRate: Math.Round(results.Average(r => r.Hit ? 1.0 : 0.0), 3),
                Mrr: Math.Round(results.Average(r => r.Rank == 0 ? 0.0 : 1.0 / r.Rank), 3),
                AvgRetrievalMs: Math.Round(results.Average(r => r.RetrievalMs), 1),
                FaithfulnessRate: judged.Count > 0
                    ? Math.Round(judged.Average(r => r.Faithful!.Value ? 1.0 : 0.0), 3)
                    : null,
                Judged: doJudge,
                TopK: k,
                Cases: results);

            return Results.Ok(summary);
        });
    }

    // Pulls the answer text out of the model's JSON ({ "answer": "..." }); falls
    // back to the raw string if it is not valid JSON.
    private static string ExtractAnswer(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<StructuredAnswer>(raw, JsonOpts);
            if (!string.IsNullOrWhiteSpace(parsed?.Answer))
                return parsed!.Answer;
        }
        catch (JsonException) { /* fall through */ }
        return raw.Trim();
    }

    // Parses the judge's JSON ({ "faithful": bool, "reason": "..." }).
    private static (bool? faithful, string? reason) ParseJudge(string raw)
    {
        try
        {
            var v = JsonSerializer.Deserialize<JudgeVerdict>(raw, JsonOpts);
            if (v is not null)
                return (v.Faithful, v.Reason);
        }
        catch (JsonException) { /* fall through */ }
        return (null, "could not parse judge output");
    }

    private sealed record JudgeVerdict(bool Faithful, string? Reason);
}
