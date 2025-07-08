using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pathfinder.Shared.Embeddings;

public class OllamaEmbeddingsClient
{
    private readonly HttpClient _httpClient;
    private readonly string _defaultModel = "nomic-embed-text";
    private readonly int _maxTokenContextLength = 8192;

    // Default estimate of characters per token (approximate)
    private readonly int _estimatedCharsPerToken = 4;

    public OllamaEmbeddingsClient(string baseUrl = "http://localhost:11434")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    public async Task<float[]> GetEmbeddingsAsync(string inputText, string model = null)
    {
        var requestPayload = new
        {
            model = model ?? _defaultModel,
            prompt = inputText
        };

        var jsonRequest = JsonSerializer.Serialize(requestPayload);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync("/api/embeddings", content);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<EmbeddingResponse>(jsonResponse);

        return responseObject.Embedding;
    }

    /// <summary>
    /// Gets embeddings for large content by chunking it into smaller pieces.
    /// </summary>
    /// <param name="inputText">The large text to be embedded</param>
    /// <param name="chunkStrategy">Strategy for chunking the text</param>
    /// <param name="combineStrategy">Strategy for combining chunk embeddings</param>
    /// <param name="overlap">Number of characters to overlap between chunks</param>
    /// <param name="maxChunkTokens">Maximum tokens per chunk (default: 8000, allowing some buffer)</param>
    /// <param name="model">Model to use for embeddings (default: nomic-embed-text)</param>
    /// <returns>Either a single combined embedding or multiple embeddings depending on combineStrategy</returns>
    public async Task<EmbeddingResult> GetEmbeddingsForLargeContentAsync(
        string inputText,
        ChunkStrategy chunkStrategy = ChunkStrategy.Paragraph,
        EmbeddingCombineStrategy combineStrategy = EmbeddingCombineStrategy.Average,
        int overlap = 100,
        int maxChunkTokens = 8000,
        string model = null)
    {
        model ??= _defaultModel;

        // If the content is small enough, just use the regular method
        if (EstimateTokenCount(inputText) < _maxTokenContextLength)
        {
            var embedding = await GetEmbeddingsAsync(inputText, model);
            return new EmbeddingResult
            {
                CombinedEmbedding = embedding,
                ChunkEmbeddings = new List<ChunkEmbedding>
                {
                    new ChunkEmbedding
                    {
                        Embedding = embedding,
                        Text = inputText,
                        StartPosition = 0,
                        EndPosition = inputText.Length
                    }
                }
            };
        }

        // Split the text into chunks based on the selected strategy
        var chunks = SplitTextIntoChunks(inputText, chunkStrategy, overlap, maxChunkTokens);

        // Get embeddings for each chunk
        var chunkEmbeddings = new List<ChunkEmbedding>();
        foreach (var chunk in chunks)
        {
            var embedding = await GetEmbeddingsAsync(chunk.Text, model);
            chunkEmbeddings.Add(new ChunkEmbedding
            {
                Embedding = embedding,
                Text = chunk.Text,
                StartPosition = chunk.StartPosition,
                EndPosition = chunk.EndPosition
            });
        }

        // Combine the embeddings according to the strategy
        float[] combinedEmbedding = null;

        if (combineStrategy != EmbeddingCombineStrategy.None)
        {
            combinedEmbedding = CombineEmbeddings(chunkEmbeddings, combineStrategy);
        }

        return new EmbeddingResult
        {
            CombinedEmbedding = combinedEmbedding,
            ChunkEmbeddings = chunkEmbeddings
        };
    }

