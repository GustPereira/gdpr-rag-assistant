using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DocsRag.Api.Rag;

/// <summary>
/// Generates embeddings by calling the local Ollama server (free, offline). An
/// embedding is a vector of numbers representing the "meaning" of a text; similar
/// texts produce nearby vectors, which is what enables semantic search.
///
/// Uses Ollama's /api/embed endpoint, which accepts a batch of texts.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public EmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Rag:EmbeddingModel"] ?? "bge-m3";

        var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434";
        _http.BaseAddress = new Uri(endpoint);
        // The first call loads the model into memory; allow extra time.
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    // Local stack: there is no API key to configure.
    public bool IsConfigured => true;

    /// <summary>Generates embeddings for several texts in a single call.</summary>
    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
            return [];

        var payload = new { model = _model, input = inputs };
        using var resp = await _http.PostAsJsonAsync("/api/embed", payload, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama embedding failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<EmbedResponse>(ct);
        return parsed!.Embeddings;
    }

    public async Task<float[]> EmbedOneAsync(string input, CancellationToken ct = default)
        => (await EmbedAsync([input], ct))[0];

    // Ollama response: { "model": "...", "embeddings": [ [..], [..] ] }
    private sealed record EmbedResponse(
        [property: JsonPropertyName("embeddings")] List<float[]> Embeddings);
}
