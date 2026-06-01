using System.Text.Json;
using DocsRag.Api.Models;
using DocsRag.Api.Rag;

namespace DocsRag.Api.Endpoints;

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // camelCase, matching the frontend types (ref/source/title/score/text).
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this WebApplication app)
    {
        // M3: full RAG — embed the question, search the top-k articles, build the
        // prompt with the context, and ask the LLM for a structured JSON answer
        // plus the cited articles.
        app.MapPost("/ask", async (
            AskRequest req,
            IEmbeddingService embeddings,
            VectorStore store,
            IGenerationService generation,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "Field 'question' is required." });

            var topK = req.TopK ?? config.GetValue("Rag:TopK", 4);

            // 1) Question -> embedding -> semantic search
            var queryVector = await embeddings.EmbedOneAsync(req.Question, ct);
            var hits = await store.SearchAsync(queryVector, topK, ct);

            // No articles retrieved: there is nothing to ground an answer on.
            if (hits.Count == 0)
                return Results.Ok(new AnswerResponse(
                    req.Question,
                    new StructuredAnswer("I couldn't find any articles related to this question.", []),
                    []));

            // 2) Context + question -> LLM -> structured JSON answer
            var raw = await generation.GenerateAsync(req.Question, hits, ct);
            var answer = ParseAnswer(raw);

            // 3) Build the citations (full article text) with the prompt numbering ([1]...)
            var citations = hits
                .Select((h, i) => new Citation(
                    Ref: i + 1,
                    Source: h.Source,
                    Title: h.Title,
                    Score: h.Score,
                    Text: StripHeading(h.Content)))
                .ToList();

            return Results.Ok(new AnswerResponse(req.Question, answer, citations));
        });

        // M5: streaming RAG over Server-Sent Events (SSE). Same pipeline as /ask,
        // but the answer is streamed token-by-token so the UI feels responsive even
        // with a slow local model. Event sequence:
        //   1) "citations" — the retrieved articles (sent immediately, before the LLM)
        //   2) "token" ... — the answer in plain prose, piece by piece
        //   3) "done"      — end of stream
        // (On no hits we send a single "token" with a friendly message, then "done".)
        app.MapPost("/ask/stream", async (
            AskRequest req,
            IEmbeddingService embeddings,
            VectorStore store,
            IGenerationService generation,
            IConfiguration config,
            HttpContext http,
            CancellationToken ct) =>
        {
            var res = http.Response;
            res.Headers.ContentType = "text/event-stream";
            res.Headers.CacheControl = "no-cache";
            res.Headers["X-Accel-Buffering"] = "no"; // tell nginx not to buffer

            if (string.IsNullOrWhiteSpace(req.Question))
            {
                await WriteEventAsync(res, "error", new { message = "Field 'question' is required." }, ct);
                return;
            }

            var topK = req.TopK ?? config.GetValue("Rag:TopK", 4);

            // 1) Retrieval (same as /ask)
            var queryVector = await embeddings.EmbedOneAsync(req.Question, ct);
            var hits = await store.SearchAsync(queryVector, topK, ct);

            // 2) Send the citations right away — the cards appear while the LLM thinks.
            var citations = hits
                .Select((h, i) => new Citation(
                    Ref: i + 1,
                    Source: h.Source,
                    Title: h.Title,
                    Score: h.Score,
                    Text: StripHeading(h.Content)))
                .ToList();
            await WriteEventAsync(res, "citations", citations, ct);

            if (hits.Count == 0)
            {
                await WriteEventAsync(res, "token",
                    "I couldn't find any articles related to this question.", ct);
                await WriteEventAsync(res, "done", new { }, ct);
                return;
            }

            // 3) Stream the answer token-by-token.
            try
            {
                await foreach (var token in generation.GenerateStreamAsync(req.Question, hits, ct))
                {
                    await WriteEventAsync(res, "token", token, ct);
                    await res.Body.FlushAsync(ct); // push each token to the client now
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await WriteEventAsync(res, "error", new { message = ex.Message }, ct);
                return;
            }

            await WriteEventAsync(res, "done", new { }, ct);
        });

        // Retrieval-only endpoint, handy for debugging search without paying the
        // generation cost.
        app.MapPost("/search", async (
            AskRequest req,
            IEmbeddingService embeddings,
            VectorStore store,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "Field 'question' is required." });

            var topK = req.TopK ?? config.GetValue("Rag:TopK", 4);
            var queryVector = await embeddings.EmbedOneAsync(req.Question, ct);
            var hits = await store.SearchAsync(queryVector, topK, ct);

            return Results.Ok(new RetrievalResponse(req.Question, hits));
        });
    }

    // Writes one SSE message: an "event:" line, a JSON "data:" line, and a blank
    // line terminator. The payload is always JSON-encoded so tokens with newlines
    // or quotes travel safely on a single data line.
    private static async Task WriteEventAsync(
        HttpResponse res, string eventName, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, WebJson);
        await res.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
    }

    // Parses the model's JSON output. If the model returns malformed JSON (small
    // models occasionally do), fall back to showing the raw text as the answer.
    private static StructuredAnswer ParseAnswer(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<StructuredAnswer>(raw, JsonOpts);
            if (parsed is not null)
                return parsed with { CitedRefs = parsed.CitedRefs ?? [] };
        }
        catch (JsonException)
        {
            // fall through to the fallback below
        }

        return new StructuredAnswer(raw.Trim(), []);
    }

    // Removes the leading "# heading" line so the card shows just the article text.
    private static string StripHeading(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith("# "))).Trim();
    }
}
