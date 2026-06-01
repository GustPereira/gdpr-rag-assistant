using DocsRag.Api.Models;

namespace DocsRag.Api.Rag;

/// <summary>
/// Orchestrates ingestion: reads documents, splits them into chunks, generates
/// embeddings, and writes them to Postgres. It is the RAG "input pipeline".
///
/// For the LGPD corpus each file is a single law article, which is already a
/// natural, self-contained chunk.
/// </summary>
public class IngestionService
{
    private const int EmbeddingBatchSize = 64;

    private readonly IEmbeddingService _embeddings;
    private readonly VectorStore _store;
    private readonly IConfiguration _config;
    private readonly ILogger<IngestionService> _log;

    public IngestionService(
        IEmbeddingService embeddings,
        VectorStore store,
        IConfiguration config,
        ILogger<IngestionService> log)
    {
        _embeddings = embeddings;
        _store = store;
        _config = config;
        _log = log;
    }

    /// <summary>Re-indexes every .md/.txt file in the documents folder.</summary>
    public async Task<IngestResult> IngestDirectoryAsync(CancellationToken ct = default)
    {
        var dir = _config["Rag:DocsPath"] ?? "docs-samples";
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Documents folder not found: {dir}");

        var files = Directory
            .EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalChunks = 0;
        var sources = new List<string>();

        foreach (var file in files)
        {
            var source = Path.GetFileName(file);
            var text = await File.ReadAllTextAsync(file, ct);
            totalChunks += await IngestTextInternalAsync(source, text, ct);
            sources.Add(source);
        }

        return new IngestResult(files.Count, totalChunks, sources);
    }

    /// <summary>Indexes a standalone text (e.g. an upload).</summary>
    public async Task<IngestResult> IngestTextAsync(string source, string text, CancellationToken ct = default)
    {
        var chunks = await IngestTextInternalAsync(source, text, ct);
        return new IngestResult(1, chunks, [source]);
    }

    private async Task<int> IngestTextInternalAsync(string source, string text, CancellationToken ct)
    {
        var size = _config.GetValue("Rag:ChunkSize", 500);
        var overlap = _config.GetValue("Rag:ChunkOverlap", 80);

        // The title is the document's "# heading" (e.g. "Art. 7º (LGPD)"), used
        // as the citation label; falls back to the file name.
        var title = ExtractTitle(text) ?? source;

        var chunks = TextChunker.Chunk(text, size, overlap);
        if (chunks.Count == 0)
            return 0;

        var rows = new List<(int, string, float[])>(chunks.Count);

        // Embed in batches to keep request sizes reasonable.
        for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
        {
            var slice = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();
            var embeddings = await _embeddings.EmbedAsync(slice, ct);
            for (var j = 0; j < slice.Count; j++)
                rows.Add((i + j, slice[j], embeddings[j]));
        }

        // Re-indexing: drop the previous version of this source before inserting.
        await _store.DeleteBySourceAsync(source, ct);
        await _store.InsertAsync(source, title, rows, ct);

        _log.LogInformation("Indexed {Source}: {Count} chunks", source, rows.Count);
        return rows.Count;
    }

    // First "# Title" line of the document.
    private static string? ExtractTitle(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("# "))
                return t[2..].Trim();
        }
        return null;
    }
}
