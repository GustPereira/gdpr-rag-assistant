using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocsRag.Api.Models;

namespace DocsRag.Api.Rag;

/// <summary>
/// <see cref="IGenerationService"/> implementation backed by Claude (Anthropic).
/// This is the intended PRODUCTION strategy. It does not run by default — it is
/// enabled when "Rag:Provider" = "anthropic" and "Anthropic:ApiKey" is set.
///
/// Differences from Ollama:
///  - x-api-key + anthropic-version headers;
///  - the system prompt goes in its own field (not inside messages);
///  - the answer comes back in content[].text.
/// </summary>
public class AnthropicGenerationService : IGenerationService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicGenerationService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Rag:GenerationModel"] ?? "claude-sonnet-4-6";

        _http.BaseAddress = new Uri("https://api.anthropic.com/");
        _http.Timeout = TimeSpan.FromMinutes(2);

        var apiKey = config["Anthropic:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            max_tokens = 1024,
            system = systemPrompt,   // Anthropic takes the system prompt separately
            messages = new object[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        using var resp = await _http.PostAsJsonAsync("v1/messages", payload, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic generation failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<AnthropicResponse>(ct);
        return parsed?.Content?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
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
            max_tokens = 1024,
            stream = true,                  // enable Anthropic's SSE stream
            system = Prompts.SystemStream,  // plain prose, no JSON (see Ollama impl)
            messages = new object[]
            {
                new { role = "user", content = Prompts.BuildUserMessage(question, hits) }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(payload),
        };

        using var resp = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic generation failed ({(int)resp.StatusCode}): {body}");
        }

        // Anthropic streams Server-Sent Events. We care about the
        // "content_block_delta" events, whose data carries { delta: { text } }.
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                continue;

            var json = line["data:".Length..].Trim();
            if (json == "[DONE]")
                break;

            StreamEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<StreamEvent>(json);
            }
            catch (JsonException)
            {
                continue;
            }

            var token = evt?.Delta?.Text;
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    // Anthropic response: { "content": [ { "type": "text", "text": "..." } ] }
    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("text")] string? Text);

    // Streamed SSE event: { "type": "content_block_delta", "delta": { "text": "..." } }
    private sealed record StreamEvent(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("delta")] Delta? Delta);

    private sealed record Delta(
        [property: JsonPropertyName("text")] string? Text);
}
