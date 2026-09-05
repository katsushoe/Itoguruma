namespace Itoguruma.Core;

public sealed record Agent(string AgentId, string Name, string AgentType, string? SessionId,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? MetadataJson, string? ProjectId = null);

public sealed record Message(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt, string DeliveryStatus, DateTimeOffset? LeaseUntil,
    string LeaseId, string LeaseOwnerAgentId, int DeliveryAttemptCount);

public sealed record SendMessageRequest(string SenderAgentId, IReadOnlyList<string> Recipients,
    string Body, string ThreadId, string Provider, string? ReplyToMessageId = null,
    string MessageType = "message", string? PayloadJson = null, string? IdempotencyKey = null);

public sealed record ConversationMessage(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record Project(string ProjectId, string DisplayName, string InboxAgentId, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ProjectMutation(string ProjectId, string? DisplayName = null,
    string? InboxAgentId = null);

public sealed record ProjectCandidate(string ProjectId, string DisplayName, bool Enabled);

public sealed record AgentHistoryDeleteResult(string AgentId, bool DryRun, int MessageCount,
    int DeliveryCount, int ThreadCount, bool CanUnregister, string CorrelationId);

public static class AgentHistoryErrorCodes
{
    public const string AgentNotFound = "ITG_AGENT_NOT_FOUND";
}

public sealed class AgentHistoryOperationException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public static class ProjectErrorCodes
{
    public const string InvalidProjectId = "ITG_PROJECT_ID_INVALID";
    public const string UnknownProject = "ITG_PROJECT_UNKNOWN";
    public const string DisabledProject = "ITG_PROJECT_DISABLED";
    public const string ProjectReferenced = "ITG_PROJECT_REFERENCED";
    public const string ProjectCaseConflict = "ITG_PROJECT_CASE_CONFLICT";
    public const string AgentTypeConflict = "ITG_AGENT_TYPE_CONFLICT";
    public const string ConfirmationFailed = "ITG_CONFIRMATION_FAILED";
    public const string ConfirmationExpired = "ITG_CONFIRMATION_EXPIRED";
    public const string ConsoleRedirected = "ITG_CONSOLE_REDIRECTED";
}

public sealed class ProjectOperationException(
    string errorCode,
    string message,
    string? attemptedProjectId = null,
    IReadOnlyList<ProjectCandidate>? candidates = null) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
    public string? AttemptedProjectId { get; } = attemptedProjectId;
    public IReadOnlyList<ProjectCandidate> Candidates { get; } = candidates ?? [];
}

/// <summary>Project Inbox IDの正規化、検証、類似候補選択を提供します。</summary>
public static class ProjectIdPolicy
{
    private const int CandidateLimit = 5;

    /// <summary>大文字小文字を区別しない照合用にProject IDを正規化します。</summary>
    public static string Normalize(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return projectId.Trim().ToLowerInvariant();
    }

    /// <summary>正規化済みProject IDがASCII英小文字で始まり、英小文字、数字、アンダースコアで構成されるかを返します。</summary>
    public static bool IsValid(string projectId)
    {
        if (projectId.Length == 0 || projectId[0] is < 'a' or > 'z') return false;
        return projectId.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    }

    /// <summary>登録済みプロジェクトから入力に近い候補を最大5件返します。</summary>
    public static IReadOnlyList<ProjectCandidate> FindCandidates(
        string attemptedProjectId,
        IEnumerable<Project> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var normalized = Normalize(attemptedProjectId);
        return projects
            .Select(project => new
            {
                Project = project,
                Normalized = Normalize(project.ProjectId),
                Distance = EditDistance(normalized, Normalize(project.ProjectId))
            })
            .Where(item => IsRelated(normalized, item.Normalized, item.Distance))
            .OrderByDescending(item => string.Equals(normalized, item.Normalized, StringComparison.Ordinal))
            .ThenByDescending(item => item.Normalized.StartsWith(normalized, StringComparison.Ordinal)
                || normalized.StartsWith(item.Normalized, StringComparison.Ordinal))
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Project.ProjectId, StringComparer.Ordinal)
            .Take(CandidateLimit)
            .Select(item => new ProjectCandidate(
                item.Project.ProjectId, item.Project.DisplayName, item.Project.Enabled))
            .ToArray();
    }

    private static bool IsRelated(string attempted, string candidate, int distance)
    {
        if (attempted.StartsWith(candidate, StringComparison.Ordinal)
            || candidate.StartsWith(attempted, StringComparison.Ordinal)) return true;
        var threshold = Math.Max(2, Math.Min(attempted.Length, candidate.Length) / 3);
        return distance <= threshold;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
