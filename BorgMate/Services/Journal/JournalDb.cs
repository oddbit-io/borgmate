using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BorgMate.Models;
using Microsoft.Data.Sqlite;

namespace BorgMate.Services.Journal;

public class JournalDb
{
    private readonly string _connectionString;

    public JournalDb()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BorgMate");
        Directory.CreateDirectory(dir);

        var dbPath = Path.Combine(dir, "journal.db");
        _connectionString = $"Data Source={dbPath}";

        using var connection = Open();
        EnsureSchema(connection);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();

        // Check if the current schema exists
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='entries'";
        var hasTable = cmd.ExecuteScalar() is not null;

        if (hasTable)
        {
            // Verify schema has the expected columns; drop and recreate if not
            cmd.CommandText = "PRAGMA table_info(entries)";
            using var reader = cmd.ExecuteReader();
            var columns = new System.Collections.Generic.HashSet<string>();
            while (reader.Read())
                columns.Add(reader.GetString(1));
            reader.Close();

            if (!columns.Contains("started_at") || !columns.Contains("result"))
            {
                cmd.CommandText = "DROP TABLE entries";
                cmd.ExecuteNonQuery();
                hasTable = false;
            }
        }

        if (!hasTable)
        {
            cmd.CommandText = """
                CREATE TABLE entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_kind INTEGER NOT NULL,
                    title_args TEXT,
                    detail TEXT,
                    repository_name TEXT,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    result INTEGER NOT NULL DEFAULT 0
                )
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public long Insert(JournalEntry entry)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO entries (event_kind, title_args, detail, repository_name, started_at, completed_at, result)
            VALUES ($eventKind, $titleArgs, $detail, $repoName, $startedAt, $completedAt, $result);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$eventKind", (int)entry.EventKind);
        cmd.Parameters.AddWithValue("$titleArgs",
            entry.TitleArgs is { Length: > 0 } ? JsonSerializer.Serialize(entry.TitleArgs) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$detail", (object?)entry.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$repoName", (object?)entry.RepositoryName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$completedAt", entry.CompletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$result", (int)entry.Result);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(JournalEntry entry)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE entries SET result = $result, completed_at = $completedAt, detail = $detail
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$result", (int)entry.Result);
        cmd.Parameters.AddWithValue("$completedAt", entry.CompletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$detail", (object?)entry.Detail ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<JournalEntry> LoadAll(int limit = 200)
    {
        var entries = new List<JournalEntry>();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, event_kind, title_args, detail, repository_name, started_at, completed_at, result FROM entries ORDER BY id DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var eventKind = (JournalEventKind)reader.GetInt32(1);
            var titleArgsJson = reader.IsDBNull(2) ? null : reader.GetString(2);
            var detail = reader.IsDBNull(3) ? null : reader.GetString(3);
            var repoName = reader.IsDBNull(4) ? null : reader.GetString(4);
            var startedAt = DateTime.Parse(reader.GetString(5));
            var completedAt = reader.IsDBNull(6) ? (DateTime?)null : DateTime.Parse(reader.GetString(6));
            var result = (JournalResult)reader.GetInt32(7);

            object[]? titleArgs = null;
            if (titleArgsJson is not null)
            {
                var arr = JsonSerializer.Deserialize<string[]>(titleArgsJson);
                if (arr is not null) titleArgs = arr;
            }

            entries.Add(new JournalEntry(eventKind, titleArgs, detail, repoName, startedAt, completedAt, result, id));
        }

        return entries;
    }

    public void CompleteStaleRunning()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE entries SET result = $cancelled, completed_at = started_at WHERE result = 0";
        cmd.Parameters.AddWithValue("$cancelled", (int)JournalResult.Cancelled);
        cmd.ExecuteNonQuery();
    }

    public void DeleteFinished()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE result != 0";
        cmd.ExecuteNonQuery();
    }

    public void DeleteOlderThan(DateTime cutoff)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE started_at < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void Trim(int maxEntries = 200)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE id NOT IN (SELECT id FROM entries ORDER BY id DESC LIMIT $limit)";
        cmd.Parameters.AddWithValue("$limit", maxEntries);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
