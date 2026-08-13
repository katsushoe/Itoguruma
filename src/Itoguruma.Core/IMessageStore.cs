namespace Itoguruma.Core;

public interface IMessageStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<Agent> RegisterAgentAsync(string agentId, string agentType, string? name = null,
        string? sessionId = null, string? metadataJson = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Agent>> ListAgentsAsync(CancellationToken cancellationToken = default);
    Task<string> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(string agentId, int limit = 50,
        TimeSpan? leaseDuration = null, string? threadId = null, CancellationToken cancellationToken = default);
    Task<bool> AckMessageAsync(string agentId, string messageId, CancellationToken cancellationToken = default);
}
