using System.Text.Json.Serialization;

namespace DocsRag.Api.Models;

/// <summary>Result of an ingestion operation.</summary>
public record IngestResult(int Files, int Chunks, IReadOnlyList<string> Sources);

/// <summary>A chunk retrieved by the search, with its similarity score.</summary>
public record ChunkHit(
    long Id,
    string Source,
    string Title,
    int ChunkIndex,
    string Content,
    double Score);

/// <summary>Body of POST /ask.</summary>
public record AskRequest(string Question, int? TopK = null);

/// <summary>Response of /ask in M2 (retrieval only, no LLM yet).</summary>
public record RetrievalResponse(string Question, IReadOnlyList<ChunkHit> Hits);

/// <summary>A source (LGPD article) returned with the answer. Carries the full text.</summary>
public record Citation(int Ref, string Source, string Title, double Score, string Text);

/// <summary>The structured answer the model produces (parsed from its JSON output).</summary>
public record StructuredAnswer(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("cited_refs")] List<int> CitedRefs);

/// <summary>Full RAG response: structured answer + the articles that were retrieved.</summary>
public record AnswerResponse(string Question, StructuredAnswer Answer, IReadOnlyList<Citation> Citations);
