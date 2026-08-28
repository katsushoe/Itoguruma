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
    public async Task SendMessage_ToKnownProjectWithDifferentCase_UsesCanonicalProject()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("Kotodama", "Kotodama", "project-inbox-kotodama"));

        await store.SendMessageAsync(new("sender", ["KOTODAMA"], "body", "thread", "codex"));

        Assert.Single(await store.ListProjectsAsync());
        Assert.Equal("body", Assert.Single(await store.GetMessagesAsync("project-inbox-kotodama")).Body);
        Assert.Equal("Kotodama", (await store.GetProjectAsync("kotodama"))!.ProjectId);
    }

    [Fact]
    public async Task ProjectOperations_WithDifferentCase_TargetCanonicalProject()
    {
        var store = await CreateStoreAsync();
        await store.AddProjectAsync(new("Kotodama", "Before", "project-inbox-kotodama"));

        var updated = await store.UpdateProjectAsync(new("KOTODAMA", "After"));
        var disabled = await store.SetProjectEnabledAsync("kotodama", false);

        Assert.Equal("Kotodama", updated.ProjectId);
        Assert.Equal("After", updated.DisplayName);
        Assert.False(disabled.Enabled);
        Assert.True(await store.DeleteProjectAsync("KoToDaMa"));
        Assert.Empty(await store.ListProjectsAsync());
    }

    [Fact]
    public async Task AddProject_WhenIdDiffersOnlyByCase_RejectsDuplicate()
    {
        var store = await CreateStoreAsync();
        await store.AddProjectAsync(new("Kotodama", null, "project-inbox-kotodama"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.AddProjectAsync(new("kotodama", null, "project-inbox-duplicate")));

        Assert.Single(await store.ListProjectsAsync());
    }

    [Fact]
    public async Task SendMessage_ToUnknownProject_AutoRegistersProjectAndInbox()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");

        await store.SendMessageAsync(new("sender", ["Kotodama"], "body", "thread", "codex"));

        var project = Assert.Single(await store.ListProjectsAsync());
        Assert.Equal("Kotodama", project.ProjectId);
        Assert.Equal("Kotodama", project.DisplayName);
        Assert.Equal("Kotodama", project.InboxAgentId);
        Assert.True(project.Enabled);
        Assert.Equal("body", Assert.Single(await store.GetMessagesAsync("Kotodama")).Body);
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

    [Fact]
    public async Task ConcurrentSend_ToUnknownProject_RegistersOneProjectAndInbox()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => store.SendMessageAsync(
            new("sender", ["Kotodama"], $"body-{index}", "thread", "codex"))));

        Assert.Single(await store.ListProjectsAsync());
        Assert.Single(await store.ListAgentsAsync(), agent => agent.AgentType == "project_inbox");
        Assert.Equal(12, (await store.GetMessagesAsync("Kotodama", limit: 50)).Count);
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
