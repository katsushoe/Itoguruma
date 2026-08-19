using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class MessagingStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itoguruma-tests", Guid.NewGuid().ToString("N"));
    private SqliteMessageStore CreateStore() => new(Path.Combine(_directory,"messages.db"));

    [Fact]
    public async Task Message_WhenLeasedAndAcked_IsNotDeliveredAgain()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("sender","test"); await store.RegisterAgentAsync("recipient","test");
        var id=await store.SendMessageAsync(new("sender",["recipient"],"hello","thread-1"));
        var first=await store.GetMessagesAsync("recipient");
        Assert.Single(first); Assert.Equal(id,first[0].MessageId); Assert.True(await store.AckMessageAsync("recipient",id));
        Assert.Empty(await store.GetMessagesAsync("recipient"));
    }

    [Fact]
    public async Task Message_WhenLeaseExpires_IsDeliveredAgain()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        await store.SendMessageAsync(new("a",["b"],"hello","t"));
        Assert.Single(await store.GetMessagesAsync("b",leaseDuration:TimeSpan.FromMilliseconds(-1)));
        Assert.Single(await store.GetMessagesAsync("b"));
    }

    [Fact]
    public async Task SendMessage_WhenRecipientDoesNotExist_DoesNotPersistMessage()
    {
        var store=CreateStore(); await store.InitializeAsync(); await store.RegisterAgentAsync("a","test");
        await Assert.ThrowsAnyAsync<Exception>(()=>store.SendMessageAsync(new("a",["missing"],"hello","t")));
    }

    [Fact]
    public async Task SendMessage_WhenIdempotencyKeyIsRepeated_ReturnsOriginalMessage()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var request=new SendMessageRequest("a",["b"],"hello","t",IdempotencyKey:"request-1");
        var first=await store.SendMessageAsync(request); var second=await store.SendMessageAsync(request);
        Assert.Equal(first,second); Assert.Single(await store.GetMessagesAsync("b"));
    }

    [Fact]
    public async Task Message_WhenStoreIsRecreated_RemainsAvailable()
    {
        var firstStore=CreateStore(); await firstStore.InitializeAsync();
        await firstStore.RegisterAgentAsync("claude","test"); await firstStore.RegisterAgentAsync("codex","test");
        var id=await firstStore.SendMessageAsync(new("claude",["codex"],"persist","restart"));

        var restartedStore=CreateStore(); await restartedStore.InitializeAsync();
        var received=await restartedStore.GetMessagesAsync("codex");

        Assert.Single(received); Assert.Equal(id,received[0].MessageId);
    }

    [Fact]
    public async Task Messages_WhenSentInBothDirections_CanBeAcked()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("claude","test"); await store.RegisterAgentAsync("codex","test");
        var outbound=await store.SendMessageAsync(new("claude",["codex"],"request","roundtrip"));
        var atCodex=Assert.Single(await store.GetMessagesAsync("codex"));
        Assert.True(await store.AckMessageAsync("codex",atCodex.MessageId));
        var reply=await store.SendMessageAsync(new("codex",["claude"],"response","roundtrip",outbound));
        var atClaude=Assert.Single(await store.GetMessagesAsync("claude"));
        Assert.Equal(reply,atClaude.MessageId); Assert.Equal(outbound,atClaude.ReplyToMessageId);
        Assert.True(await store.AckMessageAsync("claude",atClaude.MessageId));
    }

    [Fact]
    public async Task SendMessage_WhenConcurrentWithSameKey_CreatesOneDelivery()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var request=new SendMessageRequest("a",["b"],"once","concurrent",IdempotencyKey:"same-key");

        var ids=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>store.SendMessageAsync(request)));

        Assert.Single(ids.Distinct(StringComparer.Ordinal));
        Assert.Single(await store.GetMessagesAsync("b",limit:50));
    }

    [Fact]
    public async Task GetConversationHistory_ReturnsMessagesInChronologicalOrder()
    {
        var store=CreateStore(); await store.InitializeAsync();
        await store.RegisterAgentAsync("a","test"); await store.RegisterAgentAsync("b","test");
        var first=await store.SendMessageAsync(new("a",["b"],"first","history-thread"));
        var second=await store.SendMessageAsync(new("b",["a"],"second","history-thread",first));

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
        for (var i=0;i<5;i++) await store.SendMessageAsync(new("a",["b"],$"m{i}","paged-thread"));

        var firstPage=await store.GetConversationHistoryAsync("paged-thread",limit:2,offset:0);
        var secondPage=await store.GetConversationHistoryAsync("paged-thread",limit:2,offset:2);

        Assert.Equal(["m0","m1"],firstPage.Select(x=>x.Body));
        Assert.Equal(["m2","m3"],secondPage.Select(x=>x.Body));
    }

    public void Dispose() { if(Directory.Exists(_directory)) Directory.Delete(_directory,true); }
}
