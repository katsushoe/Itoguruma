using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ProjectRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itoguruma-project-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RegisterProjectInbox_IsIdempotentAndRepairsOrphanInbox()
    {
        var store = await CreateStoreAsync();
        await store.RegisterLegacyAgentAsync("githubieselftest", "project_inbox");
        await store.RegisterProjectInboxAsync("GithubieSelfTest", "Githubie Self Test");
        await store.RegisterProjectInboxAsync("githubieselftest", "Githubie Self Test");
        var project = Assert.Single(await store.ListProjectsAsync());
        var agent = Assert.Single(await store.ListAgentsAsync());
        Assert.Equal("githubieselftest", project.ProjectId);
        Assert.Equal(project.ProjectId, agent.ProjectId);
    }

    [Fact]
    public async Task RegisterAgent_RequiresEnabledExistingParent()
    {
        var store = await CreateStoreAsync();
        var missing = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.RegisterAgentAsync("worker", "codex", "missing"));
        Assert.Equal(ProjectErrorCodes.UnknownProject, missing.ErrorCode);
        await store.RegisterProjectInboxAsync("moyai", "Moyai");
        await store.SetProjectEnabledAsync("moyai", false);
        var disabled = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.RegisterAgentAsync("worker", "codex", "moyai"));
        Assert.Equal(ProjectErrorCodes.DisabledProject, disabled.ErrorCode);
        Assert.DoesNotContain(await store.ListAgentsAsync(), agent => agent.AgentId == "worker");
    }

    [Fact]
    public async Task RegisterProjectInbox_WhenAgentTypeConflicts_RollsBackProject()
    {
        var store = await CreateStoreAsync();
        await store.RegisterLegacyAgentAsync("githubieselftest", "codex");
        var error = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.RegisterProjectInboxAsync("githubieselftest", "Githubie Self Test"));
        Assert.Equal(ProjectErrorCodes.AgentTypeConflict, error.ErrorCode);
        Assert.Empty(await store.ListProjectsAsync());
    }

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
        Assert.Equal("kotodama", (await store.GetProjectAsync("kotodama"))!.ProjectId);
    }

    [Fact]
    public async Task ProjectOperations_WithDifferentCase_TargetCanonicalProject()
    {
        var store = await CreateStoreAsync();
        await store.AddProjectAsync(new("Kotodama", "Before", "project-inbox-kotodama"));

        var updated = await store.UpdateProjectAsync(new("KOTODAMA", "After"));
        var disabled = await store.SetProjectEnabledAsync("kotodama", false);

        Assert.Equal("kotodama", updated.ProjectId);
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
        Assert.Equal("kotodama", project.ProjectId);
        Assert.Equal("kotodama", project.DisplayName);
        Assert.Equal("kotodama", project.InboxAgentId);
        Assert.True(project.Enabled);
        Assert.Equal("body", Assert.Single(await store.GetMessagesAsync("kotodama")).Body);
    }

    [Fact]
    public async Task SendMessage_ToProjectWithUnderscore_AutoRegistersProjectAndInbox()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");

        await store.SendMessageAsync(new("sender", ["AI_prompt"], "body", "thread", "codex"));

        var project = Assert.Single(await store.ListProjectsAsync());
        Assert.Equal("ai_prompt", project.ProjectId);
        Assert.Equal("body", Assert.Single(await store.GetMessagesAsync("ai_prompt")).Body);
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
        Assert.Equal("Kotodama", exception.AttemptedProjectId);
        var candidate = Assert.Single(exception.Candidates);
        Assert.Equal("kotodama", candidate.ProjectId);
        Assert.False(candidate.Enabled);
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
        Assert.Equal(12, (await store.GetMessagesAsync("kotodama", limit: 50)).Count);
    }

    [Theory]
    [InlineData("Itoguruma", "itoguruma")]
    [InlineData("moyai2", "moyai2")]
    [InlineData("AI_prompt", "ai_prompt")]
    public void ProjectIdPolicy_WhenInputUsesSupportedCharacters_NormalizesAndValidates(
        string input,
        string expected)
    {
        Assert.Equal(expected, ProjectIdPolicy.Normalize(input));
        Assert.True(ProjectIdPolicy.IsValid(expected));
    }

    [Theory]
    [InlineData("moyai-codex-root")]
    [InlineData("_moyai")]
    [InlineData("2moyai")]
    [InlineData("もやい")]
    public void ProjectIdPolicy_WhenInputViolatesContract_ReturnsInvalid(string input)
    {
        Assert.False(ProjectIdPolicy.IsValid(ProjectIdPolicy.Normalize(input)));
    }

    [Fact]
    public void ProjectIdPolicy_WhenProjectsAreUnrelated_ReturnsNoCandidates()
    {
        var now = DateTimeOffset.Parse(
            "2026-08-30T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Project[] projects = [new("itoguruma", "Itoguruma", "itoguruma", true, now, now)];

        var candidates = ProjectIdPolicy.FindCandidates("completely-different", projects);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task SendMessage_WhenProjectIdContainsHyphens_ReturnsCandidatesWithoutRegisteringProject()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("moyai", "Moyai", "project-inbox-moyai"));

        var exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.SendMessageAsync(new("sender", ["moyai-codex-root"], "body", "thread", "codex")));

        Assert.Equal(ProjectErrorCodes.InvalidProjectId, exception.ErrorCode);
        Assert.Equal("moyai-codex-root", exception.AttemptedProjectId);
        Assert.Equal("moyai", Assert.Single(exception.Candidates).ProjectId);
        Assert.Single(await store.ListProjectsAsync());
        Assert.Empty(await store.GetMessagesAsync("project-inbox-moyai"));
    }

    [Fact]
    public async Task SendMessage_WhenRuntimeAgentMatchesInvalidProjectId_RejectsRecipient()
    {
        var store = await CreateStoreAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.RegisterAgentAsync("moyai-codex-root", "codex");

        var exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            store.SendMessageAsync(new("sender", ["moyai-codex-root"], "body", "thread", "codex")));

        Assert.Equal(ProjectErrorCodes.InvalidProjectId, exception.ErrorCode);
        Assert.Empty(await store.GetMessagesAsync("moyai-codex-root"));
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
