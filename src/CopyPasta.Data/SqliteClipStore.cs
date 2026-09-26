using System.Text.Json;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using Microsoft.Data.Sqlite;

namespace CopyPasta.Data;

/// <summary>How the history is ordered. Port of the macOS <c>sortsByCreatedAt</c> flag.</summary>
public enum ClipOrder
{
    /// <summary>Newest first by when the content was first seen.</summary>
    CreatedAt,

    /// <summary>Most recently used first. The macOS default.</summary>
    UpdatedAt,
}

/// <summary>A clip row without its blobs — enough to build a menu item.</summary>
public sealed record ClipSummary
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<ClipboardFormat> Formats { get; init; }

    public required long CreatedAt { get; init; }

    public required long UpdatedAt { get; init; }

    public required bool IsConcealed { get; init; }

    public required bool IsFromCloudClipboard { get; init; }

    /// <summary>The primary format, or null for a row with no formats recorded.</summary>
    public ClipboardFormat? PrimaryFormat => Formats.Count > 0 ? Formats[0] : null;
}

/// <summary>SQLite-backed clip storage.</summary>
/// <remarks>
/// Port of macOS <c>PasteboardHistoryRepository</c>. Reads are deliberately split: the menu
/// needs titles and formats for every row, but blobs only for the row the user picks, and a
/// history of screenshots makes that difference enormous.
/// </remarks>
public sealed class SqliteClipStore : IClipStore
{
    private readonly ClipDatabase _database;

    public SqliteClipStore(ClipDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public bool Exists(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM clip WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", clipId);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>True when any clip is stored. Drives the "Clear History" menu item's enabled state.</summary>
    public bool Any()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM clip LIMIT 1;";
        return command.ExecuteScalar() is not null;
    }

    public int Count()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM clip;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Save(StoredClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        bool existed;
        using (SqliteCommand probe = connection.CreateCommand())
        {
            probe.Transaction = transaction;
            probe.CommandText = "SELECT 1 FROM clip WHERE id = $id LIMIT 1;";
            probe.Parameters.AddWithValue("$id", clip.Id);
            existed = probe.ExecuteScalar() is not null;
        }

        if (existed)
        {
            // The assets are identical by construction — the id is the content hash whenever a
            // conflict is possible — so only the timestamp moves. created_at is left alone to
            // keep "date created" ordering meaningful.
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE clip
                SET updated_at = $updated, title = $title
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$id", clip.Id);
            update.Parameters.AddWithValue("$title", clip.Title);
            update.Parameters.AddWithValue("$updated", clip.UpdatedAt);
            update.ExecuteNonQuery();
        }
        else
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO clip (
                    id, content_hash, title, ocr_text, formats,
                    created_at, updated_at, is_concealed, is_from_cloud
                )
                VALUES (
                    $id, $hash, $title, NULL, $formats,
                    $created, $updated, $concealed, $cloud
                );
                """;
            insert.Parameters.AddWithValue("$id", clip.Id);
            insert.Parameters.AddWithValue("$hash", clip.ContentHash);
            insert.Parameters.AddWithValue("$title", clip.Title);
            insert.Parameters.AddWithValue("$formats", SerializeFormats(clip.Formats));
            insert.Parameters.AddWithValue("$created", clip.CreatedAt);
            insert.Parameters.AddWithValue("$updated", clip.UpdatedAt);
            insert.Parameters.AddWithValue("$concealed", clip.IsConcealed ? 1 : 0);
            insert.Parameters.AddWithValue("$cloud", clip.IsFromCloudClipboard ? 1 : 0);
            insert.ExecuteNonQuery();

            InsertAssets(connection, transaction, clip);
        }

        transaction.Commit();
    }

    private static void InsertAssets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredClip clip)
    {
        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO clip_asset (clip_id, ordinal, format, data)
            VALUES ($clip, $ordinal, $format, $data);
            """;

        SqliteParameter clipId = insert.Parameters.Add("$clip", SqliteType.Text);
        SqliteParameter ordinal = insert.Parameters.Add("$ordinal", SqliteType.Integer);
        SqliteParameter format = insert.Parameters.Add("$format", SqliteType.Text);
        SqliteParameter data = insert.Parameters.Add("$data", SqliteType.Blob);

        clipId.Value = clip.Id;

        for (int index = 0; index < clip.Assets.Count; index++)
        {
            ordinal.Value = index;
            format.Value = clip.Assets[index].Format.Name;
            data.Value = clip.Assets[index].Data;
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>The most recent clips, without blobs.</summary>
    public IReadOnlyList<ClipSummary> FetchRecent(ClipOrder order, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, title, formats, created_at, updated_at, is_concealed, is_from_cloud
            FROM clip
            ORDER BY {OrderColumn(order)} DESC, id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        List<ClipSummary> summaries = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(ReadSummary(reader));
        }

        return summaries;
    }

    private static ClipSummary ReadSummary(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Title = reader.GetString(1),
        Formats = DeserializeFormats(reader.GetString(2)),
        CreatedAt = reader.GetInt64(3),
        UpdatedAt = reader.GetInt64(4),
        IsConcealed = reader.GetInt64(5) != 0,
        IsFromCloudClipboard = reader.GetInt64(6) != 0,
    };

    /// <summary>
    /// The full content of one clip, blobs included, or null when the id is unknown.
    /// </summary>
    public ClipContent? FetchContent(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT format, data
            FROM clip_asset
            WHERE clip_id = $id
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$id", clipId);

        List<ClipAsset> assets = [];
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                assets.Add(new ClipAsset(
                    ClipboardFormat.FromName(reader.GetString(0)),
                    ReadBlob(reader, 1)));
            }
        }

        return ClipContent.TryCreate(assets, out ClipContent? content) ? content : null;
    }

