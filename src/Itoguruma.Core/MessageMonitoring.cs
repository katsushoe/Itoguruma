using Microsoft.Data.Sqlite;

namespace Itoguruma.Core;

public sealed record MessageMonitorQuery(
    string? Status = null,
    string? AgentId = null,
    string? SearchText = null,
    string? MessageType = null,
    int Limit = 500);

public sealed record MonitoredMessage(
    string MessageId,
    string ThreadId,
    string SenderAgentId,
    string Provider,
    string RecipientAgentId,
    string MessageType,
    string Body,
    string? PayloadJson,
    string? ReplyToMessageId,
    string? IdempotencyKey,
    DateTimeOffset CreatedAt,
    string DeliveryStatus,
    DateTimeOffset? LeaseUntil,
    string? LeaseOwnerAgentId,
    int DeliveryAttemptCount,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? AcknowledgedAt);

public sealed record MessageMonitorSnapshot(
    IReadOnlyList<MonitoredMessage> Messages,
    IReadOnlyList<string> AgentIds,
    int PendingCount,
    int LeasedCount,
    int AcknowledgedCount,
    DateTimeOffset LoadedAt);

public interface IMessageMonitor
{
    Task<MessageMonitorSnapshot> LoadAsync(
        MessageMonitorQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class SqliteMessageMonitor(string databasePath, TimeProvider? timeProvider = null) : IMessageMonitor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MessageMonitorSnapshot> LoadAsync(
        MessageMonitorQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(query));
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Itoguruma database was not found.", databasePath);

        var messages = new List<MonitoredMessage>();
        var agents = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await ExecutePragmaAsync(connection, cancellationToken);

        await using (var agentCommand = connection.CreateCommand())
        {
            agentCommand.CommandText = "SELECT agent_id FROM agents ORDER BY agent_id";
            await using var reader = await agentCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) agents.Add(reader.GetString(0));
        }

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT status, COUNT(*) FROM message_deliveries GROUP BY status";
            await using var reader = await countCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT m.message_id, m.thread_id, m.sender_agent_id, m.provider, d.recipient_agent_id,
                       m.message_type, m.body, m.payload_json, m.reply_to_message_id,
                       m.idempotency_key, m.created_at, d.status, d.lease_until,
                       d.lease_owner_agent_id, d.delivery_attempt_count, d.delivered_at, d.acked_at
                FROM messages m
                JOIN message_deliveries d ON d.message_id = m.message_id
                WHERE ($status IS NULL OR d.status = $status)
                  AND ($type IS NULL OR m.message_type = $type)
                  AND ($agent IS NULL OR m.sender_agent_id = $agent OR d.recipient_agent_id = $agent)
                  AND ($search IS NULL OR m.body LIKE $pattern OR m.thread_id LIKE $pattern
                       OR m.message_id LIKE $pattern)
                ORDER BY m.created_at DESC, d.recipient_agent_id
                LIMIT $limit;
                """;
            Add(command, "$status", EmptyToNull(query.Status));
            Add(command, "$type", EmptyToNull(query.MessageType));
            Add(command, "$agent", EmptyToNull(query.AgentId));
            var search = EmptyToNull(query.SearchText);
            Add(command, "$search", search);
            Add(command, "$pattern", search is null ? null : $"%{search}%");
            Add(command, "$limit", query.Limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableString(reader, 7),
                    NullableString(reader, 8), NullableString(reader, 9), Parse(reader.GetString(10)),
                    reader.GetString(11), NullableDate(reader, 12), NullableString(reader, 13),
                    reader.GetInt32(14), NullableDate(reader, 15), NullableDate(reader, 16)));
            }
        }

        return new(messages, agents, Count(counts, "pending"), Count(counts, "leased"),
            Count(counts, "acked"), _timeProvider.GetUtcNow());
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));
    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static int Count(IReadOnlyDictionary<string, int> counts, string status) =>
        counts.TryGetValue(status, out var value) ? value : 0;
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
