using System.Text.Json;
using Itoguruma.Core;

var databasePath = Environment.GetEnvironmentVariable("ITOGURUMA_DB")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itoguruma", "messages.db");
var service = new MessagingService(new SqliteMessageStore(databasePath));
await service.InitializeAsync();
var server = new McpServer(service, Console.In, Console.Out);
await server.RunAsync();

internal sealed class McpServer(MessagingService service, TextReader input, TextWriter output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement? id = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement; id = root.TryGetProperty("id", out var requestId) ? requestId.Clone() : null;
                var method = root.GetProperty("method").GetString();
                if (id is null && method is "notifications/initialized") continue;
                object result = method switch
                {
                    "initialize" => new { protocolVersion = "2025-03-26", capabilities = new { tools = new { } }, serverInfo = new { name = "itoguruma", version = ProductInfo.Version } },
                    "ping" => new { },
                    "tools/list" => new { tools = ToolDefinitions.All },
                    "tools/call" => await CallToolAsync(root.GetProperty("params"), cancellationToken),
                    _ => throw new RpcException(-32601, $"Method not found: {method}")
                };
                await WriteAsync(new { jsonrpc = "2.0", id, result });
            }
            catch (Exception ex)
            {
                var rpc = ex as RpcException;
                await WriteAsync(new { jsonrpc = "2.0", id, error = new { code = rpc?.Code ?? -32603, message = ex.Message } });
            }
        }
    }

    private async Task<object> CallToolAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var name = parameters.GetProperty("name").GetString();
        var args = parameters.TryGetProperty("arguments", out var value) ? value : default;
        object data = name switch
        {
            "register_agent" => await service.RegisterAgentAsync(S(args,"agent_id"), S(args,"agent_type"), O(args,"name"), O(args,"session_id"), O(args,"metadata_json"), cancellationToken),
            "list_agents" => await service.ListAgentsAsync(cancellationToken),
            "send_message" => new { message_id = await service.SendMessageAsync(new(S(args,"sender_agent_id"), Recipients(args), S(args,"body"), S(args,"thread_id"), O(args,"reply_to_message_id"), O(args,"message_type") ?? "message", O(args,"payload_json"), O(args,"idempotency_key")), cancellationToken) },
            "get_messages" => await service.GetMessagesAsync(S(args,"agent_id"), I(args,"limit",50), TimeSpan.FromSeconds(I(args,"lease_seconds",300)), O(args,"thread_id"), cancellationToken),
            "ack_message" => new { acked = await service.AckMessageAsync(S(args,"agent_id"), S(args,"message_id"), cancellationToken) },
            _ => throw new RpcException(-32602, $"Unknown tool: {name}")
        };
        return new
        {
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(data, JsonOptions) } },
            structuredContent = new { data }
        };
    }

    private async Task WriteAsync(object value) { await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions)); await output.FlushAsync(); }
    private static string S(JsonElement e,string n) => e.TryGetProperty(n,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString()! : throw new RpcException(-32602,$"Missing string: {n}");
    private static string? O(JsonElement e,string n) => e.TryGetProperty(n,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString() : null;
    private static int I(JsonElement e,string n,int d) => e.TryGetProperty(n,out var v) && v.TryGetInt32(out var i) ? i : d;
    private static IReadOnlyList<string> Recipients(JsonElement e) => e.TryGetProperty("recipients",out var a) && a.ValueKind==JsonValueKind.Array ? a.EnumerateArray().Select(x=>x.GetString()!).ToArray() : new[] { S(e,"recipient") };
}

internal sealed class RpcException(int code, string message) : Exception(message) { public int Code { get; } = code; }

internal static class ToolDefinitions
{
    private static object Tool(string name,string description,object properties,string[]? required=null) => new { name,description,inputSchema=new { type="object",properties,required=required ?? [] } };
    public static readonly object[] All =
    [
        Tool("register_agent","Register or refresh an agent.",new { agent_id=new{type="string"},agent_type=new{type="string"},name=new{type="string"},session_id=new{type="string"},metadata_json=new{type="string"}},["agent_id","agent_type"]),
        Tool("list_agents","List registered agents.",new { }),
        Tool("send_message","Persist and enqueue a message idempotently.",new { sender_agent_id=new{type="string"},recipient=new{type="string"},recipients=new{type="array",items=new{type="string"}},body=new{type="string"},thread_id=new{type="string"},reply_to_message_id=new{type="string"},message_type=new{type="string",@enum=new[]{"message","notification","system"}},payload_json=new{type="string"},idempotency_key=new{type="string"}},["sender_agent_id","body","thread_id"]),
        Tool("get_messages","Lease pending messages for an agent.",new { agent_id=new{type="string"},limit=new{type="integer",minimum=1,maximum=500},lease_seconds=new{type="integer",minimum=1},thread_id=new{type="string"}},["agent_id"]),
        Tool("ack_message","Acknowledge a leased message.",new { agent_id=new{type="string"},message_id=new{type="string"}},["agent_id","message_id"])
    ];
}
