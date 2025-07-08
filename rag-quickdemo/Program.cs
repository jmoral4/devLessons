using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Pathfinder.Shared.Data;
using Pathfinder.Shared.Embeddings;
using System.Text.Json;

namespace ragdemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Bind configuration to AppSettings class
        var appSettings = new AppSettings();
        configuration.Bind(appSettings);

        // Get embeddings for storing a document
        OllamaEmbeddingsClient client = new OllamaEmbeddingsClient("http://localhost:11435");
        var documentText = "The sky is blue because of Rayleigh scattering";
        var embeddings = await client.GetEmbeddingsAsync(documentText);
        Console.WriteLine($"Embeddings for document: {string.Join(", ", embeddings)}");

        // Create the datastore
        MultiModalDataStore ds = new MultiModalDataStore(appSettings.Database, 768, @"vec0.dll");
        Console.WriteLine($"Created/Using database: {appSettings.Database}");

        // Insert document with vector in a single operation
        var (docId, vectorId) = ds.UpsertDocumentWithVector(
            title: "Sky Color",
            content: documentText,
            vector: embeddings,
            metadata: JsonSerializer.Serialize(new { source = "science_facts" })
        );
        Console.WriteLine($"Inserted document with ID: {docId} and vector ID: {vectorId}");
    }
}
