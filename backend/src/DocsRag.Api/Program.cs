// DocsRAG — API
// M0: skeleton + health checks
// M1: ingestion (chunking + embeddings + pgvector)
// M2: retrieval · M3: generation + citations
// M4: chat frontend · M5: streaming (SSE) · M6: evaluation (/eval) + tests

using DocsRag.Api.Endpoints;
using DocsRag.Api.Rag;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=docsrag;Username=docsrag;Password=docsrag";
var embeddingDims = builder.Configuration.GetValue("Rag:EmbeddingDimensions", 1024);

// Npgsql data source with the pgvector `vector` type mapping enabled.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
dataSourceBuilder.UseVector();
builder.Services.AddSingleton(dataSourceBuilder.Build());

// RAG pipeline services. Embedding is registered behind IEmbeddingService (typed
// HttpClient) so consumers depend on the interface, not the concrete Ollama client.
builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<VectorStore>();
builder.Services.AddScoped<IngestionService>();

// Generation strategy chosen by configuration (Strategy Pattern):
//   Rag:Provider = "ollama"    -> local Llama/Qwen, free (default, dev)
//   Rag:Provider = "anthropic" -> Claude (production; requires Anthropic:ApiKey)
// The rest of the app only knows IGenerationService — switching the provider
// changes no other line of code.
var provider = (builder.Configuration["Rag:Provider"] ?? "ollama").ToLowerInvariant();
if (provider == "anthropic")
    builder.Services.AddHttpClient<IGenerationService, AnthropicGenerationService>();
else
    builder.Services.AddHttpClient<IGenerationService, OllamaGenerationService>();

// CORS open for the frontend in dev (`npm run dev`). In production Nginx proxies
// on the same host.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

// Ensure the schema (pgvector extension + table) at startup, with a few retries
// in case Postgres is still finishing its boot.
await EnsureSchemaWithRetryAsync(app, connString, embeddingDims);

// --- Health checks -------------------------------------------------------

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "DocsRag.Api",
    milestone = "M6"
}));

app.MapGet("/health/db", async (NpgsqlDataSource db, VectorStore store) =>
{
    try
    {
        var count = await store.CountAsync();
        return Results.Ok(new { db = "ready", chunks = count });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { db = "unreachable", error = ex.Message });
    }
});

// --- RAG endpoints -------------------------------------------------------

app.MapIngestEndpoints();
app.MapChatEndpoints();
app.MapEvalEndpoints();

app.Run();


// Schema migration with retry (the compose healthcheck already helps, but this
// makes startup more robust).
static async Task EnsureSchemaWithRetryAsync(WebApplication app, string connString, int dims)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    const int maxAttempts = 10;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await VectorStore.EnsureSchemaAsync(connString, dims);
            logger.LogInformation("Schema verified/created successfully.");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning("Postgres not ready yet (attempt {Attempt}/{Max}): {Msg}",
                attempt, maxAttempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

// Needed to use ILogger<Program> in the static method above.
public partial class Program;
