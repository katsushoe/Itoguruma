using System.Text.Json;

namespace Itoguruma.Core;

public sealed class MessagingService(IMessageStore store, ChangeRequestValidator? changeRequestValidator = null)
{
    private static readonly HashSet<string> MessageTypes = new(StringComparer.Ordinal)
        { "message", "notification", "system", "change_request" };

    public Task InitializeAsync(CancellationToken cancellationToken = default) => store.InitializeAsync(cancellationToken);

    public Task<Agent> RegisterAgentAsync(string agentId, string agentType, string projectId, string? name = null,
        string? sessionId = null, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        ValidateJson(metadataJson, nameof(metadataJson));
        return store.RegisterAgentAsync(agentId, agentType, projectId, name, sessionId, metadataJson, cancellationToken);
    }

    internal Task<Agent> RegisterAgentAsync(string agentId, string agentType,
        CancellationToken cancellationToken = default) =>
        store is SqliteMessageStore sqlite
            ? sqlite.RegisterLegacyAgentAsync(agentId, agentType, cancellationToken)
            : throw new NotSupportedException("Legacy registration is only available to database compatibility tests.");

    public Task<Project> RegisterProjectInboxAsync(string projectId, string displayName,
        CancellationToken cancellationToken = default) =>
        store.RegisterProjectInboxAsync(projectId, displayName, cancellationToken);

    public Task<IReadOnlyList<Agent>> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        store.ListAgentsAsync(cancellationToken);

    public Task<bool> UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default) =>
        store.UnregisterAgentAsync(agentId, cancellationToken);

    public Task<AgentHistoryDeleteResult> DeleteAgentHistoryAsync(string agentId, bool dryRun,
        CancellationToken cancellationToken = default) =>
        store.DeleteAgentHistoryAsync(agentId, dryRun, cancellationToken);

    public async Task<string> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (!MessageTypes.Contains(request.MessageType))
            throw new ArgumentException($"Unsupported message type: {request.MessageType}", nameof(request));
        ValidateJson(request.PayloadJson, nameof(request.PayloadJson));
        if (request.MessageType == "change_request")
        {
            if (changeRequestValidator is null)
                throw new ArgumentException("change_request delivery is not configured.", nameof(request));
            await changeRequestValidator.InspectAsync(request.PayloadJson, cancellationToken: cancellationToken);
        }
        return await store.SendMessageAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<Message>> GetMessagesAsync(string agentId, int limit = 50,
        TimeSpan? leaseDuration = null, string? threadId = null, string? messageType = null,
        CancellationToken cancellationToken = default) =>
        store.GetMessagesAsync(agentId, limit, leaseDuration, threadId, messageType, cancellationToken);

    /// <summary>保存済みpayloadとCRファイルの現在状態を再検証します。</summary>
    public Task<ChangeRequestInspection> InspectChangeRequestAsync(string? payloadJson,
        CancellationToken cancellationToken = default) =>
        changeRequestValidator?.InspectAsync(payloadJson, requireStatusMatch: false, cancellationToken)
        ?? throw new InvalidOperationException("change_request delivery is not configured.");

    public Task<bool> AckMessageAsync(string agentId, string messageId, CancellationToken cancellationToken = default) =>
        store.AckMessageAsync(agentId, messageId, cancellationToken);

    public Task<IReadOnlyList<ConversationMessage>> GetConversationHistoryAsync(string threadId, int limit = 100,
        int offset = 0, CancellationToken cancellationToken = default) =>
        store.GetConversationHistoryAsync(threadId, limit, offset, cancellationToken);

    public Task<Project> AddProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default) =>
        store.AddProjectAsync(mutation, cancellationToken);
    public Task<Project> UpdateProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default) =>
        store.UpdateProjectAsync(mutation, cancellationToken);
    public Task<Project> SetProjectEnabledAsync(string projectId, bool enabled, CancellationToken cancellationToken = default) =>
        store.SetProjectEnabledAsync(projectId, enabled, cancellationToken);
    public Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
        store.DeleteProjectAsync(projectId, cancellationToken);
    public Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        store.ListProjectsAsync(cancellationToken);
    public Task<Project?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
        store.GetProjectAsync(projectId, cancellationToken);

    private static void ValidateJson(string? value, string name)
    {
        if (value is null) return;
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException ex) { throw new ArgumentException("Value must be valid JSON.", name, ex); }
    }
}
