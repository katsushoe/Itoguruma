using System.Collections.Concurrent;
using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class MessagingStressTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "itoguruma-stress-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Deliveries_WhenStoresSendAndReceiveConcurrently_HaveNoLossOrDuplicates()
    {
        const int messageCount = 120;
        var stores = await CreateStoresAsync(6);
        await stores[0].RegisterAgentAsync("sender", "test");
        await stores[0].RegisterAgentAsync("recipient", "test");

        var sentIds = await Task.WhenAll(Enumerable.Range(0, messageCount).Select(index =>
            stores[index % stores.Count].SendMessageAsync(new(
                "sender", ["recipient"], $"message-{index}", "stress", "codex",
                IdempotencyKey: $"stress-{index}"))));

        Assert.Equal(messageCount, sentIds.Distinct(StringComparer.Ordinal).Count());
        var deliveries = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var acknowledged = 0;
        var workers = stores.Take(4).Select(store => ConsumeAsync(store)).ToArray();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(messageCount, acknowledged);
        Assert.Equal(messageCount, deliveries.Count);
        Assert.All(deliveries.Values, count => Assert.Equal(1, count));
        Assert.Empty(await stores[0].GetMessagesAsync("recipient", limit: 500));
        return;

        async Task ConsumeAsync(SqliteMessageStore store)
        {
            while (Volatile.Read(ref acknowledged) < messageCount)
            {
                var messages = await store.GetMessagesAsync(
                    "recipient", limit: 15, leaseDuration: TimeSpan.FromSeconds(10));
                if (messages.Count == 0)
                {
                    await Task.Delay(10);
                    continue;
                }

                foreach (var message in messages)
                {
                    deliveries.AddOrUpdate(message.MessageId, 1, (_, count) => count + 1);
                    if (await store.AckMessageAsync(
                        "recipient", message.LeaseOwnerAgentId, message.MessageId, message.LeaseId))
                        Interlocked.Increment(ref acknowledged);
                }
            }
        }
    }

    [Fact]
    public async Task Deliveries_WhenLeasesExpireUnderContention_AreRedeliveredOnce()
    {
        const int messageCount = 40;
        var stores = await CreateStoresAsync(4);
        await stores[0].RegisterAgentAsync("sender", "test");
        await stores[0].RegisterAgentAsync("recipient", "test");
        await Task.WhenAll(Enumerable.Range(0, messageCount).Select(index =>
            stores[index % stores.Count].SendMessageAsync(new(
                "sender", ["recipient"], $"lease-{index}", "lease-stress", "codex"))));

        var firstDelivery = await stores[0].GetMessagesAsync(
            "recipient", limit: messageCount, leaseDuration: TimeSpan.FromMilliseconds(100));
        Assert.Equal(messageCount, firstDelivery.Count);
        await Task.Delay(250);

        var competingReads = await Task.WhenAll(stores.Select(store =>
            store.GetMessagesAsync("recipient", limit: 15, leaseDuration: TimeSpan.FromSeconds(10))));
        var redelivered = competingReads.SelectMany(messages => messages).ToArray();

        Assert.Equal(messageCount, redelivered.Length);
        Assert.Equal(messageCount, redelivered.Select(message => message.MessageId)
            .Distinct(StringComparer.Ordinal).Count());
        await Task.WhenAll(redelivered.Select((message, index) =>
            stores[index % stores.Count].AckMessageAsync(
                "recipient", message.LeaseOwnerAgentId, message.MessageId, message.LeaseId)));
        Assert.Empty(await stores[0].GetMessagesAsync("recipient", limit: 500));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<IReadOnlyList<SqliteMessageStore>> CreateStoresAsync(int count)
    {
        var databasePath = Path.Combine(_directory, "messages.db");
        var stores = Enumerable.Range(0, count)
            .Select(_ => new SqliteMessageStore(databasePath))
            .ToArray();
        await stores[0].InitializeAsync();
        return stores;
    }
}
