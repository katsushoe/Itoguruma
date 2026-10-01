using System.Text.Json;

namespace Itoguruma.Core;

/// <summary>CLI hookとMCP get_hook_contextが共有する受信コンテキスト文字列を作成します。</summary>
public static class HookContextFormatter
{
    /// <summary>受信箱と消費者のIDを見出しに含めます。ACKにはこの2つのIDとlease IDが必要です。</summary>
    public static string? Format(string agentId, string consumerAgentId, IReadOnlyList<Message> messages)
    {
        if (messages.Count == 0) return null;
        return AppLocalization.Text(
                $"Itoguruma inbox messages (agent_id: {agentId}, consumer_agent_id: {consumerAgentId}):\n",
                $"Itoguruma受信メッセージ（agent_id: {agentId}、consumer_agent_id: {consumerAgentId}）:\n") +
            JsonSerializer.Serialize(messages, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }
}
