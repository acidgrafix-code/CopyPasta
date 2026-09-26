using CopyPasta.Core.Snippets;
using Microsoft.Data.Sqlite;

namespace CopyPasta.Data;

/// <summary>SQLite-backed snippet storage. Port of macOS <c>SnippetRepository</c>.</summary>
public sealed class SqliteSnippetStore : ISnippetStore
{
    private readonly ClipDatabase _database;

    public SqliteSnippetStore(ClipDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    // ---- Reads --------------------------------------------------------------------------

    public IReadOnlyList<SnippetFolderDetail> FetchFolderDetails()
    {
        using SqliteConnection connection = _database.OpenConnection();

        List<SnippetFolder> folders = ReadFolders(connection, folderId: null);
        ILookup<Guid, Snippet> byFolder = ReadSnippets(connection, folderId: null)
            .ToLookup(snippet => snippet.FolderId);

        return folders
            .Select(folder => new SnippetFolderDetail(folder, byFolder[folder.Id].ToArray()))
            .ToArray();
    }

    public SnippetFolderDetail? FetchFolderDetail(Guid folderId)
    {
        using SqliteConnection connection = _database.OpenConnection();

        SnippetFolder? folder = ReadFolders(connection, folderId).FirstOrDefault();
        if (folder is null)
        {
            return null;
        }

        return new SnippetFolderDetail(folder, ReadSnippets(connection, folderId).ToArray());
    }

    public Snippet? FetchSnippet(Guid snippetId)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, folder_id, title, content, ordinal, is_enabled
            FROM snippet
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", snippetId.ToString());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadSnippet(reader) : null;
    }

    // ---- Folders -------------------------------------------------------------------------

    public SnippetFolder InsertFolder()
    {
        using SqliteConnection connection = _database.OpenConnection();

        SnippetFolder folder = new(
            Guid.NewGuid(),
            SnippetFolder.DefaultTitle,
            NextOrdinal(connection, "SELECT MAX(ordinal) FROM snippet_folder;", null));

        InsertFolderRow(connection, transaction: null, folder);
        return folder;
    }

    public IReadOnlyList<SnippetFolderDetail> InsertFolders(IReadOnlyList<ImportedFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (folders.Count == 0)
        {
            return [];
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        int ordinal = NextOrdinal(connection, "SELECT MAX(ordinal) FROM snippet_folder;", transaction);
        List<SnippetFolderDetail> inserted = [];

        foreach (ImportedFolder imported in folders)
        {
            SnippetFolder folder = new(Guid.NewGuid(), imported.Title, ordinal++);
            InsertFolderRow(connection, transaction, folder);

            List<Snippet> snippets = [];
            for (int index = 0; index < imported.Snippets.Count; index++)
            {
                ImportedSnippet source = imported.Snippets[index];
                Snippet snippet = new(
                    Guid.NewGuid(),
                    folder.Id,
                    source.Title,
                    source.Content,
                    index);

                InsertSnippetRow(connection, transaction, snippet);
                snippets.Add(snippet);
            }

            inserted.Add(new SnippetFolderDetail(folder, snippets));
        }

        transaction.Commit();
        return inserted;
    }

    public void UpdateFolderTitle(Guid folderId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        UpdateColumn("snippet_folder", "title", folderId, title);
    }

    public void UpdateFolderIsEnabled(Guid folderId, bool isEnabled) =>
        UpdateColumn("snippet_folder", "is_enabled", folderId, isEnabled ? 1 : 0);

    public void UpdateFolderOrder(IReadOnlyList<Guid> folderIds) =>
        ApplyOrder("snippet_folder", folderIds);

    public bool DeleteFolder(Guid folderId)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snippet_folder WHERE id = $id;";
        command.Parameters.AddWithValue("$id", folderId.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    // ---- Snippets --------------------------------------------------------------------------

    public Snippet? InsertSnippet(Guid folderId)
    {
        using SqliteConnection connection = _database.OpenConnection();

        // Guard explicitly rather than relying on the foreign key: the caller gets a null it can
        // act on instead of an exception from a folder deleted a moment ago.
        if (!FolderExists(connection, folderId))
        {
            return null;
        }

        using SqliteCommand next = connection.CreateCommand();
        next.CommandText = "SELECT MAX(ordinal) FROM snippet WHERE folder_id = $folder;";
        next.Parameters.AddWithValue("$folder", folderId.ToString());

        object? result = next.ExecuteScalar();
        int ordinal = result is null or DBNull ? 0 : Convert.ToInt32(result, Culture) + 1;

        Snippet snippet = new(
            Guid.NewGuid(),
            folderId,
            Snippet.DefaultTitle,
            string.Empty,
            ordinal);

        InsertSnippetRow(connection, transaction: null, snippet);
        return snippet;
    }

    public void UpdateSnippetTitle(Guid snippetId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        UpdateColumn("snippet", "title", snippetId, title);
    }

    public void UpdateSnippetContent(Guid snippetId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        UpdateColumn("snippet", "content", snippetId, content);
    }

    public void UpdateSnippetIsEnabled(Guid snippetId, bool isEnabled) =>
        UpdateColumn("snippet", "is_enabled", snippetId, isEnabled ? 1 : 0);

    public void UpdateSnippetOrder(IReadOnlyList<Guid> snippetIds) =>
        ApplyOrder("snippet", snippetIds);

    public void MoveSnippet(Guid snippetId, Guid folderId, IReadOnlyList<Guid> snippetIdsInDestination)
    {
        ArgumentNullException.ThrowIfNull(snippetIdsInDestination);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand move = connection.CreateCommand())
        {
            move.Transaction = transaction;
            move.CommandText = "UPDATE snippet SET folder_id = $folder WHERE id = $id;";
            move.Parameters.AddWithValue("$folder", folderId.ToString());
            move.Parameters.AddWithValue("$id", snippetId.ToString());
            move.ExecuteNonQuery();
        }

        // The move and the reorder are one transaction: a snippet that landed in the new folder
        // but kept the old folder's ordinal would sort unpredictably.
        ApplyOrder(connection, transaction, "snippet", snippetIdsInDestination);

        transaction.Commit();
    }

    public bool DeleteSnippet(Guid snippetId)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snippet WHERE id = $id;";
        command.Parameters.AddWithValue("$id", snippetId.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    // ---- Plumbing ---------------------------------------------------------------------------

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    private static List<SnippetFolder> ReadFolders(SqliteConnection connection, Guid? folderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = folderId is null
            ? "SELECT id, title, ordinal, is_enabled FROM snippet_folder ORDER BY ordinal, id;"
            : "SELECT id, title, ordinal, is_enabled FROM snippet_folder WHERE id = $id;";

        if (folderId is { } id)
        {
            command.Parameters.AddWithValue("$id", id.ToString());
        }

        List<SnippetFolder> folders = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new SnippetFolder(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt64(3) != 0));
        }

        return folders;
    }

