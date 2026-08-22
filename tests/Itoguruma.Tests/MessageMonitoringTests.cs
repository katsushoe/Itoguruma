using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class MessageMonitoringTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itoguruma-monitor-tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "messages.db");

    [Fact]
    public async Task Monitor_WhenMessagesHaveDifferentStates_ReturnsCountsWithoutChangingState()
    {
        var store = new SqliteMessageStore(DatabasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("claude", "test");
        await store.RegisterAgentAsync("codex", "test");
        await store.SendMessageAsync(new("claude", ["codex"], "pending body", "pending-thread", "claude-code"));
        var leasedId = await store.SendMessageAsync(new("claude", ["codex"], "leased body", "leased-thread", "claude-code"));
        Assert.Contains(await store.GetMessagesAsync("codex", threadId: "leased-thread"), x => x.MessageId == leasedId);
        var acknowledgedId = await store.SendMessageAsync(new("claude", ["codex"], "acked body", "acked-thread", "claude-code"));
        Assert.Contains(await store.GetMessagesAsync("codex", threadId: "acked-thread"), x => x.MessageId == acknowledgedId);
        Assert.True(await store.AckMessageAsync("codex", acknowledgedId));

        var monitor = new SqliteMessageMonitor(DatabasePath);
        var snapshot = await monitor.LoadAsync(new());

        Assert.Equal(1, snapshot.PendingCount);
        Assert.Equal(1, snapshot.LeasedCount);
        Assert.Equal(1, snapshot.AcknowledgedCount);
        Assert.Equal(3, snapshot.Messages.Count);
        var leasedSnapshot = await monitor.LoadAsync(new(Status: "leased"));
        Assert.Single(leasedSnapshot.Messages);
        Assert.Equal("leased", leasedSnapshot.Messages[0].DeliveryStatus);
        var pendingSnapshot = await monitor.LoadAsync(new(Status: "pending"));
        var pendingMessage = Assert.Single(pendingSnapshot.Messages);
        Assert.Equal("pending", pendingMessage.DeliveryStatus);
    }

    [Fact]
    public async Task Monitor_WhenAgentAndTextAreSpecified_FiltersDeliveries()
    {
        var store = new SqliteMessageStore(DatabasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("claude", "test");
        await store.RegisterAgentAsync("codex", "test");
        await store.RegisterAgentAsync("reviewer", "test");
        await store.SendMessageAsync(new("claude", ["codex"], "alpha request", "feature-a", "claude-code"));
        await store.SendMessageAsync(new("reviewer", ["claude"], "beta review", "feature-b", "codex"));

        var monitor = new SqliteMessageMonitor(DatabasePath);
        var snapshot = await monitor.LoadAsync(new(AgentId: "codex", SearchText: "alpha"));

        var message = Assert.Single(snapshot.Messages);
        Assert.Equal("codex", message.RecipientAgentId);
        Assert.Equal("claude-code", message.Provider);
        Assert.Equal("alpha request", message.Body);
        Assert.Equal(3, snapshot.AgentIds.Count);
    }

    [Fact]
    public async Task Monitor_WhenMessageTypeIsSpecified_FiltersDeliveries()
    {
        var store = new SqliteMessageStore(DatabasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.RegisterAgentAsync("recipient", "test");
        await store.SendMessageAsync(new("sender", ["recipient"], "normal", "normal", "codex"));
        await store.SendMessageAsync(new("sender", ["recipient"], "cr", "cr",
            Provider: "codex", MessageType: "change_request", PayloadJson: "{}"));

        var monitor = new SqliteMessageMonitor(DatabasePath);
        var snapshot = await monitor.LoadAsync(new(MessageType: "change_request"));

        var message = Assert.Single(snapshot.Messages);
        Assert.Equal("change_request", message.MessageType);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
