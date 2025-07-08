using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pathfinder.Shared.Data;

public sealed class MultiModalDataStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int _dimensions;

    // Table names
    private const string VectorTable = "vec_data";
    private const string DocumentTable = "documents";
    private const string NodeTable = "graph_nodes";
    private const string EdgeTable = "graph_edges";
    private const string AssociationsTable = "entity_associations"; // New table for linking entities

    /// <summary>
    /// Initializes a new instance of the multi-modal data store
    /// </summary>
    /// <param name="dbPath">Path to the SQLite database file</param>
    /// <param name="dimensions">Number of dimensions for vector embeddings</param>
    /// <param name="vecExtensionPath">Path to the sqlite-vec extension library</param>
    public MultiModalDataStore(string dbPath, int dimensions, string vecExtensionPath)
    {
        _dimensions = dimensions;

        // Use a connection string with better options
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
            //Journal = SqliteJournalMode.Wal // Add WAL mode for better concurrency
        };

        _connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
        _connection.Open();

        try
        {
            // Load the vec0 extension
            _connection.LoadExtension(vecExtensionPath);

            // Create all required tables
            CreateTables();
        }
        catch (Exception ex)
        {
            _connection.Dispose();
            throw new InvalidOperationException($"Failed to initialize MultiModalDataStore: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates all required tables for the multi-modal data store
    /// </summary>
    private void CreateTables()
    {
        using var transaction = _connection.BeginTransaction();

        try
        {
            // Create vector table using the correct vec0 syntax
            using (var cmd = _connection.CreateCommand())
            {
                // Use the float[dimensions] syntax as shown in the example
                cmd.CommandText = $"CREATE VIRTUAL TABLE IF NOT EXISTS {VectorTable} USING vec0(sample_embedding float[{_dimensions}])";
                cmd.ExecuteNonQuery();
            }

            // Create FTS5 table for document storage
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $@"
                    CREATE VIRTUAL TABLE IF NOT EXISTS {DocumentTable} USING fts5(
                        title, 
                        content, 
                        metadata,
                        tokenize='unicode61 remove_diacritics 1'
                    )";
                cmd.ExecuteNonQuery();
            }

            // Create graph nodes table with indexes
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {NodeTable} (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        label TEXT NOT NULL,
                        type TEXT,
                        properties TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    );
                    CREATE INDEX IF NOT EXISTS idx_{NodeTable}_label ON {NodeTable}(label);
                    CREATE INDEX IF NOT EXISTS idx_{NodeTable}_type ON {NodeTable}(type);";
                cmd.ExecuteNonQuery();
            }

            // Create graph edges table with indexes
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {EdgeTable} (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        source INTEGER NOT NULL,
                        target INTEGER NOT NULL,
                        rel TEXT NOT NULL,
                        weight REAL DEFAULT 1.0,
                        properties TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY(source) REFERENCES {NodeTable}(id) ON DELETE CASCADE,
                        FOREIGN KEY(target) REFERENCES {NodeTable}(id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS idx_{EdgeTable}_source ON {EdgeTable}(source);
                    CREATE INDEX IF NOT EXISTS idx_{EdgeTable}_target ON {EdgeTable}(target);
                    CREATE INDEX IF NOT EXISTS idx_{EdgeTable}_rel ON {EdgeTable}(rel);
                    CREATE INDEX IF NOT EXISTS idx_{EdgeTable}_source_target ON {EdgeTable}(source, target);";
                cmd.ExecuteNonQuery();
            }

            // NEW: Create associations table that links vectors to entities (documents, nodes)
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {AssociationsTable} (
                        vector_id INTEGER NOT NULL,
                        entity_type TEXT NOT NULL,  -- 'document', 'node'
                        entity_id INTEGER NOT NULL,
                        relevance REAL DEFAULT 1.0,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (vector_id, entity_type, entity_id)
                    );
                    CREATE INDEX IF NOT EXISTS idx_{AssociationsTable}_vector_id ON {AssociationsTable}(vector_id);
                    CREATE INDEX IF NOT EXISTS idx_{AssociationsTable}_entity ON {AssociationsTable}(entity_type, entity_id);";
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            throw new InvalidOperationException($"Failed to create tables: {ex.Message}", ex);
        }
    }

    // ========================== VECTOR API ==========================

    /// <summary>
    /// Inserts a vector embedding with auto-generated ID
    /// </summary>
    /// <param name="vector">The vector to insert</param>
    /// <returns>The ID of the inserted vector</returns>
    public long InsertVector(float[] vector)
    {
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector must have {_dimensions} dimensions.");

        // Serialize the vector as JSON
        var vecJson = JsonSerializer.Serialize(vector);

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Insert the vector
            using (var cmdInsert = _connection.CreateCommand())
            {
                cmdInsert.CommandText = $"INSERT INTO {VectorTable}(sample_embedding) VALUES (@vec)";
                cmdInsert.Parameters.AddWithValue("@vec", vecJson);
                cmdInsert.ExecuteNonQuery();
            }

            // Get the last inserted rowid
            using (var cmdLastId = _connection.CreateCommand())
            {
                cmdLastId.CommandText = "SELECT last_insert_rowid()";
                var result = cmdLastId.ExecuteScalar();
                transaction.Commit();
                return Convert.ToInt64(result);
            }
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Inserts a vector embedding with a specified ID
    /// </summary>
    /// <param name="rowId">The ID to use</param>
    /// <param name="vector">The vector to insert</param>
    public void InsertVectorWithId(long rowId, float[] vector)
    {
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector must have {_dimensions} dimensions.");

        // Serialize the vector as JSON array
        var vecJson = JsonSerializer.Serialize(vector);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {VectorTable}(rowid, sample_embedding) VALUES (@id, @vec)";
        cmd.Parameters.AddWithValue("@id", rowId);
        cmd.Parameters.AddWithValue("@vec", vecJson);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Performs a K-Nearest Neighbors search against the vector table
    /// </summary>
    /// <param name="queryVector">The query vector</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of tuples containing row ID and distance</returns>
    public List<(long RowId, double Distance)> KnnQuery(float[] queryVector, int limit = 10)
    {
        if (queryVector.Length != _dimensions)
            throw new ArgumentException($"Query vector must have {_dimensions} dimensions.");

        // Serialize the query vector as JSON array
        var queryJson = JsonSerializer.Serialize(queryVector);
        var results = new List<(long, double)>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT rowid, distance FROM {VectorTable} WHERE sample_embedding MATCH @query ORDER BY distance LIMIT @limit";
        cmd.Parameters.AddWithValue("@query", queryJson);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetInt64(0), reader.GetDouble(1)));
        }

        return results;
    }

    // ========================== DOCUMENT API ==========================

    /// <summary>
    /// Inserts a document with title and content
    /// </summary>
    /// <param name="title">Document title</param>
    /// <param name="content">Document content</param>
    /// <param name="metadata">Optional JSON metadata</param>
    /// <returns>The ID of the inserted document</returns>
    public long InsertDocument(string title, string content, string metadata = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {DocumentTable}(title, content, metadata) VALUES (@title, @content, @metadata) RETURNING rowid";
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@metadata", metadata ?? "{}");

        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Inserts a document with its vector embedding in a single operation
    /// </summary>
    /// <param name="title">Document title</param>
    /// <param name="content">Document content</param>
    /// <param name="vector">Vector embedding of the document</param>
    /// <param name="metadata">Optional JSON metadata</param>
    /// <returns>A tuple containing the document ID and vector ID</returns>
    public (long DocumentId, long VectorId) InsertDocumentWithVector(string title, string content, float[] vector, string metadata = null)
    {
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector must have {_dimensions} dimensions.");

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Insert document - FIX: Don't use RETURNING for FTS tables
            long documentId;
            using (var cmdDoc = _connection.CreateCommand())
            {
                cmdDoc.CommandText = $"INSERT INTO {DocumentTable}(title, content, metadata) VALUES (@title, @content, @metadata)";
                cmdDoc.Parameters.AddWithValue("@title", title);
                cmdDoc.Parameters.AddWithValue("@content", content);
                cmdDoc.Parameters.AddWithValue("@metadata", metadata ?? "{}");
                cmdDoc.ExecuteNonQuery();

                // Get the rowid in a separate query
                cmdDoc.CommandText = "SELECT last_insert_rowid()";
                documentId = Convert.ToInt64(cmdDoc.ExecuteScalar());
            }

            // Insert vector
            var vecJson = JsonSerializer.Serialize(vector);
            long vectorId;
            using (var cmdVec = _connection.CreateCommand())
            {
                cmdVec.CommandText = $"INSERT INTO {VectorTable}(sample_embedding) VALUES (@vec)";
                cmdVec.Parameters.AddWithValue("@vec", vecJson);
                cmdVec.ExecuteNonQuery();

                cmdVec.CommandText = "SELECT last_insert_rowid()";
                vectorId = Convert.ToInt64(cmdVec.ExecuteScalar());
            }

            // Create association
            using (var cmdAssoc = _connection.CreateCommand())
            {
                cmdAssoc.CommandText = $"INSERT INTO {AssociationsTable}(vector_id, entity_type, entity_id) VALUES (@vecId, 'document', @docId)";
                cmdAssoc.Parameters.AddWithValue("@vecId", vectorId);
                cmdAssoc.Parameters.AddWithValue("@docId", documentId);
                cmdAssoc.ExecuteNonQuery();
            }

            transaction.Commit();
            Console.WriteLine($"Created document with ID {documentId} and vector ID {vectorId}");
            return (documentId, vectorId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Inserts or updates a document with its vector embedding
    /// </summary>
    public (long DocumentId, long VectorId) UpsertDocumentWithVector(
        string title,
        string content,
        float[] vector,
        string metadata = null,
        double similarityThreshold = 0.95)
    {
        // First, check if a very similar vector already exists
        // First, check if a very similar vector already exists
        var existingVectors = KnnQuery(vector, limit: 5);
        var similarVectorId = -1L;

        foreach (var vecMatch in existingVectors)
        {
            // In vec0, distance of 0 is perfect match, smaller values = more similar
            // Using a small threshold like 0.05 to find nearly identical vectors
            if (vecMatch.Distance <= 0.05)  // Threshold for "nearly identical"
            {
                similarVectorId = vecMatch.RowId;
                Console.WriteLine("Found existing near perfect vector match! (dupe or super close to it)");
                break;
            }
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            long documentId;
            long vectorId;

            // Check if we already have a similar vector associated with a document
            if (similarVectorId > 0)
            {
                Console.WriteLine("Performing upsert fixup with doc + embeddings in Transaction.");
                var associations = GetVectorAssociations(similarVectorId);
                var docAssociation = associations.FirstOrDefault(a => a.EntityType == "document");

                if (docAssociation.EntityType == "document")
                {
                    // Update existing document
                    documentId = docAssociation.EntityId;
                    // Update existing document
                    documentId = docAssociation.EntityId;
                    using (var cmdUpdateDoc = _connection.CreateCommand())
                    {
                        // For FTS5 tables, DELETE followed by INSERT is the proper way to update
                        cmdUpdateDoc.CommandText = $"DELETE FROM {DocumentTable} WHERE rowid = @id";
                        cmdUpdateDoc.Parameters.AddWithValue("@id", documentId);
                        cmdUpdateDoc.ExecuteNonQuery();

                        // When inserting into FTS5 tables, don't specify rowid directly
                        cmdUpdateDoc.Parameters.Clear();
                        cmdUpdateDoc.CommandText = $"INSERT INTO {DocumentTable}(title, content, metadata) VALUES (@title, @content, @metadata)";
                        cmdUpdateDoc.Parameters.AddWithValue("@title", title);
                        cmdUpdateDoc.Parameters.AddWithValue("@content", content);
                        cmdUpdateDoc.Parameters.AddWithValue("@metadata", metadata ?? "{}");
                        cmdUpdateDoc.ExecuteNonQuery();

                        // Verify we got the same rowid back or get the new one
                        cmdUpdateDoc.CommandText = "SELECT last_insert_rowid()";
                        var newDocId = Convert.ToInt64(cmdUpdateDoc.ExecuteScalar());

                        // If we got a different rowid, we need to update associations
                        if (newDocId != documentId)
                        {
                            // Update the association to point to the new document id
                            cmdUpdateDoc.CommandText = $"UPDATE {AssociationsTable} SET entity_id = @newId WHERE entity_type = 'document' AND entity_id = @oldId";
                            cmdUpdateDoc.Parameters.Clear();
                            cmdUpdateDoc.Parameters.AddWithValue("@newId", newDocId);
                            cmdUpdateDoc.Parameters.AddWithValue("@oldId", documentId);
                            cmdUpdateDoc.ExecuteNonQuery();

                            documentId = newDocId;
                        }
                    }

                    // Use the existing vector
                    vectorId = similarVectorId;
                }
                else
                {
                    Console.WriteLine("Inserted new vector/document semantic record!");
                    // Insert new document but reuse the vector
                    using (var cmdDoc = _connection.CreateCommand())
                    {
                        cmdDoc.CommandText = $"INSERT INTO {DocumentTable}(title, content, metadata) VALUES (@title, @content, @metadata)";
                        cmdDoc.Parameters.AddWithValue("@title", title);
                        cmdDoc.Parameters.AddWithValue("@content", content);
                        cmdDoc.Parameters.AddWithValue("@metadata", metadata ?? "{}");
                        cmdDoc.ExecuteNonQuery();

                        cmdDoc.CommandText = "SELECT last_insert_rowid()";
                        documentId = Convert.ToInt64(cmdDoc.ExecuteScalar());
                    }

                    vectorId = similarVectorId;

                    // Create association
                    using (var cmdAssoc = _connection.CreateCommand())
                    {
                        cmdAssoc.CommandText = $"INSERT INTO {AssociationsTable}(vector_id, entity_type, entity_id) VALUES (@vecId, 'document', @docId)";
                        cmdAssoc.Parameters.AddWithValue("@vecId", vectorId);
                        cmdAssoc.Parameters.AddWithValue("@docId", documentId);
                        cmdAssoc.ExecuteNonQuery();
                    }
                }
            }
            else
            {
                // No similar vector exists, insert new document and vector
                using (var cmdDoc = _connection.CreateCommand())
                {
                    cmdDoc.CommandText = $"INSERT INTO {DocumentTable}(title, content, metadata) VALUES (@title, @content, @metadata)";
                    cmdDoc.Parameters.AddWithValue("@title", title);
                    cmdDoc.Parameters.AddWithValue("@content", content);
                    cmdDoc.Parameters.AddWithValue("@metadata", metadata ?? "{}");
                    cmdDoc.ExecuteNonQuery();

                    cmdDoc.CommandText = "SELECT last_insert_rowid()";
                    documentId = Convert.ToInt64(cmdDoc.ExecuteScalar());
                }

                // Insert vector
                var vecJson = JsonSerializer.Serialize(vector);
                using (var cmdVec = _connection.CreateCommand())
                {
                    cmdVec.CommandText = $"INSERT INTO {VectorTable}(sample_embedding) VALUES (@vec)";
                    cmdVec.Parameters.AddWithValue("@vec", vecJson);
                    cmdVec.ExecuteNonQuery();

                    cmdVec.CommandText = "SELECT last_insert_rowid()";
                    vectorId = Convert.ToInt64(cmdVec.ExecuteScalar());
                }

                // Create association
                using (var cmdAssoc = _connection.CreateCommand())
                {
                    cmdAssoc.CommandText = $"INSERT INTO {AssociationsTable}(vector_id, entity_type, entity_id) VALUES (@vecId, 'document', @docId)";
                    cmdAssoc.Parameters.AddWithValue("@vecId", vectorId);
                    cmdAssoc.Parameters.AddWithValue("@docId", documentId);
                    cmdAssoc.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            return (documentId, vectorId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private string ProcessFtsQuery(string query)
    {
        // Remove special FTS5 characters or convert them appropriately
        return query.Replace("?", "")  
                    .Replace(".", "")// Remove question marks
                    .Replace("*", "\\*")  // Escape asterisks
                    .Replace("\"", "\\\"") // Escape quotes
                    .Replace("(", "\\(")  // Escape parentheses
                    .Replace(")", "\\)")
                    .Replace("^", "\\^"); // Escape other special chars
    }

    /// <summary>
    /// Performs a full-text search against the document store
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of tuples containing document ID, title, content snippet, and metadata</returns>
    public List<(long DocId, string Title, string Content, string Metadata)> SearchDocuments(string query, int limit = 10)
    {
        var results = new List<(long, string, string, string)>();

        // Process the query for FTS5
        string processedQuery = ProcessFtsQuery(query);

        using var cmd = _connection.CreateCommand();
        // Use FTS5 highlight function for snippets
        cmd.CommandText = $@"
        SELECT 
            rowid, 
            title,
            highlight({DocumentTable}, 2, '<mark>', '</mark>') AS snippet,
            metadata
        FROM {DocumentTable} 
        WHERE {DocumentTable} MATCH @query
        ORDER BY rank
        LIMIT @limit";
        cmd.Parameters.AddWithValue("@query", processedQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }

        return results;
    }


    /// <summary>
    /// Gets a document by its ID
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <returns>Tuple containing document title, content, and metadata</returns>
    public (string Title, string Content, string Metadata)? GetDocumentById(long documentId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT title, content, metadata FROM {DocumentTable} WHERE rowid = @id";
        cmd.Parameters.AddWithValue("@id", documentId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)
            );
        }

        return null;
    }

    // ===================== KNOWLEDGE GRAPH API ======================

    /// <summary>
    /// Inserts a graph node with auto-generated ID
    /// </summary>
    /// <param name="label">Node label</param>
    /// <param name="type">Optional node type</param>
    /// <param name="propertiesJson">Optional JSON properties</param>
    /// <returns>The ID of the inserted node</returns>
    public long InsertGraphNode(string label, string type = null, string propertiesJson = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {NodeTable}(label, type, properties) VALUES (@label, @type, @props) RETURNING id";
        cmd.Parameters.AddWithValue("@label", label);
        cmd.Parameters.AddWithValue("@type", type as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@props", string.IsNullOrEmpty(propertiesJson) ? "{}" : propertiesJson);

        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Inserts a graph node with its vector embedding in a single operation
    /// </summary>
    /// <param name="label">Node label</param>
    /// <param name="vector">Vector embedding of the node</param>
    /// <param name="type">Optional node type</param>
    /// <param name="propertiesJson">Optional JSON properties</param>
    /// <returns>A tuple containing the node ID and vector ID</returns>
    public (long NodeId, long VectorId) InsertGraphNodeWithVector(string label, float[] vector, string type = null, string propertiesJson = null)
    {
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector must have {_dimensions} dimensions.");

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Insert node
            long nodeId;
            using (var cmdNode = _connection.CreateCommand())
            {
                cmdNode.CommandText = $"INSERT INTO {NodeTable}(label, type, properties) VALUES (@label, @type, @props) RETURNING id";
                cmdNode.Parameters.AddWithValue("@label", label);
                cmdNode.Parameters.AddWithValue("@type", type as object ?? DBNull.Value);
                cmdNode.Parameters.AddWithValue("@props", string.IsNullOrEmpty(propertiesJson) ? "{}" : propertiesJson);
                nodeId = Convert.ToInt64(cmdNode.ExecuteScalar());
            }

            // Insert vector
            var vecJson = JsonSerializer.Serialize(vector);
            long vectorId;
            using (var cmdVec = _connection.CreateCommand())
            {
                cmdVec.CommandText = $"INSERT INTO {VectorTable}(sample_embedding) VALUES (@vec)";
                cmdVec.Parameters.AddWithValue("@vec", vecJson);
                cmdVec.ExecuteNonQuery();

                cmdVec.CommandText = "SELECT last_insert_rowid()";
                vectorId = Convert.ToInt64(cmdVec.ExecuteScalar());
            }

            // Create association
            using (var cmdAssoc = _connection.CreateCommand())
            {
                cmdAssoc.CommandText = $"INSERT INTO {AssociationsTable}(vector_id, entity_type, entity_id) VALUES (@vecId, 'node', @nodeId)";
                cmdAssoc.Parameters.AddWithValue("@vecId", vectorId);
                cmdAssoc.Parameters.AddWithValue("@nodeId", nodeId);
                cmdAssoc.ExecuteNonQuery();
            }

            transaction.Commit();
            return (nodeId, vectorId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Inserts a graph edge with auto-generated ID
    /// </summary>
    /// <param name="sourceId">Source node ID</param>
    /// <param name="targetId">Target node ID</param>
    /// <param name="relation">Edge relation type</param>
    /// <param name="weight">Optional edge weight</param>
    /// <param name="propertiesJson">Optional JSON properties</param>
    /// <returns>The ID of the inserted edge</returns>
    public long InsertGraphEdge(long sourceId, long targetId, string relation, double weight = 1.0, string propertiesJson = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {EdgeTable}(source, target, rel, weight, properties) VALUES (@source, @target, @rel, @weight, @props) RETURNING id";
        cmd.Parameters.AddWithValue("@source", sourceId);
        cmd.Parameters.AddWithValue("@target", targetId);
        cmd.Parameters.AddWithValue("@rel", relation);
        cmd.Parameters.AddWithValue("@weight", weight);
        cmd.Parameters.AddWithValue("@props", string.IsNullOrEmpty(propertiesJson) ? "{}" : propertiesJson);

        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Gets a graph node by ID
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <returns>Tuple containing node label, type, and properties</returns>
    public (string Label, string Type, string Properties)? GetGraphNodeById(long nodeId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT label, type, properties FROM {NodeTable} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", nodeId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            string nodeType = reader.IsDBNull(1) ? null : reader.GetString(1);
            return (
                reader.GetString(0),
                nodeType,
                reader.GetString(2)
            );
        }

        return null;
    }

    /// <summary>
    /// Searches for graph nodes by label with optional fuzzy matching
    /// </summary>
    /// <param name="labelPattern">Label pattern to search for</param>
    /// <param name="fuzzyMatch">Whether to use fuzzy matching</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of tuples containing node ID, label, type, and properties</returns>
    public List<(long NodeId, string Label, string Type, string Properties)> SearchGraphNodes(
        string labelPattern,
        bool fuzzyMatch = true,
        int limit = 100)
    {
        var results = new List<(long, string, string, string)>();

        using var cmd = _connection.CreateCommand();
        var whereClause = fuzzyMatch ? "label LIKE @pattern" : "label = @exact";

        cmd.CommandText = $"SELECT id, label, type, properties FROM {NodeTable} WHERE {whereClause} LIMIT @limit";

        if (fuzzyMatch)
        {
            cmd.Parameters.AddWithValue("@pattern", "%" + labelPattern + "%");
        }
        else
        {
            cmd.Parameters.AddWithValue("@exact", labelPattern);
        }

        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string nodeType = reader.IsDBNull(2) ? null : reader.GetString(2);
            results.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                nodeType,
                reader.GetString(3)
            ));
        }

        return results;
    }

    /// <summary>
    /// Finds neighboring nodes connected to the specified node
    /// </summary>
    /// <param name="nodeId">Starting node ID</param>
    /// <param name="relationFilter">Optional relation type filter</param>
    /// <param name="direction">Direction: "outgoing", "incoming", or "both"</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of tuples containing neighbor node ID, label, relation, and edge properties</returns>
    public List<(long NodeId, string Label, string Relation, string EdgeProperties)> GetNeighbors(
        long nodeId,
        string relationFilter = null,
        string direction = "both",
        int limit = 100)
    {
        var results = new List<(long, string, string, string)>();

        using var cmd = _connection.CreateCommand();
        var relClause = relationFilter != null ? "AND e.rel = @relation" : "";

        string sql = direction.ToLower() switch
        {
            "outgoing" => $@"
                SELECT n.id, n.label, e.rel, e.properties
                FROM {EdgeTable} e
                JOIN {NodeTable} n ON e.target = n.id
                WHERE e.source = @nodeId {relClause}
                LIMIT @limit",

            "incoming" => $@"
                SELECT n.id, n.label, e.rel, e.properties
                FROM {EdgeTable} e
                JOIN {NodeTable} n ON e.source = n.id
                WHERE e.target = @nodeId {relClause}
                LIMIT @limit",

            _ => $@"
                SELECT n.id, n.label, e.rel, e.properties
                FROM {EdgeTable} e
                JOIN {NodeTable} n ON (e.target = n.id AND e.source = @nodeId)
                    OR (e.source = n.id AND e.target = @nodeId)
                WHERE (e.source = @nodeId OR e.target = @nodeId) {relClause}
                LIMIT @limit"
        };

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@nodeId", nodeId);

        if (relationFilter != null)
        {
            cmd.Parameters.AddWithValue("@relation", relationFilter);
        }

        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }

        return results;
    }

    // ===================== SEMANTIC SEARCH API ======================

    /// <summary>
    /// Represents a semantic search result
    /// </summary>
    public class SemanticSearchResult
    {
        public string EntityType { get; set; }  // 'document' or 'node'
        public long EntityId { get; set; }
        public double Similarity { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Metadata { get; set; }
    }

    /// <summary>
    /// Performs semantic search across all entities (documents and nodes) using a query vector
    /// </summary>
    /// <param name="queryVector">The query vector for semantic search</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of semantic search results</returns>
    public List<SemanticSearchResult> SemanticSearch(float[] queryVector, int limit = 10)
    {
        // First get nearest vectors
        var nearestVectors = KnnQuery(queryVector, limit);
        var results = new List<SemanticSearchResult>();

        if (nearestVectors.Count == 0)
            return results;

        // Add debug output to see what's happening
        Console.WriteLine($"Found {nearestVectors.Count} matching vectors");

        foreach (var vectorMatch in nearestVectors)
        {
            // Get associations for each vector individually
            var associations = GetVectorAssociations(vectorMatch.RowId);

            foreach (var assoc in associations)
            {
                // Get the actual entity based on type
                if (assoc.EntityType == "document")
                {
                    var doc = GetDocumentById(assoc.EntityId);
                    if (doc.HasValue)
                    {
                        results.Add(new SemanticSearchResult
                        {
                            EntityType = "document",
                            EntityId = assoc.EntityId,
                            Similarity = vectorMatch.Distance,
                            Title = doc.Value.Title,
                            Content = doc.Value.Content,
                            Metadata = doc.Value.Metadata
                        });
                    }
                }
                else if (assoc.EntityType == "node")
                {
                    // Handle node entities similarly...
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Links a vector to an entity (document or node)
    /// </summary>
    /// <param name="vectorId">The vector ID</param>
    /// <param name="entityType">The entity type ('document' or 'node')</param>
    /// <param name="entityId">The entity ID</param>
    /// <param name="relevance">Optional relevance score</param>
    public void AssociateVectorWithEntity(long vectorId, string entityType, long entityId, double relevance = 1.0)
    {
        if (entityType != "document" && entityType != "node")
            throw new ArgumentException("Entity type must be 'document' or 'node'");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            INSERT OR REPLACE INTO {AssociationsTable}(vector_id, entity_type, entity_id, relevance)
            VALUES (@vecId, @type, @entId, @rel)";
        cmd.Parameters.AddWithValue("@vecId", vectorId);
        cmd.Parameters.AddWithValue("@type", entityType);
        cmd.Parameters.AddWithValue("@entId", entityId);
        cmd.Parameters.AddWithValue("@rel", relevance);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Get entities associated with a vector
    /// </summary>
    /// <param name="vectorId">The vector ID</param>
    /// <returns>List of associated entities</returns>
    public List<(string EntityType, long EntityId, double Relevance)> GetVectorAssociations(long vectorId)
    {
        var results = new List<(string, long, double)>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT entity_type, entity_id, relevance FROM {AssociationsTable} WHERE vector_id = @id";
        cmd.Parameters.AddWithValue("@id", vectorId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetDouble(2)
            ));
        }

        return results;
    }

    /// <summary>
    /// Performs hybrid search combining vector similarity and text search
    /// </summary>
    /// <param name="queryText">The text query</param>
    /// <param name="queryVector">The vector embedding of the query</param>
    /// <param name="vectorWeight">Weight given to vector similarity (0-1)</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>List of semantic search results</returns>
    public List<SemanticSearchResult> HybridSearch(string queryText, float[] queryVector, double vectorWeight = 0.7, int limit = 10)
    {
        // First, get results from vector search
        var vectorResults = SemanticSearch(queryVector, limit * 2);

        // Second, get results from text search - but only for documents
        var textResults = new List<SemanticSearchResult>();
        var textDocResults = SearchDocuments(queryText, limit * 2);

        foreach (var doc in textDocResults)
        {
            textResults.Add(new SemanticSearchResult
            {
                EntityType = "document",
                EntityId = doc.DocId,
                Similarity = 0.0, // Will be computed in fusion step
                Title = doc.Title,
                Content = doc.Content,
                Metadata = doc.Metadata
            });
        }

        // Search graph nodes by label too
        var nodeResults = SearchGraphNodes(queryText, true, limit * 2);
        foreach (var node in nodeResults)
        {
            textResults.Add(new SemanticSearchResult
            {
                EntityType = "node",
                EntityId = node.NodeId,
                Similarity = 0.0, // Will be computed in fusion step
                Title = node.Label,
                Content = null,
                Metadata = node.Properties
            });
        }

        // Perform a simple fusion by combining results
        var fusedResults = new Dictionary<(string, long), SemanticSearchResult>();

        // Add vector results with their weights
        foreach (var result in vectorResults)
        {
            var key = (result.EntityType, result.EntityId);
            result.Similarity *= vectorWeight;
            fusedResults[key] = result;
        }

        // Add text results with their weights
        double textWeight = 1.0 - vectorWeight;
        for (int i = 0; i < textResults.Count; i++)
        {
            var result = textResults[i];
            var key = (result.EntityType, result.EntityId);

            // Simple ranking function that decreases with position
            double textScore = 1.0 - i / (double)textResults.Count;
            textScore *= textWeight;

            if (fusedResults.TryGetValue(key, out var existingResult))
            {
                // If this entity already exists from vector search, combine scores
                existingResult.Similarity += textScore;
            }
            else
            {
                // Otherwise add a new result
                result.Similarity = textScore;
                fusedResults[key] = result;
            }
        }

        // Return the top results
        return fusedResults.Values
            .OrderByDescending(r => r.Similarity)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Disposes the connection
    /// </summary>
    public void Dispose()
    {
        _connection?.Dispose();
    }
}