    public bool Delete(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clip WHERE id = $id;";
        command.Parameters.AddWithValue("$id", clipId);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Stores or replaces a clip's menu preview.</summary>
    public void SaveThumbnail(string clipId, string kind, byte[] data)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(data);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clip_thumbnail (clip_id, kind, data)
            VALUES ($clip, $kind, $data)
            ON CONFLICT (clip_id) DO UPDATE SET kind = excluded.kind, data = excluded.data;
            """;
        command.Parameters.AddWithValue("$clip", clipId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$data", data);
        command.ExecuteNonQuery();
    }

    /// <summary>A clip's menu preview, or null when it has none.</summary>
    public (string Kind, byte[] Data)? FetchThumbnail(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT kind, data FROM clip_thumbnail WHERE clip_id = $clip;";
        command.Parameters.AddWithValue("$clip", clipId);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetString(0), ReadBlob(reader, 1))
            : null;
    }

    /// <summary>
    /// Every clip's preview in one query, keyed by clip id.
    /// </summary>
    /// <remarks>
    /// The menu needs previews for every visible row at once, and doing that one round trip per row
    /// is the kind of thing that makes a menu feel slow with a full history.
    /// </remarks>
    public IReadOnlyDictionary<string, (string Kind, byte[] Data)> FetchThumbnails(
        IReadOnlyCollection<string> clipIds)
    {
        ArgumentNullException.ThrowIfNull(clipIds);

        if (clipIds.Count == 0)
        {
            return new Dictionary<string, (string, byte[])>();
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        // Parameterised rather than interpolated, even though the ids are internal hashes.
        List<string> placeholders = [];
        int index = 0;
        foreach (string clipId in clipIds)
        {
            string name = $"$id{index++}";
            placeholders.Add(name);
            command.Parameters.AddWithValue(name, clipId);
        }

        command.CommandText =
            $"SELECT clip_id, kind, data FROM clip_thumbnail WHERE clip_id IN ({string.Join(", ", placeholders)});";

        Dictionary<string, (string Kind, byte[] Data)> thumbnails = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            thumbnails[reader.GetString(0)] = (reader.GetString(1), ReadBlob(reader, 2));
        }

        return thumbnails;
    }

    public bool Touch(string clipId, long updatedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE clip SET updated_at = $updated WHERE id = $id;";
        command.Parameters.AddWithValue("$id", clipId);
        command.Parameters.AddWithValue("$updated", updatedAt);
        return command.ExecuteNonQuery() > 0;
    }

    public void DeleteAll()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clip;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes everything beyond the newest <paramref name="maximumCount"/> clips.
    /// Port of <c>deleteOverflowingHistories</c>.
    /// </summary>
    /// <remarks>
    /// The order must match the order the menu displays, or this trims from the wrong end.
    /// A non-positive limit means "keep nothing", matching macOS.
    /// </remarks>
    public int DeleteOverflowing(ClipOrder order, int maximumCount)
    {
        if (maximumCount <= 0)
        {
            using SqliteConnection wipe = _database.OpenConnection();
            using SqliteCommand deleteAll = wipe.CreateCommand();
            deleteAll.CommandText = "DELETE FROM clip;";
            return deleteAll.ExecuteNonQuery();
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM clip
            WHERE id IN (
                SELECT id FROM clip
                ORDER BY {OrderColumn(order)} DESC, id
                LIMIT -1 OFFSET $keep
            );
            """;
        command.Parameters.AddWithValue("$keep", maximumCount);
        return command.ExecuteNonQuery();
    }

