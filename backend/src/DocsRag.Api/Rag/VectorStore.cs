using DocsRag.Api.Models;
using Npgsql;
using Pgvector;

namespace DocsRag.Api.Rag;

/// <summary>
/// Stores and searches chunks in Postgres using the pgvector extension.
/// Each row of the `chunks` table holds the text + its embedding (vector).
/// </summary>
public class VectorStore
{
    private readonly NpgsqlDataSource _db;

    public VectorStore(NpgsqlDataSource db) => _db = db;

    /// <summary>Creates the pgvector extension and the `chunks` table (idempotent).</summary>
    public static async Task EnsureSchemaAsync(string connString, int dimensions, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS chunks (
                id          BIGSERIAL PRIMARY KEY,
                source      TEXT NOT NULL,
                title       TEXT NOT NULL,
                chunk_index INT  NOT NULL,
                content     TEXT NOT NULL,
                embedding   vector({dimensions}) NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS chunks_source_idx ON chunks (source);

            -- HNSW index for cosine-similarity search (fast at scale).
            CREATE INDEX IF NOT EXISTS chunks_embedding_idx
                ON chunks USING hnsw (embedding vector_cosine_ops);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Removes all chunks for a source (used when re-indexing).</summary>
    public async Task DeleteBySourceAsync(string source, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand("DELETE FROM chunks WHERE source = $1");
        cmd.Parameters.AddWithValue(source);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Inserts the chunks of a source within a transaction.</summary>
    public async Task InsertAsync(
        string source,
        string title,
        IReadOnlyList<(int Index, string Content, float[] Embedding)> rows,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var row in rows)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO chunks (source, title, chunk_index, content, embedding)
                VALUES ($1, $2, $3, $4, $5)
                """, conn, tx);

            cmd.Parameters.AddWithValue(source);
            cmd.Parameters.AddWithValue(title);
            cmd.Parameters.AddWithValue(row.Index);
            cmd.Parameters.AddWithValue(row.Content);
            cmd.Parameters.AddWithValue(new Vector(row.Embedding));

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Returns the <paramref name="topK"/> chunks most similar to the query vector,
    /// using cosine distance (pgvector's &lt;=&gt; operator). The returned score is
    /// 1 - distance: the closer to 1, the more similar.
    /// </summary>
    public async Task<List<ChunkHit>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand(
            """
            SELECT id, source, title, chunk_index, content,
                   1 - (embedding <=> $1) AS score
            FROM chunks
            ORDER BY embedding <=> $1
            LIMIT $2
            """);

        cmd.Parameters.AddWithValue(new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue(topK);

        var hits = new List<ChunkHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new ChunkHit(
                Id: reader.GetInt64(0),
                Source: reader.GetString(1),
                Title: reader.GetString(2),
                ChunkIndex: reader.GetInt32(3),
                Content: reader.GetString(4),
                Score: reader.GetDouble(5)));
        }

        return hits;
    }

    /// <summary>Total number of chunks.</summary>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand("SELECT COUNT(*) FROM chunks");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
