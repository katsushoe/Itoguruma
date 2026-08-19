using System.Text.Json;

namespace Itoguruma.Core;

public sealed class MessagingService(IMessageStore store)
{
    private static readonly HashSet<string> MessageTypes = new(StringComparer.Ordinal)
        { "message", "notification", "system" };

    public Task InitializeAsync(CancellationToken cancellationToken = default) => store.InitializeAsync(cancellationToken);

    public Task<Agent> RegisterAgentAsync(string agentId, string agentType, string? name = null,
        string? sessionId = null, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        ValidateJson(metadataJson, nameof(metadataJson));
        return store.RegisterAgentAsync(agentId, agentType, name, sessionId, metadataJson, cancellationToken);
    }

    public Task<IReadOnlyList<Agent>> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        store.ListAgentsAsync(cancellationToken);

    public Task<string> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (!MessageTypes.Contains(request.MessageType))
            throw new ArgumentException($"Unsupported message type: {request.MessageType}", nameof(request));
        ValidateJson(request.PayloadJson, nameof(request.PayloadJson));
        return store.SendMessageAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<Message>> GetMessagesAsync(string agentId, int limit = 50,
        TimeSpan? leaseDuration = null, string? threadId = null, CancellationToken cancellationToken = default) =>
        store.GetMessagesAsync(agentId, limit, leaseDuration, threadId, cancellationToken);

    public Task<bool> AckMessageAsync(string agentId, string messageId, CancellationToken cancellationToken = default) =>
        store.AckMessageAsync(agentId, messageId, cancellationToken);

    public Task<IReadOnlyList<ConversationMessage>> GetConversationHistoryAsync(string threadId, int limit = 100,
        int offset = 0, CancellationToken cancellationToken = default) =>
        store.GetConversationHistoryAsync(threadId, limit, offset, cancellationToken);

    private static void ValidateJson(string? value, string name)
    {
        if (value is null) return;
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException ex) { throw new ArgumentException("Value must be valid JSON.", name, ex); }
    }
}
