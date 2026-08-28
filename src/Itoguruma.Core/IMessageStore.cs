namespace Itoguruma.Core;

public interface IMessageStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<Agent> RegisterAgentAsync(string agentId, string agentType, string? name = null,
        string? sessionId = null, string? metadataJson = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Agent>> ListAgentsAsync(CancellationToken cancellationToken = default);
    Task<bool> UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task<AgentHistoryDeleteResult> DeleteAgentHistoryAsync(string agentId, bool dryRun,
        CancellationToken cancellationToken = default);
    Task<string> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(string agentId, int limit = 50,
        TimeSpan? leaseDuration = null, string? threadId = null, string? messageType = null,
        CancellationToken cancellationToken = default);
    Task<bool> AckMessageAsync(string agentId, string messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> GetConversationHistoryAsync(string threadId, int limit = 100,
        int offset = 0, CancellationToken cancellationToken = default);
    Task<Project> AddProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default);
    Task<Project> UpdateProjectAsync(ProjectMutation mutation, CancellationToken cancellationToken = default);
    Task<Project> SetProjectEnabledAsync(string projectId, bool enabled, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
}
