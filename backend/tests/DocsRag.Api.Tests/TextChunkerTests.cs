using DocsRag.Api.Rag;
using Xunit;

namespace DocsRag.Api.Tests;

// Unit tests for the chunking logic — pure, fast, no I/O. Chunking quality directly
// affects retrieval quality, so it is worth pinning down its behaviour.
public class TextChunkerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Chunk_EmptyOrBlank_ReturnsEmpty(string? text)
    {
        var chunks = TextChunker.Chunk(text!, sizeWords: 100, overlapWords: 20);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_ShorterThanSize_ReturnsSingleChunk()
    {
        var text = "one two three four five";
        var chunks = TextChunker.Chunk(text, sizeWords: 100, overlapWords: 20);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void Chunk_NormalizesWhitespace()
    {
        var chunks = TextChunker.Chunk("  one   two\t three \n four ", sizeWords: 100, overlapWords: 0);

        Assert.Single(chunks);
        Assert.Equal("one two three four", chunks[0]);
    }

    [Fact]
    public void Chunk_LongerThanSize_SplitsIntoMultiple()
    {
        var text = string.Join(' ', Enumerable.Range(1, 250).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(text, sizeWords: 100, overlapWords: 20);

        // step = 100 - 20 = 80. Windows start at 0, 80, 160; the window at 160 covers
        // words 160..249 (reaches the end), so the loop stops there => 3 chunks.
        Assert.Equal(3, chunks.Count);
        Assert.Equal("w1", chunks[0].Split(' ')[0]);
        Assert.Equal("w250", chunks[^1].Split(' ')[^1]); // last chunk reaches the end
        Assert.All(chunks, c => Assert.True(c.Split(' ').Length <= 100));
    }

    [Fact]
    public void Chunk_AppliesOverlapBetweenConsecutiveChunks()
    {
        var text = string.Join(' ', Enumerable.Range(1, 150).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(text, sizeWords: 100, overlapWords: 20);

        Assert.True(chunks.Count >= 2);

        // The first chunk ends at w100; with overlap 20 the second starts at w81.
        var firstWords = chunks[0].Split(' ');
        var secondWords = chunks[1].Split(' ');
        Assert.Equal("w100", firstWords[^1]);
        Assert.Equal("w81", secondWords[0]);

        // The last 20 words of chunk 0 equal the first 20 words of chunk 1 (overlap).
        Assert.Equal(firstWords[^20..], secondWords[..20]);
    }

    [Fact]
    public void Chunk_OverlapNotLessThanSize_DoesNotLoopForever()
    {
        // overlap >= size would make step <= 0; the chunker must guard against it.
        var text = string.Join(' ', Enumerable.Range(1, 300).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(text, sizeWords: 50, overlapWords: 50);

        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count < 300); // progress was made; no infinite loop
    }
}
