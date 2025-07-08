namespace rag_quickdemo;

public class AppSettings
{
    public AzureOpenAI AzureOpenAI { get; set; } = new();
    public string Database { get; set; } = "rag.db";
    public int TopK { get; set; } = 4;
}

public class AzureOpenAI
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-ada-002";
    public string ChatModel { get; set; } = "gpt-4o-mini";
}