    private static List<Snippet> ReadSnippets(SqliteConnection connection, Guid? folderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = folderId is null
            ? """
              SELECT id, folder_id, title, content, ordinal, is_enabled
              FROM snippet ORDER BY ordinal, id;
              """
            : """
              SELECT id, folder_id, title, content, ordinal, is_enabled
              FROM snippet WHERE folder_id = $folder ORDER BY ordinal, id;
              """;

        if (folderId is { } id)
        {
            command.Parameters.AddWithValue("$folder", id.ToString());
        }

        List<Snippet> snippets = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            snippets.Add(ReadSnippet(reader));
        }

        return snippets;
    }

    private static Snippet ReadSnippet(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.GetInt64(5) != 0);

    private static bool FolderExists(SqliteConnection connection, Guid folderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM snippet_folder WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", folderId.ToString());
        return command.ExecuteScalar() is not null;
    }

    private static int NextOrdinal(
        SqliteConnection connection,
        string maxQuery,
        SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = maxQuery;

        object? result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result, Culture) + 1;
    }

    private static void InsertFolderRow(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SnippetFolder folder)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO snippet_folder (id, title, ordinal, is_enabled)
            VALUES ($id, $title, $ordinal, $enabled);
            """;
        command.Parameters.AddWithValue("$id", folder.Id.ToString());
        command.Parameters.AddWithValue("$title", folder.Title);
        command.Parameters.AddWithValue("$ordinal", folder.Index);
        command.Parameters.AddWithValue("$enabled", folder.IsEnabled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static void InsertSnippetRow(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Snippet snippet)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO snippet (id, folder_id, title, content, ordinal, is_enabled)
            VALUES ($id, $folder, $title, $content, $ordinal, $enabled);
            """;
        command.Parameters.AddWithValue("$id", snippet.Id.ToString());
        command.Parameters.AddWithValue("$folder", snippet.FolderId.ToString());
        command.Parameters.AddWithValue("$title", snippet.Title);
        command.Parameters.AddWithValue("$content", snippet.Content);
        command.Parameters.AddWithValue("$ordinal", snippet.Index);
        command.Parameters.AddWithValue("$enabled", snippet.IsEnabled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void UpdateColumn(string table, string column, Guid id, object value)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        // Table and column are compile-time constants from this file, never caller input.
        command.CommandText = $"UPDATE {table} SET {column} = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private void ApplyOrder(string table, IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return;
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        ApplyOrder(connection, transaction, table, ids);

        transaction.Commit();
    }

    private static void ApplyOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<Guid> ids)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET ordinal = $ordinal WHERE id = $id;";

        SqliteParameter ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
        SqliteParameter id = command.Parameters.Add("$id", SqliteType.Text);

        for (int index = 0; index < ids.Count; index++)
        {
            ordinal.Value = index;
            id.Value = ids[index].ToString();
            command.ExecuteNonQuery();
        }
    }
}