    /// <summary>Records the text recognised in a clip's image.</summary>
    /// <remarks>The search index follows automatically, through a trigger on <c>clip</c>.</remarks>
    public bool SaveOcrText(string clipId, string ocrText)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);
        ArgumentNullException.ThrowIfNull(ocrText);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE clip SET ocr_text = $text WHERE id = $id;";
        command.Parameters.AddWithValue("$id", clipId);
        command.Parameters.AddWithValue("$text", ocrText);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// A clip's recognised text, or null when it has not been scanned yet.
    /// </summary>
    /// <remarks>
    /// Null and empty are distinct: null means no attempt has been made, empty means a completed
    /// scan that found nothing.
    /// </remarks>
    public string? FetchOcrText(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ocr_text FROM clip WHERE id = $id;";
        command.Parameters.AddWithValue("$id", clipId);

        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>Clips that have had no OCR attempt yet, oldest first.</summary>
    /// <remarks>
    /// Lets a restart pick up work that was interrupted, rather than leaving clips permanently
    /// unsearchable because the app closed mid-recognition.
    /// </remarks>
    public IReadOnlyList<string> FetchClipsAwaitingOcr(int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id FROM clip
            WHERE ocr_text IS NULL
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        List<string> ids = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>
    /// Clips whose title or recognised text matches, newest first.
    /// </summary>
    /// <remarks>
    /// The query is tokenised and re-quoted rather than passed through. FTS5 has its own syntax
    /// where bare punctuation is a syntax error, so typing <c>c++</c> into a search box would
    /// otherwise throw rather than search. Each word also gets a trailing <c>*</c> so results
    /// narrow as the user types.
    /// </remarks>
    public IReadOnlyList<ClipSummary> Search(string query, ClipOrder order, int limit)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (limit <= 0)
        {
            return [];
        }

        string expression = BuildMatchExpression(query);
        if (expression.Length == 0)
        {
            return [];
        }

        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.id, c.title, c.formats, c.created_at, c.updated_at, c.is_concealed, c.is_from_cloud
            FROM clip_search s
            JOIN clip c ON c.id = s.clip_id
            WHERE clip_search MATCH $query
            ORDER BY c.{OrderColumn(order)} DESC, c.id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", expression);
        command.Parameters.AddWithValue("$limit", limit);

        List<ClipSummary> results = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSummary(reader));
        }

        return results;
    }

    /// <summary>
    /// Turns user input into a safe FTS5 MATCH expression.
    /// </summary>
    internal static string BuildMatchExpression(string query)
    {
        List<string> terms = [];

        foreach (string raw in query.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Keep only what the tokeniser would index anyway, so punctuation cannot escape the
            // quoting and become syntax.
            string token = new([.. raw.Where(character => char.IsLetterOrDigit(character) || character == '_')]);

            if (token.Length > 0)
            {
                terms.Add($"\"{token}\"*");
            }
        }

        return string.Join(" ", terms);
    }

    /// <summary>
    /// Deletes the oldest clips until the stored blobs fit within a byte budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This has no macOS counterpart, and it is the gap the code review called out: macOS trims by
    /// row count only, so a history of thirty 4K screenshots is thirty rows and several hundred
    /// megabytes. A count says nothing about size when a clip can be a line of text or a full-screen
    /// bitmap.
    /// </para>
    /// <para>
    /// The newest clip is always kept, however large it is. Deleting the thing the user just copied
    /// because it exceeded the budget on its own would be worse than briefly exceeding the budget.
    /// </para>
    /// </remarks>
    public int DeleteOverflowingBytes(ClipOrder order, long maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            return 0;
        }

        using SqliteConnection connection = _database.OpenConnection();

        List<(string Id, long Bytes)> sized = [];

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT c.id, COALESCE(SUM(LENGTH(a.data)), 0) + COALESCE(MAX(t.size), 0)
                FROM clip c
                LEFT JOIN clip_asset a ON a.clip_id = c.id
                LEFT JOIN (SELECT clip_id, LENGTH(data) AS size FROM clip_thumbnail) t
                    ON t.clip_id = c.id
                GROUP BY c.id
                ORDER BY MAX(c.{OrderColumn(order)}) DESC, c.id;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                sized.Add((reader.GetString(0), reader.GetInt64(1)));
            }
        }

        List<string> doomed = [];
        long running = 0;

        for (int index = 0; index < sized.Count; index++)
        {
            running += sized[index].Bytes;

            // index > 0 keeps the newest clip whatever it costs.
            if (index > 0 && running > maximumBytes)
            {
                doomed.Add(sized[index].Id);
            }
        }

        if (doomed.Count == 0)
        {
            return 0;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM clip WHERE id = $id;";
        SqliteParameter id = delete.Parameters.Add("$id", SqliteType.Text);

        int removed = 0;
        foreach (string clipId in doomed)
        {
            id.Value = clipId;
            removed += delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return removed;
    }

    /// <summary>Total bytes held in clip blobs; the basis for the byte-budget cap.</summary>
    public long TotalAssetBytes()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(LENGTH(data)), 0) FROM clip_asset;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Reclaims file space after a large delete.</summary>
    public void Vacuum()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "VACUUM;";
        command.ExecuteNonQuery();
    }

    private static string OrderColumn(ClipOrder order) => order switch
    {
        ClipOrder.CreatedAt => "created_at",
        ClipOrder.UpdatedAt => "updated_at",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    private static byte[] ReadBlob(SqliteDataReader reader, int ordinal)
    {
        using Stream stream = reader.GetStream(ordinal);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string SerializeFormats(IReadOnlyList<ClipboardFormat> formats) =>
        JsonSerializer.Serialize(formats.Select(format => format.Name));

    private static IReadOnlyList<ClipboardFormat> DeserializeFormats(string json)
    {
        string[]? names = JsonSerializer.Deserialize<string[]>(json);
        if (names is null)
        {
            return [];
        }

        return names
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(ClipboardFormat.FromName)
            .ToArray();
    }
}
