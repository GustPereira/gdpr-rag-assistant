using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocsRag.Api.Models;

namespace DocsRag.Api.Rag;

/// <summary>
/// <see cref="IGenerationService"/> implementation backed by a local Llama/Qwen
/// model via Ollama (free, offline). This is the default strategy in development.
/// Supports both one-shot (JSON) and token-by-token streaming (M5) generation.
/// </summary>
public class OllamaGenerationService : IGenerationService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaGenerationService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Rag:GenerationModel"] ?? "qwen2.5:3b";

        var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434";
        _http.BaseAddress = new Uri(endpoint);
        // Generation on CPU can be slow; allow a generous timeout.
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            // Constrains the output to valid JSON (grammar-constrained decoding).
            // The exact shape is described in the system prompt.
            format = "json",
            // temperature 0 = more deterministic answers, faithful to the context
            // (important with small models, which tend to drift).
            options = new { temperature = 0.0 },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var resp = await _http.PostAsJsonAsync("/api/chat", payload, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama generation failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(ct);
        return parsed?.Message?.Content?.Trim() ?? string.Empty;
    }

    public Task<string> GenerateAsync(
        string question,
        IReadOnlyList<ChunkHit> hits,
        CancellationToken ct = default)
        => CompleteAsync(Prompts.System, Prompts.BuildUserMessage(question, hits), ct);

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string question,
        IReadOnlyList<ChunkHit> hits,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            stream = true,            // ask Ollama to stream the response
            // No "format: json" here — we want plain prose to stream nicely.
            options = new { temperature = 0.0 },
            messages = new object[]
            {
                new { role = "system", content = Prompts.SystemStream },
                new { role = "user", content = Prompts.BuildUserMessage(question, hits) }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(payload),
        };

        // ResponseHeadersRead: start reading as soon as the headers arrive, without
        // buffering the whole body — essential for streaming.
        using var resp = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama generation failed ({(int)resp.StatusCode}): {body}");
        }

        // Ollama streams NDJSON: one JSON object per line, each with a partial
        // { "message": { "content": "<token>" }, "done": false }.
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatResponse>(line);
            }
            catch (JsonException)
            {
                continue; // ignore any malformed line
            }

            var token = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(token))
                yield return token;

            if (chunk?.Done == true)
                break;
        }
    }

    // Ollama response: { "message": { "role": "assistant", "content": "..." }, "done": bool }
    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("done")] bool Done);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")] string? Content);
}
