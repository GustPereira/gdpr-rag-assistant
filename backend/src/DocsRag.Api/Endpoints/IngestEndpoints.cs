using DocsRag.Api.Rag;

namespace DocsRag.Api.Endpoints;

public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this WebApplication app)
    {
        // Re-indexes every document in the configured folder (docs-samples / /docs).
        app.MapPost("/ingest", async (IngestionService ingestion, CancellationToken ct) =>
        {
            var result = await ingestion.IngestDirectoryAsync(ct);
            return Results.Ok(result);
        });

        // Indexes an uploaded file (multipart/form-data, field "file").
        app.MapPost("/ingest/upload", async (HttpRequest req, IngestionService ingestion, CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "Send multipart/form-data with a 'file' field." });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "File 'file' is missing or empty." });

            using var reader = new StreamReader(file.OpenReadStream());
            var text = await reader.ReadToEndAsync(ct);

            var result = await ingestion.IngestTextAsync(file.FileName, text, ct);
            return Results.Ok(result);
        });
    }
}
