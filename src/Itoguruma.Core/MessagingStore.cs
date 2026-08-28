using System.Data;
using Microsoft.Data.Sqlite;

namespace Itoguruma.Core;

public sealed class SqliteMessageStore(string databasePath, TimeProvider? timeProvider = null) : IMessageStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenAsync(cancellationToken);
        await DatabaseMigrator.MigrateAsync(connection, cancellationToken);
    }

    private static class DatabaseMigrator
    {
        public static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = (SqliteTransaction)transaction;
            versionCommand.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (version < 1) await ApplyAsync(connection, (SqliteTransaction)transaction, """
            CREATE TABLE IF NOT EXISTS agents (
              agent_id TEXT PRIMARY KEY, name TEXT NOT NULL, agent_type TEXT NOT NULL,
              session_id TEXT NULL, created_at TEXT NOT NULL, last_seen_at TEXT NOT NULL,
              metadata_json TEXT NULL);
            CREATE TABLE IF NOT EXISTS messages (
              message_id TEXT PRIMARY KEY, thread_id TEXT NOT NULL,
              sender_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
              reply_to_message_id TEXT NULL REFERENCES messages(message_id),
              message_type TEXT NOT NULL CHECK(message_type IN ('message','notification','system','change_request')),
              body TEXT NOT NULL, payload_json TEXT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS message_deliveries (
              message_id TEXT NOT NULL REFERENCES messages(message_id),
              recipient_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
              status TEXT NOT NULL CHECK(status IN ('pending','leased','acked')),
              lease_until TEXT NULL, delivered_at TEXT NULL, acked_at TEXT NULL,
              PRIMARY KEY(message_id, recipient_agent_id));
            CREATE INDEX IF NOT EXISTS ix_deliveries_inbox
              ON message_deliveries(recipient_agent_id, status, lease_until);
            CREATE INDEX IF NOT EXISTS ix_messages_thread ON messages(thread_id, created_at);
            PRAGMA user_version=1;
            """, cancellationToken);
            if (version < 2) await ApplyAsync(connection, (SqliteTransaction)transaction, """
            ALTER TABLE messages ADD COLUMN idempotency_key TEXT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_sender_idempotency
              ON messages(sender_agent_id, idempotency_key) WHERE idempotency_key IS NOT NULL;
            PRAGMA user_version=2;
            """, cancellationToken);
            if (version < 3) await ApplyAsync(connection, (SqliteTransaction)transaction, """
            CREATE TABLE messages_v3 (
              message_id TEXT PRIMARY KEY, thread_id TEXT NOT NULL,
              sender_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
              reply_to_message_id TEXT NULL REFERENCES messages_v3(message_id),
              message_type TEXT NOT NULL CHECK(message_type IN ('message','notification','system','change_request')),
              body TEXT NOT NULL, payload_json TEXT NULL, created_at TEXT NOT NULL,
              idempotency_key TEXT NULL);
            INSERT INTO messages_v3 SELECT message_id,thread_id,sender_agent_id,reply_to_message_id,
              message_type,body,payload_json,created_at,idempotency_key FROM messages;
            CREATE TABLE message_deliveries_v3 (
              message_id TEXT NOT NULL REFERENCES messages_v3(message_id),
              recipient_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
              status TEXT NOT NULL CHECK(status IN ('pending','leased','acked')),
              lease_until TEXT NULL, delivered_at TEXT NULL, acked_at TEXT NULL,
              PRIMARY KEY(message_id, recipient_agent_id));
            INSERT INTO message_deliveries_v3 SELECT message_id,recipient_agent_id,status,
              lease_until,delivered_at,acked_at FROM message_deliveries;
            DROP TABLE message_deliveries;
            DROP TABLE messages;
            ALTER TABLE messages_v3 RENAME TO messages;
            ALTER TABLE message_deliveries_v3 RENAME TO message_deliveries;
            CREATE INDEX ix_deliveries_inbox ON message_deliveries(recipient_agent_id,status,lease_until);
            CREATE INDEX ix_messages_thread ON messages(thread_id,created_at);
            CREATE UNIQUE INDEX ux_messages_sender_idempotency
              ON messages(sender_agent_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
            PRAGMA user_version=3;
            """, cancellationToken);
            if (version < 4) await ApplyAsync(connection, (SqliteTransaction)transaction, """
            ALTER TABLE messages ADD COLUMN provider TEXT NOT NULL DEFAULT 'unknown';
            PRAGMA user_version=4;
            """, cancellationToken);
            if (version < 5) await ApplyAsync(connection, (SqliteTransaction)transaction, """
            CREATE TABLE projects (
              project_id TEXT PRIMARY KEY, display_name TEXT NOT NULL,
              inbox_agent_id TEXT NOT NULL UNIQUE, enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE audit_log (
              audit_id INTEGER PRIMARY KEY AUTOINCREMENT, event_type TEXT NOT NULL,
              subject_id TEXT NOT NULL, created_at TEXT NOT NULL);
            PRAGMA user_version=5;
            """, cancellationToken);
            if (version < 6)
            {
                await EnsureNoProjectCaseConflictsAsync(connection, (SqliteTransaction)transaction, cancellationToken);
                await ApplyAsync(connection, (SqliteTransaction)transaction, """
                CREATE UNIQUE INDEX ux_projects_id_nocase
                  ON projects(project_id COLLATE ITOGURUMA_NOCASE);
                PRAGMA user_version=6;
                """, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task EnsureNoProjectCaseConflictsAsync(SqliteConnection connection,
            SqliteTransaction transaction, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                SELECT project_id FROM projects
                GROUP BY project_id COLLATE ITOGURUMA_NOCASE HAVING COUNT(*) > 1 LIMIT 1;
                """;
            if (await command.ExecuteScalarAsync(cancellationToken) is string projectId)
                throw new ProjectOperationException(ProjectErrorCodes.ProjectCaseConflict,
                    $"Project IDs differ only by case: {projectId}");
        }

        private static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction,
            string sql, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = sql; await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<Agent> RegisterAgentAsync(string agentId, string agentType, string? name = null,
        string? sessionId = null, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        RequireText(agentId, nameof(agentId)); RequireText(agentType, nameof(agentType));
        var now = Now();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agents(agent_id,name,agent_type,session_id,created_at,last_seen_at,metadata_json)
            VALUES($id,$name,$type,$session,$now,$now,$metadata)
            ON CONFLICT(agent_id) DO UPDATE SET name=$name, agent_type=$type,
              session_id=$session, last_seen_at=$now, metadata_json=$metadata;
            """;
        command.Parameters.AddWithValue("$id", agentId);
        command.Parameters.AddWithValue("$name", name ?? agentId);
        command.Parameters.AddWithValue("$type", agentType);
        command.Parameters.AddWithValue("$session", (object?)sessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(now));
        command.Parameters.AddWithValue("$metadata", (object?)metadataJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new Agent(agentId, name ?? agentId, agentType, sessionId, now, now, metadataJson);
    }

    public async Task<IReadOnlyList<Agent>> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        var agents = new List<Agent>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT agent_id,name,agent_type,session_id,created_at,last_seen_at,metadata_json FROM agents ORDER BY agent_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            agents.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), Parse(reader.GetString(4)),
                Parse(reader.GetString(5)), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return agents;
    }

    public async Task<bool> UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        RequireText(agentId, nameof(agentId));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM agents WHERE agent_id=$id;";
        Add(command, "$id", agentId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<string> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        RequireText(request.SenderAgentId, nameof(request.SenderAgentId));
        RequireText(request.Body, nameof(request.Body)); RequireText(request.ThreadId, nameof(request.ThreadId));
        var provider = NormalizeProvider(request.Provider);
        if (request.Recipients.Count == 0) throw new ArgumentException("At least one recipient is required.", nameof(request));
        var messageId = Guid.NewGuid().ToString("N");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (request.IdempotencyKey is not null)
            {
                await using var existing = connection.CreateCommand();
                existing.Transaction = (SqliteTransaction)transaction;
                existing.CommandText = "SELECT message_id FROM messages WHERE sender_agent_id=$sender AND idempotency_key=$key";
                Add(existing, "$sender", request.SenderAgentId); Add(existing, "$key", request.IdempotencyKey);
                if (await existing.ExecuteScalarAsync(cancellationToken) is string existingId)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return existingId;
                }
            }
            await using (var message = connection.CreateCommand())
            {
                message.Transaction = (SqliteTransaction)transaction;
                message.CommandText = """
                    INSERT OR IGNORE INTO messages(message_id,thread_id,sender_agent_id,provider,reply_to_message_id,message_type,body,payload_json,created_at,idempotency_key)
                    VALUES($id,$thread,$sender,$provider,$reply,$type,$body,$payload,$created,$key)
                    """;
                Add(message, "$id", messageId); Add(message, "$thread", request.ThreadId);
                Add(message, "$sender", request.SenderAgentId); Add(message, "$provider", provider);
                Add(message, "$reply", request.ReplyToMessageId);
                Add(message, "$type", request.MessageType); Add(message, "$body", request.Body);
                Add(message, "$payload", request.PayloadJson); Add(message, "$created", Format(Now()));
                Add(message, "$key", request.IdempotencyKey);
                var inserted = await message.ExecuteNonQueryAsync(cancellationToken);
                if (inserted == 0 && request.IdempotencyKey is not null)
                {
                    await using var existing = connection.CreateCommand();
                    existing.Transaction = (SqliteTransaction)transaction;
                    existing.CommandText = "SELECT message_id FROM messages WHERE sender_agent_id=$sender AND idempotency_key=$key";
                    Add(existing, "$sender", request.SenderAgentId); Add(existing, "$key", request.IdempotencyKey);
                    var existingId = (string?)await existing.ExecuteScalarAsync(cancellationToken)
                        ?? throw new InvalidOperationException("Idempotent message could not be resolved.");
                    await transaction.CommitAsync(cancellationToken);
                    return existingId;
                }
            }
            foreach (var recipient in request.Recipients.Distinct(StringComparer.Ordinal))
            {
                RequireText(recipient, nameof(request.Recipients));
                var resolvedRecipient = await ResolveRecipientAsync(
                    connection, (SqliteTransaction)transaction, recipient, cancellationToken);
                await using var delivery = connection.CreateCommand();
                delivery.Transaction = (SqliteTransaction)transaction;
                delivery.CommandText = "INSERT INTO message_deliveries(message_id,recipient_agent_id,status) VALUES($id,$recipient,'pending')";
                Add(delivery, "$id", messageId); Add(delivery, "$recipient", resolvedRecipient);
                await delivery.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return messageId;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(string agentId, int limit = 50,
        TimeSpan? leaseDuration = null, string? threadId = null, string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        RequireText(agentId, nameof(agentId));
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = Now(); var leaseUntil = now + (leaseDuration ?? TimeSpan.FromMinutes(5));
        var messages = new List<Message>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE message_deliveries SET status='leased', lease_until=$lease, delivered_at=COALESCE(delivered_at,$now)
            WHERE rowid IN (SELECT d.rowid FROM message_deliveries d JOIN messages m ON m.message_id=d.message_id
              WHERE d.recipient_agent_id=$agent AND (d.status='pending' OR (d.status='leased' AND d.lease_until <= $now))
              AND ($thread IS NULL OR m.thread_id=$thread)
              AND ($type IS NULL OR m.message_type=$type) ORDER BY m.created_at LIMIT $limit)
            RETURNING message_id;
            """;
        Add(command, "$lease", Format(leaseUntil)); Add(command, "$now", Format(now));
        Add(command, "$agent", agentId); Add(command, "$thread", threadId); Add(command, "$limit", limit);
        Add(command, "$type", messageType);
        var ids = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetString(0));
        foreach (var id in ids)
        {
            await using var select = connection.CreateCommand(); select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = """
                SELECT m.message_id,m.thread_id,m.sender_agent_id,m.provider,m.reply_to_message_id,m.message_type,
                  m.body,m.payload_json,m.created_at,d.status,d.lease_until FROM messages m
                JOIN message_deliveries d ON d.message_id=m.message_id
                WHERE m.message_id=$id AND d.recipient_agent_id=$agent;
                """;
            Add(select, "$id", id); Add(select, "$agent", agentId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) messages.Add(ReadMessage(reader));
        }
        await transaction.CommitAsync(cancellationToken);
        return messages.OrderBy(x => x.CreatedAt).ToArray();
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetConversationHistoryAsync(string threadId, int limit = 100,
        int offset = 0, CancellationToken cancellationToken = default)
    {
        RequireText(threadId, nameof(threadId));
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var history = new List<ConversationMessage>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT message_id,thread_id,sender_agent_id,provider,reply_to_message_id,message_type,body,payload_json,created_at
            FROM messages WHERE thread_id=$thread ORDER BY created_at ASC LIMIT $limit OFFSET $offset;
            """;
        Add(command, "$thread", threadId); Add(command, "$limit", limit); Add(command, "$offset", offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            history.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), Parse(reader.GetString(8))));
        return history;
    }

    public async Task<bool> AckMessageAsync(string agentId, string messageId, CancellationToken cancellationToken = default)
    {
        var now = Format(Now());
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE message_deliveries SET status='acked',acked_at=$now,lease_until=NULL
            WHERE message_id=$message AND recipient_agent_id=$agent AND status='leased';
            """;
        Add(command, "$now", now); Add(command, "$message", messageId); Add(command, "$agent", agentId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<Project> AddProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default)
    {
        ValidateMutation(mutation, true);
        var now = Format(Now());
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO projects(project_id,display_name,inbox_agent_id,enabled,created_at,updated_at)
            VALUES($id,$name,$inbox,1,$now,$now);
            """;
        Add(command, "$id", mutation.ProjectId); Add(command, "$name", mutation.DisplayName ?? mutation.ProjectId);
        Add(command, "$inbox", mutation.InboxAgentId); Add(command, "$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(connection, (SqliteTransaction)transaction, "project_added", mutation.ProjectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetProjectAsync(mutation.ProjectId, cancellationToken))!;
    }

    public async Task<Project> UpdateProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default)
    {
        ValidateMutation(mutation, false);
        var now = Format(Now());
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE projects SET display_name=COALESCE($name,display_name),
              inbox_agent_id=COALESCE($inbox,inbox_agent_id),updated_at=$now
              WHERE project_id=$id COLLATE ITOGURUMA_NOCASE;
            """;
        Add(command, "$id", mutation.ProjectId); Add(command, "$name", mutation.DisplayName);
        Add(command, "$inbox", mutation.InboxAgentId); Add(command, "$now", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new ProjectOperationException(ProjectErrorCodes.UnknownProject, "Unknown project.");
        await AuditAsync(connection, (SqliteTransaction)transaction, "project_updated", mutation.ProjectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetProjectAsync(mutation.ProjectId, cancellationToken))!;
    }

    public async Task<Project> SetProjectEnabledAsync(string projectId, bool enabled, CancellationToken cancellationToken = default)
    {
        RequireText(projectId, nameof(projectId)); var now = Format(Now());
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE projects SET enabled=$enabled,updated_at=$now
            WHERE project_id=$id COLLATE ITOGURUMA_NOCASE;
            """;
        Add(command, "$enabled", enabled ? 1 : 0); Add(command, "$now", now); Add(command, "$id", projectId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new ProjectOperationException(ProjectErrorCodes.UnknownProject, "Unknown project.");
        await AuditAsync(connection, (SqliteTransaction)transaction, enabled ? "project_enabled" : "project_disabled", projectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetProjectAsync(projectId, cancellationToken))!;
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        RequireText(projectId, nameof(projectId)); var now = Format(Now());
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var referenced = connection.CreateCommand(); referenced.Transaction = (SqliteTransaction)transaction;
        referenced.CommandText = """
            SELECT EXISTS(SELECT 1 FROM projects p JOIN agents a ON a.agent_id=p.inbox_agent_id
              JOIN message_deliveries d ON d.recipient_agent_id=a.agent_id
              WHERE p.project_id=$id COLLATE ITOGURUMA_NOCASE);
            """;
        Add(referenced, "$id", projectId);
        if (Convert.ToInt32(await referenced.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 0)
            throw new ProjectOperationException(ProjectErrorCodes.ProjectReferenced, "Project is referenced by delivery history; disable it instead.");
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM projects WHERE project_id=$id COLLATE ITOGURUMA_NOCASE";
        Add(command, "$id", projectId);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (deleted) await AuditAsync(connection, (SqliteTransaction)transaction, "project_deleted", projectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return deleted;
    }

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Project>(); await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT project_id,display_name,inbox_agent_id,enabled,created_at,updated_at FROM projects ORDER BY project_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadProject(reader)); return result;
    }

    public async Task<Project?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        RequireText(projectId, nameof(projectId)); await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = """
            SELECT project_id,display_name,inbox_agent_id,enabled,created_at,updated_at
            FROM projects WHERE project_id=$id COLLATE ITOGURUMA_NOCASE;
            """;
        Add(command, "$id", projectId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    private async Task<string> ResolveRecipientAsync(SqliteConnection connection, SqliteTransaction transaction,
        string recipient, CancellationToken cancellationToken)
    {
        await using var agent = connection.CreateCommand(); agent.Transaction = transaction;
        agent.CommandText = "SELECT agent_id FROM agents WHERE agent_id=$id"; Add(agent, "$id", recipient);
        if (await agent.ExecuteScalarAsync(cancellationToken) is string existing) return existing;
        await using var project = connection.CreateCommand(); project.Transaction = transaction;
        project.CommandText = """
            SELECT inbox_agent_id,enabled FROM projects
            WHERE project_id=$id COLLATE ITOGURUMA_NOCASE;
            """;
        Add(project, "$id", recipient);
        await using var reader = await project.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            var now = Format(Now());
            await using var addProject = connection.CreateCommand(); addProject.Transaction = transaction;
            addProject.CommandText = """
                INSERT INTO projects(project_id,display_name,inbox_agent_id,enabled,created_at,updated_at)
                VALUES($id,$id,$id,1,$now,$now);
                """;
            Add(addProject, "$id", recipient); Add(addProject, "$now", now);
            await addProject.ExecuteNonQueryAsync(cancellationToken);
            await AuditAsync(connection, transaction, "project_auto_registered", recipient, now, cancellationToken);
            return await RegisterProjectInboxAsync(connection, transaction, recipient, recipient, cancellationToken);
        }
        if (reader.GetInt32(1) == 0)
            throw new ProjectOperationException(ProjectErrorCodes.DisabledProject, "Project recipient is disabled.");
        var inbox = reader.GetString(0); await reader.DisposeAsync();
        return await RegisterProjectInboxAsync(connection, transaction, recipient, inbox, cancellationToken);
    }

    private async Task<string> RegisterProjectInboxAsync(SqliteConnection connection, SqliteTransaction transaction,
        string projectId, string inbox, CancellationToken cancellationToken)
    {
        await using var register = connection.CreateCommand(); register.Transaction = transaction;
        register.CommandText = """
            INSERT OR IGNORE INTO agents(agent_id,name,agent_type,session_id,created_at,last_seen_at,metadata_json)
            VALUES($id,$id,'project_inbox',NULL,$now,$now,NULL);
            """;
        Add(register, "$id", inbox); Add(register, "$now", Format(Now()));
        if (await register.ExecuteNonQueryAsync(cancellationToken) == 1)
            await AuditAsync(connection, transaction, "project_inbox_registered", projectId, Format(Now()), cancellationToken);
        return inbox;
    }

    private static async Task AuditAsync(SqliteConnection connection, SqliteTransaction transaction,
        string eventType, string subjectId, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO audit_log(event_type,subject_id,created_at) VALUES($event,$subject,$now)";
        Add(command, "$event", eventType); Add(command, "$subject", subjectId); Add(command, "$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateMutation(ProjectMutation mutation, bool requireInbox)
    {
        RequireText(mutation.ProjectId, nameof(mutation.ProjectId));
        if (requireInbox) RequireText(mutation.InboxAgentId ?? string.Empty, nameof(mutation.InboxAgentId));
        if (mutation.DisplayName is not null) RequireText(mutation.DisplayName, nameof(mutation.DisplayName));
        if (mutation.InboxAgentId is not null) RequireText(mutation.InboxAgentId, nameof(mutation.InboxAgentId));
    }

    private static Project ReadProject(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1),
        reader.GetString(2), reader.GetInt32(3) != 0, Parse(reader.GetString(4)), Parse(reader.GetString(5)));

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.CreateCollation("ITOGURUMA_NOCASE",
            (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken); return connection;
    }
    private DateTimeOffset Now() => _timeProvider.GetUtcNow();
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static void RequireText(string value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name); }
    private static Message ReadMessage(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2),
        r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), Parse(r.GetString(8)), r.GetString(9),
        r.IsDBNull(10) ? null : Parse(r.GetString(10)));

    private static string NormalizeProvider(string provider) => NormalizeProviderOrNull(provider)
        ?? throw new ProviderValidationException(
            "Provider must contain only ASCII letters, digits, or hyphens and must not be unknown.",
            nameof(provider));

    private static string? NormalizeProviderOrNull(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        return normalized.Length > 0
            && !string.Equals(normalized, "unknown", StringComparison.Ordinal)
            && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? normalized
            : null;
    }
}

/// <summary>送信要求のProviderが無効であることを示します。</summary>
public sealed class ProviderValidationException(string message, string parameterName)
    : ArgumentException(message, parameterName);
