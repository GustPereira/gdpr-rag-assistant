using DocsRag.Api.Models;

namespace DocsRag.Api.Rag;

/// <summary>
/// Abstracts the LLM provider used for generation. Lets us switch between Ollama
/// (local, free) and Anthropic/Claude (production) without touching the endpoints,
/// the retrieval, or the frontend — just register a different implementation in DI.
/// (Strategy Pattern.)
/// </summary>
public interface IGenerationService
{
    /// <summary>
    /// General-purpose single-shot completion in JSON mode (the provider is asked to
    /// return one JSON object). Used both by <see cref="GenerateAsync"/> and by the
    /// /eval LLM-as-judge. Keeping it on the interface means the judge works with any
    /// provider (Ollama or Claude), not just the local one.
    /// </summary>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);

    /// <summary>Generates the answer from the question and the retrieved snippets.</summary>
    Task<string> GenerateAsync(
        string question,
        IReadOnlyList<ChunkHit> hits,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="GenerateAsync"/> but yields the answer token-by-token as
    /// the model produces it (M5). The answer is plain prose with inline [n]
    /// citations (NOT the JSON shape used by <see cref="GenerateAsync"/>), because
    /// streaming partial JSON to the UI would look broken.
    /// </summary>
    IAsyncEnumerable<string> GenerateStreamAsync(
        string question,
        IReadOnlyList<ChunkHit> hits,
        CancellationToken ct = default);
}
