namespace DocsRag.Api.Rag;

/// <summary>
/// Abstracts the embedding provider (text -> vector). Mirrors the
/// <see cref="IGenerationService"/> design: depending on an interface (not the
/// concrete Ollama client) lets the ingestion/retrieval code be unit-tested with a
/// fake, and lets the provider be swapped without touching its consumers.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>True when the provider is ready (the local stack needs no API key).</summary>
    bool IsConfigured { get; }

    /// <summary>Generates embeddings for several texts in a single call.</summary>
    Task<List<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default);

    /// <summary>Generates the embedding for a single text.</summary>
    Task<float[]> EmbedOneAsync(string input, CancellationToken ct = default);
}
