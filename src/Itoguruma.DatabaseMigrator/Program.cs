using Microsoft.Data.Sqlite;

if (!TryGetOption(args, "--destination", out var destination) ||
    !TryGetOption(args, "--source", out var source))
{
    Console.Error.WriteLine("Usage: itoguruma-database-migrator --destination <path> --source <path> [--backup-directory <path>]");
    return 2;
}

if (!File.Exists(source))
{
    Console.WriteLine($"Source database does not exist; migration skipped: {source}");
    return 0;
}

destination = Path.GetFullPath(destination);
source = Path.GetFullPath(source);
if (string.Equals(destination, source, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Source and destination are identical; migration skipped.");
    return 0;
}

var backupDirectory = TryGetOption(args, "--backup-directory", out var configuredBackupDirectory)
    ? Path.GetFullPath(configuredBackupDirectory)
    : Path.Combine(Path.GetDirectoryName(destination)!, "migration-backups");
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
Directory.CreateDirectory(backupDirectory);

var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
Backup(source, Path.Combine(backupDirectory, $"source-{suffix}.db"));
if (!File.Exists(destination))
{
    File.Copy(source, destination, overwrite: false);
    Console.WriteLine($"Database copied to production location: {destination}");
    return 0;
}

Backup(destination, Path.Combine(backupDirectory, $"destination-{suffix}.db"));
using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = destination,
    Mode = SqliteOpenMode.ReadWrite
}.ToString());
connection.Open();

using (var attach = connection.CreateCommand())
{
    attach.CommandText = "ATTACH DATABASE $source AS legacy;";
    attach.Parameters.AddWithValue("$source", source);
    attach.ExecuteNonQuery();
}

using var transaction = connection.BeginTransaction();
Execute(connection, transaction, "PRAGMA foreign_keys = OFF;");
Execute(connection, transaction, """
    INSERT INTO agents (agent_id, name, agent_type, session_id, created_at, last_seen_at, metadata_json)
    SELECT agent_id, name, agent_type, session_id, created_at, last_seen_at, metadata_json FROM legacy.agents WHERE true
    ON CONFLICT(agent_id) DO UPDATE SET
      name = CASE WHEN excluded.last_seen_at >= agents.last_seen_at THEN excluded.name ELSE agents.name END,
      agent_type = CASE WHEN excluded.last_seen_at >= agents.last_seen_at THEN excluded.agent_type ELSE agents.agent_type END,
      session_id = CASE WHEN excluded.last_seen_at >= agents.last_seen_at THEN excluded.session_id ELSE agents.session_id END,
      created_at = MIN(agents.created_at, excluded.created_at),
      last_seen_at = MAX(agents.last_seen_at, excluded.last_seen_at),
      metadata_json = CASE WHEN excluded.last_seen_at >= agents.last_seen_at THEN excluded.metadata_json ELSE agents.metadata_json END;
    """);

var providerExpression = HasColumn(connection, transaction, "legacy", "messages", "provider") ? "provider" : "NULL";
Execute(connection, transaction, $"""
    INSERT OR IGNORE INTO messages
      (message_id, thread_id, sender_agent_id, reply_to_message_id, message_type, body, payload_json, created_at, idempotency_key, provider)
    SELECT message_id, thread_id, sender_agent_id, reply_to_message_id, message_type, body, payload_json, created_at, idempotency_key, {providerExpression}
    FROM legacy.messages;
    """);
Execute(connection, transaction, """
    INSERT INTO message_deliveries
      (message_id, recipient_agent_id, status, lease_until, delivered_at, acked_at)
    SELECT message_id, recipient_agent_id, status, lease_until, delivered_at, acked_at FROM legacy.message_deliveries WHERE true
    ON CONFLICT(message_id, recipient_agent_id) DO UPDATE SET
      status = CASE
        WHEN excluded.status = 'acked' OR message_deliveries.status = 'acked' THEN 'acked'
        WHEN excluded.status = 'leased' OR message_deliveries.status = 'leased' THEN 'leased'
        ELSE 'pending' END,
      lease_until = COALESCE(excluded.lease_until, message_deliveries.lease_until),
      delivered_at = COALESCE(excluded.delivered_at, message_deliveries.delivered_at),
      acked_at = COALESCE(excluded.acked_at, message_deliveries.acked_at);
    """);
transaction.Commit();
Console.WriteLine($"Database merged into production location: {destination}");
return 0;

static bool TryGetOption(string[] arguments, string name, out string value)
{
    var index = Array.FindIndex(arguments, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
    if (index >= 0 && index + 1 < arguments.Length && !string.IsNullOrWhiteSpace(arguments[index + 1]))
    {
        value = arguments[index + 1];
        return true;
    }

    value = string.Empty;
    return false;
}

static void Backup(string databasePath, string backupPath)
{
    using var sourceConnection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
    using var backupConnection = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate");
    sourceConnection.Open();
    backupConnection.Open();
    sourceConnection.BackupDatabase(backupConnection);
}

static bool HasColumn(SqliteConnection connection, SqliteTransaction transaction, string schema, string table, string column)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = $"PRAGMA {schema}.table_info({table});";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.ExecuteNonQuery();
}
