using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pathfinder.Shared.Data
{
    using Microsoft.Data.Sqlite;
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class SqliteVectorDb : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _tableName;
        private readonly int _dimensions;

        public SqliteVectorDb(string dbPath, string tableName, int dimensions, string vecExtensionPath)
        {
            _tableName = tableName;
            _dimensions = dimensions;

            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();

            // Load the vec0 extension
            _connection.LoadExtension(vecExtensionPath);

            // Create the virtual table for vectors
            CreateVectorTable();
        }

        private void CreateVectorTable()
        {
            using (var command = _connection.CreateCommand())
            {
                // Create a virtual table using the vec0 module
                command.CommandText = $@"
                CREATE VIRTUAL TABLE IF NOT EXISTS vec_{_tableName} USING vec0(
                    embedding FLOAT[{_dimensions}] distance_metric=cosine
                )";
                command.ExecuteNonQuery();

                // Create a regular table for metadata
                command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_tableName} (
                    id TEXT PRIMARY KEY,
                    content TEXT,
                    metadata TEXT
                )";
                command.ExecuteNonQuery();
            }
        }

        public async Task InsertAsync(string id, string content, string metadata, float[] embedding)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    // Insert metadata
                    using (var metadataCmd = _connection.CreateCommand())
                    {
                        metadataCmd.CommandText = $@"
                        INSERT INTO {_tableName} (id, content, metadata)
                        VALUES (@id, @content, @metadata)";
                        metadataCmd.Parameters.AddWithValue("@id", id);
                        metadataCmd.Parameters.AddWithValue("@content", content);
                        metadataCmd.Parameters.AddWithValue("@metadata", metadata);
                        await metadataCmd.ExecuteNonQueryAsync();
                    }

                    // Insert vector
                    using (var vectorCmd = _connection.CreateCommand())
                    {
                        string vectorJson = JsonSerializer.Serialize(embedding);
                        vectorCmd.CommandText = $@"
                        INSERT INTO vec_{_tableName} (rowid, embedding)
                        VALUES (@id, @embedding)";
                        vectorCmd.Parameters.AddWithValue("@id", id);
                        vectorCmd.Parameters.AddWithValue("@embedding", vectorJson);
                        await vectorCmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public async Task UpsertAsync(string id, string content, string metadata, float[] embedding)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    // Upsert metadata
                    using (var metadataCmd = _connection.CreateCommand())
                    {
                        metadataCmd.CommandText = $@"
                        INSERT INTO {_tableName} (id, content, metadata)
                        VALUES (@id, @content, @metadata)
                        ON CONFLICT(id) DO UPDATE SET
                            content = @content,
                            metadata = @metadata";
                        metadataCmd.Parameters.AddWithValue("@id", id);
                        metadataCmd.Parameters.AddWithValue("@content", content);
                        metadataCmd.Parameters.AddWithValue("@metadata", metadata);
                        await metadataCmd.ExecuteNonQueryAsync();
                    }

                    // Delete existing vector if it exists
                    using (var deleteCmd = _connection.CreateCommand())
                    {
                        deleteCmd.CommandText = $"DELETE FROM vec_{_tableName} WHERE rowid = @id";
                        deleteCmd.Parameters.AddWithValue("@id", id);
                        await deleteCmd.ExecuteNonQueryAsync();
                    }

                    // Insert vector
                    using (var vectorCmd = _connection.CreateCommand())
                    {
                        string vectorJson = JsonSerializer.Serialize(embedding);
                        vectorCmd.CommandText = $@"
                        INSERT INTO vec_{_tableName} (rowid, embedding)
                        VALUES (@id, @embedding)";
                        vectorCmd.Parameters.AddWithValue("@id", id);
                        vectorCmd.Parameters.AddWithValue("@embedding", vectorJson);
                        await vectorCmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public async Task<List<(string Id, string Content, string Metadata, double Similarity)>> FindSimilarAsync(
            float[] queryEmbedding,
            int limit = 10)
        {
            var results = new List<(string, string, string, double)>();
            string vectorJson = JsonSerializer.Serialize(queryEmbedding);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = $@"
                SELECT m.id, m.content, m.metadata, v.distance
                FROM vec_{_tableName} as v
                JOIN {_tableName} as m ON v.rowid = m.id
                WHERE v.embedding MATCH @queryVector
                ORDER BY v.distance
                LIMIT @limit";
                command.Parameters.AddWithValue("@queryVector", vectorJson);
                command.Parameters.AddWithValue("@limit", limit);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string id = reader.GetString(0);
                        string content = reader.GetString(1);
                        string metadata = reader.GetString(2);
                        double distance = reader.GetDouble(3);
                        double similarity = 1 - distance; // Convert distance to similarity

                        results.Add((id, content, metadata, similarity));
                    }
                }
            }

            return results;
        }

        public async Task DeleteAsync(string id)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    // Delete metadata
                    using (var metadataCmd = _connection.CreateCommand())
                    {
                        metadataCmd.CommandText = $"DELETE FROM {_tableName} WHERE id = @id";
                        metadataCmd.Parameters.AddWithValue("@id", id);
                        await metadataCmd.ExecuteNonQueryAsync();
                    }

                    // Delete vector
                    using (var vectorCmd = _connection.CreateCommand())
                    {
                        vectorCmd.CommandText = $"DELETE FROM vec_{_tableName} WHERE rowid = @id";
                        vectorCmd.Parameters.AddWithValue("@id", id);
                        await vectorCmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
