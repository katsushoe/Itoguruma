using Itoguruma.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Itoguruma.Tests;

public sealed class MessagingStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itoguruma-tests", Guid.NewGuid().ToString("N"));
    private SqliteMessageStore CreateStore() => new(Path.Combine(_directory,"messages.db"));

    [Fact]
    public async Task MessagingOperations_WhenExecuted_WriteDiagnosticAuditLogs()
    {
        var logger = new TestLogger<SqliteMessageStore>();
        var store = new SqliteMessageStore(Path.Combine(_directory, "audit.db"), logger: logger);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.RegisterAgentAsync("recipient", "project_inbox");
        await store.RegisterAgentAsync("other", "project_inbox");

        var messageId = await store.SendMessageAsync(new(
            "sender", ["recipient"], "secret body", "audit-thread", "codex", IdempotencyKey: "audit-key"));
        var replayedMessageId = await store.SendMessageAsync(new(
            "sender", ["other"], "different secret", "audit-thread", "codex", IdempotencyKey: "audit-key"));
        var messages = await store.GetMessagesAsync("recipient");
        var acknowledged = await store.AckMessageAsync("recipient", messageId);

        Assert.Equal(messageId, replayedMessageId);
        Assert.Single(messages);
        Assert.True(acknowledged);
        Assert.Contains(logger.Messages, message => message.Contains("[Messaging][Send]", StringComparison.Ordinal)
            && message.Contains(messageId, StringComparison.Ordinal)
            && message.Contains("recipient", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("[Messaging][Lease]", StringComparison.Ordinal)
            && message.Contains(messageId, StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("[Messaging][Ack]", StringComparison.Ordinal)
            && message.Contains(messageId, StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("[Messaging][Send][IdempotentReplay]", StringComparison.Ordinal)
            && message.Contains("persisted recipients recipient", StringComparison.Ordinal)
            && message.Contains("requested projects other", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret body", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("different secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Message_WhenLeasedAndAcked_IsNotDeliveredAgain()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("sender","test"); await store.RegisterAgentAsync("recipient","test");
        var id=await store.SendMessageAsync(new("sender",["recipient"],"hello","thread-1","codex"));
        var first=await store.GetMessagesAsync("recipient");
        Assert.Single(first); Assert.Equal(id,first[0].MessageId); Assert.True(await store.AckMessageAsync("recipient",id));
        Assert.Empty(await store.GetMessagesAsync("recipient"));
    }

    [Fact]
    public async Task Message_WhenProviderIsProvided_StoresNormalizedProviderAcrossDeliveryAndHistory()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await new MessagingService(store).RegisterAgentAsync("sender", " CoDeX ");
        await new MessagingService(store).RegisterAgentAsync("recipient", "claude-code");

        await store.SendMessageAsync(new("sender", ["recipient"], "hello", "provider-thread", " CoDeX "));
        var first = Assert.Single(await store.GetMessagesAsync("recipient", leaseDuration: TimeSpan.FromMilliseconds(-1)));
        var redelivery = Assert.Single(await store.GetMessagesAsync("recipient"));
        var history = Assert.Single(await store.GetConversationHistoryAsync("provider-thread"));

        Assert.Equal("codex", first.Provider);
        Assert.Equal(first.Provider, redelivery.Provider);
        Assert.Equal(first.Provider, history.Provider);
    }

    [Fact]
    public async Task SendMessage_WhenProviderIsInvalid_DoesNotPersistMessage()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.RegisterAgentAsync("sender", "codex");
        await store.RegisterAgentAsync("recipient", "codex");
        await Assert.ThrowsAsync<ProviderValidationException>(() =>
            store.SendMessageAsync(new("sender", ["recipient"], "blocked", "provider-thread", "unknown")));
        Assert.Empty(await store.GetConversationHistoryAsync("provider-thread"));
    }

    [Fact]
    public async Task Message_WhenLeaseExpires_IsDeliveredAgain()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        await store.SendMessageAsync(new("a",["b"],"hello","t","codex"));
        Assert.Single(await store.GetMessagesAsync("b",leaseDuration:TimeSpan.FromMilliseconds(-1)));
        Assert.Single(await store.GetMessagesAsync("b"));
    }

    [Fact]
    public async Task SendMessage_WhenRecipientDoesNotExist_AutoRegistersProjectAndDelivers()
    {
        var store=CreateStore(); await store.InitializeAsync(); await store.RegisterAgentAsync("a","test");
        await store.SendMessageAsync(new("a",["missing"],"hello","t","codex"));

        var project = Assert.Single(await store.ListProjectsAsync());
        Assert.Equal("missing", project.ProjectId);
        Assert.Equal("missing", project.InboxAgentId);
        Assert.True(project.Enabled);
        Assert.Equal("hello", Assert.Single(await store.GetMessagesAsync("missing")).Body);
    }

    [Fact]
    public async Task SendMessage_WhenIdempotencyKeyIsRepeated_ReturnsOriginalMessage()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var request=new SendMessageRequest("a",["b"],"hello","t","codex",IdempotencyKey:"request-1");
        var first=await store.SendMessageAsync(request); var second=await store.SendMessageAsync(request);
        Assert.Equal(first,second); Assert.Single(await store.GetMessagesAsync("b"));
    }

    [Fact]
    public async Task Message_WhenStoreIsRecreated_RemainsAvailable()
    {
        var firstStore=CreateStore(); await firstStore.InitializeAsync();
        await firstStore.RegisterAgentAsync("claude","test"); await firstStore.RegisterAgentAsync("codex","test");
        var id=await firstStore.SendMessageAsync(new("claude",["codex"],"persist","restart","claude-code"));

        var restartedStore=CreateStore(); await restartedStore.InitializeAsync();
        var received=await restartedStore.GetMessagesAsync("codex");

        Assert.Single(received); Assert.Equal(id,received[0].MessageId);
    }

    [Fact]
    public async Task Messages_WhenSentInBothDirections_CanBeAcked()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("claude","test"); await store.RegisterAgentAsync("codex","test");
        var outbound=await store.SendMessageAsync(new("claude",["codex"],"request","roundtrip","claude-code"));
        var atCodex=Assert.Single(await store.GetMessagesAsync("codex"));
        Assert.True(await store.AckMessageAsync("codex",atCodex.MessageId));
        var reply=await store.SendMessageAsync(new("codex",["claude"],"response","roundtrip","codex",outbound));
        var atClaude=Assert.Single(await store.GetMessagesAsync("claude"));
        Assert.Equal(reply,atClaude.MessageId); Assert.Equal(outbound,atClaude.ReplyToMessageId);
        Assert.True(await store.AckMessageAsync("claude",atClaude.MessageId));
    }

    [Fact]
    public async Task SendMessage_WhenConcurrentWithSameKey_CreatesOneDelivery()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var request=new SendMessageRequest("a",["b"],"once","concurrent","codex",IdempotencyKey:"same-key");

        var ids=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>store.SendMessageAsync(request)));

        Assert.Single(ids.Distinct(StringComparer.Ordinal));
        Assert.Single(await store.GetMessagesAsync("b",limit:50));
    }

    [Fact]
    public async Task GetConversationHistory_ReturnsMessagesInChronologicalOrder()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var first=await store.SendMessageAsync(new("a",["b"],"first","history-thread","codex"));
        var second=await store.SendMessageAsync(new("b",["a"],"second","history-thread","claude-code",first));

        var history=await store.GetConversationHistoryAsync("history-thread");

        Assert.Equal(2,history.Count);
        Assert.Equal(first,history[0].MessageId); Assert.Equal("first",history[0].Body);
        Assert.Equal(second,history[1].MessageId); Assert.Equal(first,history[1].ReplyToMessageId);
    }

    [Fact]
    public async Task GetConversationHistory_WhenThreadDoesNotExist_ReturnsEmpty()
    {
        var store=CreateStore(); await store.InitializeAsync();

        Assert.Empty(await store.GetConversationHistoryAsync("missing-thread"));
    }

    [Fact]
    public async Task GetConversationHistory_WithLimitAndOffset_Pages()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        for (var i=0;i<5;i++) await store.SendMessageAsync(new("a",["b"],$"m{i}","paged-thread","codex"));

        var firstPage=await store.GetConversationHistoryAsync("paged-thread",limit:2,offset:0);
        var secondPage=await store.GetConversationHistoryAsync("paged-thread",limit:2,offset:2);

        Assert.Equal(["m0","m1"],firstPage.Select(x=>x.Body));
        Assert.Equal(["m2","m3"],secondPage.Select(x=>x.Body));
    }

    [Fact]
    public async Task UnregisterAgent_WhenAgentHasNoMessages_RemovesIt()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("stale","test");

        Assert.True(await store.UnregisterAgentAsync("stale"));
        Assert.Empty(await store.ListAgentsAsync());
    }

    [Fact]
    public async Task UnregisterAgent_WhenAgentDoesNotExist_ReturnsFalse()
    {
        var store=CreateStore(); await store.InitializeAsync();

        Assert.False(await store.UnregisterAgentAsync("missing"));
    }

    [Fact]
    public async Task UnregisterAgent_WhenAgentIsReferencedByMessages_Throws()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        await store.SendMessageAsync(new("a",["b"],"hello","t","codex"));

        await Assert.ThrowsAnyAsync<Exception>(()=>store.UnregisterAgentAsync("a"));
        Assert.Contains(await store.ListAgentsAsync(), agent=>agent.AgentId=="a");
    }

    [Fact]
    public async Task DeleteAgentHistory_WhenDryRun_ReturnsCountsWithoutDeleting()
    {
        var store = CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("target", "test"); await store.RegisterAgentAsync("other", "test");
        var sent = await store.SendMessageAsync(new("target", ["other"], "sent", "related", "codex"));
        await store.SendMessageAsync(new("other", ["target"], "reply", "related", "codex", sent));
        await store.SendMessageAsync(new("other", ["target"], "inbound", "inbound", "codex"));

        var result = await store.DeleteAgentHistoryAsync("target", true);

        Assert.True(result.DryRun); Assert.Equal(2, result.MessageCount);
        Assert.Equal(3, result.DeliveryCount); Assert.Equal(2, result.ThreadCount);
        Assert.True(result.CanUnregister); Assert.NotEmpty(result.CorrelationId);
        Assert.Equal(2, (await store.GetConversationHistoryAsync("related")).Count);
        Assert.Equal(2, (await store.GetMessagesAsync("target")).Count);
    }

    [Fact]
    public async Task DeleteAgentHistory_WhenExecuted_DeletesOnlyExactAgentHistoryAndAllowsUnregister()
    {
        var store = CreateStore(); await store.InitializeAsync();
        foreach (var id in new[] { "target", "TARGET", "other" }) await store.RegisterAgentAsync(id, "test");
        var sent = await store.SendMessageAsync(new("target", ["other"], "sent", "related", "codex"));
        await store.SendMessageAsync(new("other", ["target"], "reply", "related", "codex", sent));
        await store.SendMessageAsync(new("other", ["target"], "inbound", "inbound", "codex"));
        await store.SendMessageAsync(new("TARGET", ["other"], "keep-case", "unrelated", "codex"));
        await store.SendMessageAsync(new("other", ["TARGET"], "keep-delivery", "unrelated", "codex"));

        var result = await store.DeleteAgentHistoryAsync("target", false);

        Assert.False(result.DryRun); Assert.Equal(2, result.MessageCount); Assert.Equal(4, result.DeliveryCount);
        Assert.Empty(await store.GetConversationHistoryAsync("related"));
        Assert.Single(await store.GetConversationHistoryAsync("inbound"));
        Assert.Equal(2, (await store.GetConversationHistoryAsync("unrelated")).Count);
        Assert.True(await store.UnregisterAgentAsync("target"));
        Assert.Contains(await store.ListAgentsAsync(), agent => agent.AgentId == "TARGET");
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "messages.db"), Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_id,correlation_id,message_count,delivery_count,thread_count " +
            "FROM audit_log WHERE event_type='agent_history_deleted'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal("target", reader.GetString(0));
        Assert.Equal(result.CorrelationId, reader.GetString(1)); Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(4, reader.GetInt32(3)); Assert.Equal(3, reader.GetInt32(4));
    }

    [Fact]
    public async Task DeleteAgentHistory_WhenAgentHasNoHistory_ReturnsZeroAndAllowsUnregister()
    {
        var store = CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("empty", "test");

        var result = await store.DeleteAgentHistoryAsync("empty", false);

        Assert.Equal(0, result.MessageCount); Assert.Equal(0, result.DeliveryCount);
        Assert.Equal(0, result.ThreadCount); Assert.True(await store.UnregisterAgentAsync("empty"));
    }

    [Fact]
    public async Task DeleteAgentHistory_WhenAgentDoesNotExist_ThrowsStructuredException()
    {
        var store = CreateStore(); await store.InitializeAsync();

        var exception = await Assert.ThrowsAsync<AgentHistoryOperationException>(() =>
            store.DeleteAgentHistoryAsync("missing", true));

        Assert.Equal(AgentHistoryErrorCodes.AgentNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAgentHistory_WhenDeleteFails_RollsBackTransaction()
    {
        var store = CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("target", "test"); await store.RegisterAgentAsync("other", "test");
        var messageId = await store.SendMessageAsync(new("target", ["other"], "sent", "rollback", "codex"));
        var databasePath = Path.Combine(_directory, "messages.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TRIGGER fail_history_delete BEFORE DELETE ON messages " +
                $"WHEN OLD.message_id='{messageId}' BEGIN SELECT RAISE(ABORT,'forced failure'); END;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => store.DeleteAgentHistoryAsync("target", false));

        Assert.Single(await store.GetConversationHistoryAsync("rollback"));
        Assert.Single(await store.GetMessagesAsync("other"));
        await Assert.ThrowsAsync<SqliteException>(() => store.UnregisterAgentAsync("target"));
    }

    [Fact]
    public async Task Initialize_WhenSchemaVersionIsTwo_MigratesAndAcceptsChangeRequest()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "messages.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE agents (
                  agent_id TEXT PRIMARY KEY, name TEXT NOT NULL, agent_type TEXT NOT NULL,
                  session_id TEXT NULL, created_at TEXT NOT NULL, last_seen_at TEXT NOT NULL,
                  metadata_json TEXT NULL);
                CREATE TABLE messages (
                  message_id TEXT PRIMARY KEY, thread_id TEXT NOT NULL,
                  sender_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
                  reply_to_message_id TEXT NULL REFERENCES messages(message_id),
                  message_type TEXT NOT NULL CHECK(message_type IN ('message','notification','system')),
                  body TEXT NOT NULL, payload_json TEXT NULL, created_at TEXT NOT NULL,
                  idempotency_key TEXT NULL);
                CREATE TABLE message_deliveries (
                  message_id TEXT NOT NULL REFERENCES messages(message_id),
                  recipient_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
                  status TEXT NOT NULL CHECK(status IN ('pending','leased','acked')),
                  lease_until TEXT NULL, delivered_at TEXT NULL, acked_at TEXT NULL,
                  PRIMARY KEY(message_id,recipient_agent_id));
                PRAGMA user_version=2;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var store = new SqliteMessageStore(databasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("a", "test");
        await store.RegisterAgentAsync("b", "test");

        await store.SendMessageAsync(new("a", ["b"], "cr", "t",
            Provider: "codex", MessageType: "change_request", PayloadJson: "{}"));

        var message = Assert.Single(await store.GetMessagesAsync("b"));
        Assert.Equal("change_request", message.MessageType);
        Assert.Equal("codex", message.Provider);
    }

    [Fact]
    public async Task Initialize_WhenSchemaVersionIsThree_MarksExistingMessagesWithUnknownProvider()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "messages.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE agents (
                  agent_id TEXT PRIMARY KEY, name TEXT NOT NULL, agent_type TEXT NOT NULL,
                  session_id TEXT NULL, created_at TEXT NOT NULL, last_seen_at TEXT NOT NULL,
                  metadata_json TEXT NULL);
                CREATE TABLE messages (
                  message_id TEXT PRIMARY KEY, thread_id TEXT NOT NULL,
                  sender_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
                  reply_to_message_id TEXT NULL REFERENCES messages(message_id),
                  message_type TEXT NOT NULL CHECK(message_type IN ('message','notification','system','change_request')),
                  body TEXT NOT NULL, payload_json TEXT NULL, created_at TEXT NOT NULL,
                  idempotency_key TEXT NULL);
                CREATE TABLE message_deliveries (
                  message_id TEXT NOT NULL REFERENCES messages(message_id),
                  recipient_agent_id TEXT NOT NULL REFERENCES agents(agent_id),
                  status TEXT NOT NULL CHECK(status IN ('pending','leased','acked')),
                  lease_until TEXT NULL, delivered_at TEXT NULL, acked_at TEXT NULL,
                  PRIMARY KEY(message_id,recipient_agent_id));
                INSERT INTO agents VALUES('sender','sender','Codex',NULL,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',NULL);
                INSERT INTO agents VALUES('recipient','recipient','codex',NULL,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',NULL);
                INSERT INTO messages VALUES('legacy','legacy-thread','sender',NULL,'message','legacy',NULL,'2026-01-01T00:00:00Z',NULL);
                INSERT INTO message_deliveries VALUES('legacy','recipient','pending',NULL,NULL,NULL);
                PRAGMA user_version=3;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteMessageStore(databasePath);
        await store.InitializeAsync();

        var message = Assert.Single(await store.GetMessagesAsync("recipient"));
        var sender = Assert.Single(await store.ListAgentsAsync(), agent => agent.AgentId == "sender");
        Assert.Equal("unknown", message.Provider);
        Assert.Equal("Codex", sender.AgentType);
    }

    public void Dispose() { if(Directory.Exists(_directory)) Directory.Delete(_directory,true); }
}

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}
