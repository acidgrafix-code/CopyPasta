using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CopyPasta.Data;

/// <summary>
/// Owns the SQLite database: connection strings, schema creation and migration.
/// </summary>
/// <remarks>
/// <para>
/// The schema mirrors the macOS one (<c>SQLiteDataSchema.swift</c>) closely enough that the two
/// ports stay comparable: a clip row plus one blob row per stored format. macOS's
/// <c>index</c> column is <c>ordinal</c> here, because <c>index</c> reads badly in SQL.
/// </para>
/// <para>
/// Connections are opened per operation rather than held open. Microsoft.Data.Sqlite pools them
/// by connection string, and WAL mode lets a reader run while a writer commits — which matters
/// because capture writes on the clipboard-notification path while the menu reads on the UI path.
/// </para>
/// </remarks>
public sealed class ClipDatabase : IDisposable
{
    /// <summary>Bumped whenever the schema changes; drives <see cref="Migrate"/>.</summary>
    public const int SchemaVersion = 4;

    private readonly string _connectionString;

    /// <summary>
    /// Held open only for in-memory databases. A shared-cache in-memory database exists only
    /// while at least one connection to it is open, so without this it would be destroyed
    /// between operations.
    /// </summary>
    private readonly SqliteConnection? _keepAlive;

    public ClipDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        Description = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private ClipDatabase(string connectionString, string description, bool keepAlive)
    {
        _connectionString = connectionString;
        Description = description;

        if (keepAlive)
        {
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
        }
    }

    /// <summary>Where the data lives; a path, or a name for an in-memory database.</summary>
    public string Description { get; }

    /// <summary>
    /// An in-memory database for tests. Each distinct <paramref name="name"/> is an independent
    /// database, and it lives until this instance is disposed.
    /// </summary>
    public static ClipDatabase InMemory(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();

        return new ClipDatabase(connectionString, name, keepAlive: true);
    }

    public SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();

        using SqliteCommand pragmas = connection.CreateCommand();
        // journal_mode persists in the file; the rest are per-connection and set every time.
        // WAL is a no-op for in-memory databases, which report journal_mode = memory.
        pragmas.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        pragmas.ExecuteNonQuery();

