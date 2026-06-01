namespace DocsRag.Api.Rag;

/// <summary>
/// Splits a text into overlapping chunks.
///
/// Why chunking: embeddings work better (and search is more precise) when each
/// vector represents a small, coherent passage rather than a whole document. The
/// overlap avoids "cutting" an idea right at the boundary between two chunks.
///
/// Here the size is measured in WORDS (a simple approximation of tokens).
/// </summary>
public static class TextChunker
{
    public static List<string> Chunk(string text, int sizeWords, int overlapWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (overlapWords >= sizeWords)
            overlapWords = sizeWords / 5; // safety: overlap must stay below the size

        // Split on whitespace, discarding empties.
        var words = text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length <= sizeWords)
            return [string.Join(' ', words)];

        var chunks = new List<string>();
        var step = sizeWords - overlapWords;

        for (var start = 0; start < words.Length; start += step)
        {
            var window = words.Skip(start).Take(sizeWords).ToArray();
            if (window.Length == 0)
                break;

            chunks.Add(string.Join(' ', window));

            // The last chunk already reached the end of the text.
            if (start + sizeWords >= words.Length)
                break;
        }

        return chunks;
    }
}
