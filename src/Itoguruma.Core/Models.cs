namespace Itoguruma.Core;

public sealed record Agent(string AgentId, string Name, string AgentType, string? SessionId,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? MetadataJson);

public sealed record Message(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt, string DeliveryStatus, DateTimeOffset? LeaseUntil);

public sealed record SendMessageRequest(string SenderAgentId, IReadOnlyList<string> Recipients,
    string Body, string ThreadId, string? ReplyToMessageId = null,
    string MessageType = "message", string? PayloadJson = null, string? IdempotencyKey = null);

public sealed record ConversationMessage(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt);