        return connection;
    }

    /// <summary>Creates or upgrades the schema. Safe to call on every start.</summary>
    public void Migrate()
    {
        using SqliteConnection connection = OpenConnection();

        int current = GetSchemaVersion(connection);
        if (current >= SchemaVersion)
        {
            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();

        if (current < 1)
        {
            Execute(connection, transaction, CreateSchemaV1);
        }

        if (current < 2)
        {
            Execute(connection, transaction, CreateSchemaV2);
        }

        if (current < 3)
        {
            Execute(connection, transaction, CreateSchemaV3);
        }

        if (current < 4)
        {
            Execute(connection, transaction, CreateSchemaV4);
        }

        transaction.Commit();

        // PRAGMA user_version is set outside the transaction: SQLite treats it as a header
        // write, and setting it inside a transaction that later rolls back leaves the version
        // and the schema disagreeing.
        SetSchemaVersion(connection, SchemaVersion);
    }

    private const string CreateSchemaV1 = """
        CREATE TABLE IF NOT EXISTS clip (
            id            TEXT    NOT NULL PRIMARY KEY,
            content_hash  TEXT    NOT NULL,
            title         TEXT    NOT NULL,
            ocr_text      TEXT        NULL,
            formats       TEXT    NOT NULL,
            created_at    INTEGER NOT NULL,
            updated_at    INTEGER NOT NULL,
            is_concealed  INTEGER NOT NULL DEFAULT 0,
            is_from_cloud INTEGER NOT NULL DEFAULT 0
        ) STRICT;

        -- Both orders are indexed because the history can be sorted by either, and trimming
        -- has to use the same order the menu shows or it deletes the wrong clips.
        CREATE INDEX IF NOT EXISTS ix_clip_updated_at ON clip (updated_at DESC);
        CREATE INDEX IF NOT EXISTS ix_clip_created_at ON clip (created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_clip_content_hash ON clip (content_hash);

        CREATE TABLE IF NOT EXISTS clip_asset (
            id       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            clip_id  TEXT    NOT NULL REFERENCES clip (id) ON DELETE CASCADE,
            ordinal  INTEGER NOT NULL,
            format   TEXT    NOT NULL,
            data     BLOB    NOT NULL
        ) STRICT;

        CREATE UNIQUE INDEX IF NOT EXISTS ix_clip_asset_clip_ordinal
            ON clip_asset (clip_id, ordinal);
        """;

    /// <summary>
    /// Menu preview images. Port of macOS <c>PasteboardHistoryThumbnailAsset</c>.
    /// </summary>
    /// <remarks>
    /// A separate table rather than a column on <c>clip</c>, so the menu can read titles and formats
    /// for thirty rows without dragging thirty images along with them. One row per clip: a clip has
    /// at most one preview.
    /// </remarks>
    private const string CreateSchemaV2 = """
        CREATE TABLE IF NOT EXISTS clip_thumbnail (
            clip_id TEXT NOT NULL PRIMARY KEY REFERENCES clip (id) ON DELETE CASCADE,
            kind    TEXT NOT NULL,
            data    BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// Snippets. Port of the macOS <c>SnippetFolder</c> and <c>Snippet</c> tables.
    /// </summary>
    /// <remarks>
    /// Entirely separate from the clip tables: snippets are user-authored and permanent, while
    /// clips are captured and continuously trimmed. macOS's <c>index</c> column is <c>ordinal</c>
    /// here for the same reason as on clips.
    /// </remarks>
    private const string CreateSchemaV3 = """
        CREATE TABLE IF NOT EXISTS snippet_folder (
            id         TEXT    NOT NULL PRIMARY KEY,
            title      TEXT    NOT NULL,
            ordinal    INTEGER NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1
        ) STRICT;

        CREATE INDEX IF NOT EXISTS ix_snippet_folder_ordinal ON snippet_folder (ordinal);

        CREATE TABLE IF NOT EXISTS snippet (
            id         TEXT    NOT NULL PRIMARY KEY,
            folder_id  TEXT    NOT NULL REFERENCES snippet_folder (id) ON DELETE CASCADE,
            title      TEXT    NOT NULL,
            content    TEXT    NOT NULL,
            ordinal    INTEGER NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1
        ) STRICT;

        CREATE INDEX IF NOT EXISTS ix_snippet_folder_ordinal_within
            ON snippet (folder_id, ordinal);
        """;

    /// <summary>
    /// Full-text search over clip titles and their recognised text. Port of the macOS
    /// <c>PasteboardHistorySearch</c> FTS5 table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A standalone FTS5 table keyed by clip id rather than an external-content one. External
    /// content is more compact but ties the index to <c>clip</c>'s rowid, and every delete path
    /// then has to remember to keep the two in step.
    /// </para>
    /// <para>
    /// Kept in sync by triggers, not by the repository. Trimming, clearing and the paste-and-delete
    /// modifier all delete clips by different routes; a trigger cannot be forgotten by a new one.
    /// </para>
    /// </remarks>
    private const string CreateSchemaV4 = """
        CREATE VIRTUAL TABLE IF NOT EXISTS clip_search
            USING fts5(clip_id UNINDEXED, title, ocr_text, tokenize = 'unicode61');

        CREATE TRIGGER IF NOT EXISTS clip_search_insert AFTER INSERT ON clip BEGIN
            INSERT INTO clip_search (clip_id, title, ocr_text)
            VALUES (new.id, new.title, COALESCE(new.ocr_text, ''));
        END;

        CREATE TRIGGER IF NOT EXISTS clip_search_update AFTER UPDATE ON clip BEGIN
            UPDATE clip_search
            SET title = new.title, ocr_text = COALESCE(new.ocr_text, '')
            WHERE clip_id = new.id;
        END;

        CREATE TRIGGER IF NOT EXISTS clip_search_delete AFTER DELETE ON clip BEGIN
            DELETE FROM clip_search WHERE clip_id = old.id;
        END;

        -- Backfill whatever was captured before the index existed.
        INSERT INTO clip_search (clip_id, title, ocr_text)
        SELECT id, title, COALESCE(ocr_text, '') FROM clip
        WHERE id NOT IN (SELECT clip_id FROM clip_search);
        """;

    private static int GetSchemaVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        // PRAGMA does not accept parameters; the value is a compile-time constant.
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _keepAlive?.Dispose();
}
