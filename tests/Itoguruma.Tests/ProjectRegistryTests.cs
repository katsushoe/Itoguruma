using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ProjectRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itoguruma-project-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendMessage_ToKnownProject_CreatesInboxAndDelivers()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("Kotodama", "Kotodama", "project-inbox-kotodama"));

        await store.SendMessageAsync(new("sender", ["Kotodama"], "body", "thread", "codex"));

        var message = Assert.Single(await store.GetMessagesAsync("project-inbox-kotodama"));
        Assert.Equal("body", message.Body);
        var inbox = Assert.Single(await store.ListAgentsAsync(), agent => agent.AgentId == "project-inbox-kotodama");
        Assert.Equal("project_inbox", inbox.AgentType);
    }

    [Fact]
    public async Task SendMessage_ToDisabledProject_ReturnsFixedErrorCode()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("Kotodama", null, "project-inbox-kotodama"));
        await store.SetProjectEnabledAsync("Kotodama", false);

        var exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.SendMessageAsync(new("sender", ["Kotodama"], "body", "thread", "codex")));

        Assert.Equal(ProjectErrorCodes.DisabledProject, exception.ErrorCode);
        Assert.DoesNotContain(await store.ListAgentsAsync(), agent => agent.AgentType == "project_inbox");
    }

    [Fact]
    public async Task ConcurrentSend_ToKnownProject_RegistersOneInbox()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("Kotodama", null, "project-inbox-kotodama"));

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => store.SendMessageAsync(
            new("sender", ["Kotodama"], $"body-{index}", "thread", "codex"))));

        Assert.Single(await store.ListAgentsAsync(), agent => agent.AgentType == "project_inbox");
        Assert.Equal(12, (await store.GetMessagesAsync("project-inbox-kotodama", limit: 50)).Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<SqliteMessageStore> CreateStoreAsync()
    {
        var store = new SqliteMessageStore(Path.Combine(_directory, "messages.db"));
        await store.InitializeAsync();
        return store;
    }
}