    /// <summary>
    /// Estimates token count based on character count
    /// Note: This is an approximation; different tokenizers work differently
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        return (int)Math.Ceiling((double)text.Length / _estimatedCharsPerToken);
    }

    /// <summary>
    /// Splits text into chunks based on the selected strategy
    /// </summary>
    private List<TextChunk> SplitTextIntoChunks(
        string text,
        ChunkStrategy strategy,
        int overlap,
        int maxChunkTokens)
    {
        // Calculate the maximum characters per chunk
        int maxCharsPerChunk = maxChunkTokens * _estimatedCharsPerToken;

        switch (strategy)
        {
            case ChunkStrategy.Character:
                return SplitByCharacter(text, maxCharsPerChunk, overlap);

            case ChunkStrategy.Sentence:
                return SplitBySentence(text, maxCharsPerChunk, overlap);

            case ChunkStrategy.Paragraph:
                return SplitByParagraph(text, maxCharsPerChunk, overlap);

            default:
                return SplitByCharacter(text, maxCharsPerChunk, overlap);
        }
    }

    /// <summary>
    /// Splits text by character count
    /// </summary>
    private List<TextChunk> SplitByCharacter(string text, int maxCharsPerChunk, int overlap)
    {
        var chunks = new List<TextChunk>();

        for (int i = 0; i < text.Length; i += maxCharsPerChunk - overlap)
        {
            int length = Math.Min(maxCharsPerChunk, text.Length - i);
            chunks.Add(new TextChunk
            {
                Text = text.Substring(i, length),
                StartPosition = i,
                EndPosition = i + length
            });

            if (i + length >= text.Length) break;
        }

        return chunks;
    }

    /// <summary>
    /// Splits text by sentences, trying to keep chunks under the token limit
    /// </summary>
    private List<TextChunk> SplitBySentence(string text, int maxCharsPerChunk, int overlap)
    {
        var chunks = new List<TextChunk>();

        // Simple regex for sentence boundaries
        var sentenceRegex = new Regex(@"(?<=[.!?])\s+");
        var sentences = sentenceRegex.Split(text).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        int currentPos = 0;
        var currentChunk = new StringBuilder();
        int startPos = 0;

        foreach (var sentence in sentences)
        {
            // If adding this sentence would exceed the limit, create a chunk
            if (currentChunk.Length + sentence.Length > maxCharsPerChunk && currentChunk.Length > 0)
            {
                string chunkText = currentChunk.ToString();
                chunks.Add(new TextChunk
                {
                    Text = chunkText,
                    StartPosition = startPos,
                    EndPosition = startPos + chunkText.Length
                });

                // Start a new chunk with overlap
                if (overlap > 0)
                {
                    // For sentence boundaries, get complete sentences for overlap
                    currentChunk = new StringBuilder();

                    // Track how many characters we've accumulated for overlap
                    int overlapChars = 0;

                    // Go backwards through sentences in current chunk until we have enough for overlap
                    var sentencesInLastChunk = chunks.Last().Text.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.None);
                    for (int i = sentencesInLastChunk.Length - 1; i >= 0 && overlapChars < overlap; i--)
                    {
                        if (i >= 0 && i < sentencesInLastChunk.Length)
                        {
                            var sentenceToInclude = sentencesInLastChunk[i];
                            overlapChars += sentenceToInclude.Length;
                            currentChunk.Insert(0, sentenceToInclude + ". ");
                        }
                    }

                    // Update the start position based on overlap
                    startPos = chunks.Last().EndPosition - currentChunk.Length;
                }
                else
                {
                    // No overlap
                    currentChunk = new StringBuilder();
                    startPos = currentPos;
                }
            }

            currentChunk.Append(sentence);
            if (!sentence.EndsWith(".") && !sentence.EndsWith("!") && !sentence.EndsWith("?"))
            {
                currentChunk.Append(". ");
            }
            else if (!sentence.EndsWith(" "))
            {
                currentChunk.Append(" ");
            }

            currentPos += sentence.Length;
        }

        // Add the last chunk if there's anything left
        if (currentChunk.Length > 0)
        {
            string chunkText = currentChunk.ToString();
            chunks.Add(new TextChunk
            {
                Text = chunkText,
                StartPosition = startPos,
                EndPosition = startPos + chunkText.Length
            });
        }

        return chunks;
    }

    /// <summary>
    /// Splits text by paragraphs, trying to keep chunks under the token limit
    /// </summary>
    private List<TextChunk> SplitByParagraph(string text, int maxCharsPerChunk, int overlap)
    {
        var chunks = new List<TextChunk>();

        // Split by paragraph (double newline)
        var paragraphRegex = new Regex(@"(\r\n|\n){2,}");
        var paragraphs = paragraphRegex.Split(text)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        int currentPos = 0;
        var currentChunk = new StringBuilder();
        int startPos = 0;
        List<string> paragraphsInCurrentChunk = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            // If adding this paragraph would exceed the limit, create a chunk
            if (currentChunk.Length + paragraph.Length > maxCharsPerChunk && currentChunk.Length > 0)
            {
                string chunkText = currentChunk.ToString();
                chunks.Add(new TextChunk
                {
                    Text = chunkText,
                    StartPosition = startPos,
                    EndPosition = startPos + chunkText.Length
                });

                // Start a new chunk with overlap
                currentChunk = new StringBuilder();

                if (overlap > 0)
                {
                    // For paragraph boundaries, include whole paragraphs as overlap
                    // Go backwards through paragraphs until we have enough for overlap
                    int overlapChars = 0;
                    var paragraphsToInclude = new List<string>();

                    for (int i = paragraphsInCurrentChunk.Count - 1;
                         i >= 0 && overlapChars < overlap;
                         i--)
                    {
                        var p = paragraphsInCurrentChunk[i];
                        overlapChars += p.Length;
                        paragraphsToInclude.Insert(0, p);
                    }

                    foreach (var p in paragraphsToInclude)
                    {
                        currentChunk.Append(p).Append("\n\n");
                    }

                    // Update the start position based on overlap
                    startPos = chunks.Last().EndPosition - currentChunk.Length;
                }
                else
                {
                    // No overlap
                    startPos = currentPos;
                }

                // Reset the list of paragraphs in the current chunk
                paragraphsInCurrentChunk = new List<string>();
            }

            currentChunk.Append(paragraph).Append("\n\n");
            paragraphsInCurrentChunk.Add(paragraph);
            currentPos += paragraph.Length + 2; // +2 for the newlines
        }

        // Add the last chunk if there's anything left
        if (currentChunk.Length > 0)
        {
            string chunkText = currentChunk.ToString();
            chunks.Add(new TextChunk
            {
                Text = chunkText,
                StartPosition = startPos,
                EndPosition = startPos + chunkText.Length
            });
        }

        return chunks;
    }

    /// <summary>
    /// Combines multiple embeddings according to the selected strategy
    /// </summary>
    private float[] CombineEmbeddings(List<ChunkEmbedding> chunkEmbeddings, EmbeddingCombineStrategy strategy)
    {
        if (chunkEmbeddings == null || chunkEmbeddings.Count == 0)
        {
            return null;
        }

        if (chunkEmbeddings.Count == 1)
        {
            return chunkEmbeddings[0].Embedding;
        }

        int dimensions = chunkEmbeddings[0].Embedding.Length;
        var result = new float[dimensions];

        switch (strategy)
        {
            case EmbeddingCombineStrategy.Average:
                // Calculate average of all embeddings
                for (int i = 0; i < dimensions; i++)
                {
                    float sum = 0;
                    foreach (var chunkEmbedding in chunkEmbeddings)
                    {
                        sum += chunkEmbedding.Embedding[i];
                    }
                    result[i] = sum / chunkEmbeddings.Count;
                }
                break;

            case EmbeddingCombineStrategy.WeightedAverage:
                // Calculate weighted average based on chunk lengths
                float totalWeight = 0;
                var weights = new float[chunkEmbeddings.Count];

                for (int i = 0; i < chunkEmbeddings.Count; i++)
                {
                    // Use chunk length as the weight
                    weights[i] = chunkEmbeddings[i].Text.Length;
                    totalWeight += weights[i];
                }

                for (int i = 0; i < dimensions; i++)
                {
                    float weightedSum = 0;
                    for (int j = 0; j < chunkEmbeddings.Count; j++)
                    {
                        weightedSum += chunkEmbeddings[j].Embedding[i] * (weights[j] / totalWeight);
                    }
                    result[i] = weightedSum;
                }
                break;

            case EmbeddingCombineStrategy.Max:
                // Take the maximum value for each dimension
                for (int i = 0; i < dimensions; i++)
                {
                    float maxVal = float.MinValue;
                    foreach (var chunkEmbedding in chunkEmbeddings)
                    {
                        maxVal = Math.Max(maxVal, chunkEmbedding.Embedding[i]);
                    }
                    result[i] = maxVal;
                }
                break;

            default:
                throw new ArgumentException("Unsupported embedding combine strategy");
        }

        // Normalize the vector (important for cosine similarity)
        float magnitude = 0;
        for (int i = 0; i < dimensions; i++)
        {
            magnitude += result[i] * result[i];
        }

        magnitude = (float)Math.Sqrt(magnitude);

        for (int i = 0; i < dimensions; i++)
        {
            result[i] /= magnitude;
        }

        return result;
    }
}

public enum ChunkStrategy
{
    Character,   // Split by character count (simplest)
    Sentence,    // Split by sentence boundaries
    Paragraph    // Split by paragraph boundaries (best for semantic coherence)
}

public enum EmbeddingCombineStrategy
{
    None,           // Return individual chunk embeddings only
    Average,        // Simple average of all embeddings
    WeightedAverage, // Weighted by chunk length
    Max             // Take the maximum value for each dimension
}

public class TextChunk
{
    public string Text { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}

public class ChunkEmbedding
{
    public float[] Embedding { get; set; }
    public string Text { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}

public class EmbeddingResult
{
    public float[] CombinedEmbedding { get; set; }
    public List<ChunkEmbedding> ChunkEmbeddings { get; set; }
}

public class EmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}