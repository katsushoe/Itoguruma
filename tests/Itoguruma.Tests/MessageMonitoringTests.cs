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
        await store.SendMessageAsync(new("claude", ["codex"], "pending body", "pending-thread"));
        var leasedId = await store.SendMessageAsync(new("claude", ["codex"], "leased body", "leased-thread"));
        Assert.Contains(await store.GetMessagesAsync("codex", threadId: "leased-thread"), x => x.MessageId == leasedId);

        var monitor = new SqliteMessageMonitor(DatabasePath);
        var snapshot = await monitor.LoadAsync(new());

        Assert.Equal(1, snapshot.PendingCount);
        Assert.Equal(1, snapshot.LeasedCount);
        Assert.Equal(0, snapshot.AcknowledgedCount);
        Assert.Equal(2, snapshot.Messages.Count);
        var secondSnapshot = await monitor.LoadAsync(new(Status: "leased"));
        Assert.Single(secondSnapshot.Messages);
        Assert.Equal("leased", secondSnapshot.Messages[0].DeliveryStatus);
    }

    [Fact]
    public async Task Monitor_WhenAgentAndTextAreSpecified_FiltersDeliveries()
    {
        var store = new SqliteMessageStore(DatabasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("claude", "test");
        await store.RegisterAgentAsync("codex", "test");
        await store.RegisterAgentAsync("reviewer", "test");
        await store.SendMessageAsync(new("claude", ["codex"], "alpha request", "feature-a"));
        await store.SendMessageAsync(new("reviewer", ["claude"], "beta review", "feature-b"));

        var monitor = new SqliteMessageMonitor(DatabasePath);
        var snapshot = await monitor.LoadAsync(new(AgentId: "codex", SearchText: "alpha"));

        var message = Assert.Single(snapshot.Messages);
        Assert.Equal("codex", message.RecipientAgentId);
        Assert.Equal("alpha request", message.Body);
        Assert.Equal(3, snapshot.AgentIds.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
