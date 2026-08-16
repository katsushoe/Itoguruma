using System.Text.Json;
using Itoguruma.Cli;
using Itoguruma.Core;

var arguments = args.ToList();
if (arguments.Count == 0 || arguments[0] is "-h" or "--help") return Usage();
if (arguments[0] is "version" or "--version")
{
    Console.WriteLine($"itoguruma {ProductInfo.Version}");
    return 0;
}
if (arguments[0] == "auth")
{
    try
    {
        return new AuthCommand(new UserEnvironmentTokenStore(), Console.In, Console.Out, Console.Error)
            .Run(arguments.Skip(1).ToList());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}
var db = Option("--db") ?? Environment.GetEnvironmentVariable("ITOGURUMA_DB")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itoguruma", "messages.db");
var service = new MessagingService(new SqliteMessageStore(db)); await service.InitializeAsync();
try
{
    if (arguments[0] == "hook") return await RunHookAsync(service, Required("--agent"));
    object result = arguments[0] switch
    {
        "register" => await service.RegisterAgentAsync(Required("--agent"), Required("--type"), Option("--name"), Option("--session"), Option("--metadata")),
        "agents" => await service.ListAgentsAsync(),
        "send" => new { message_id = await service.SendMessageAsync(new(Required("--from"), [Required("--to")], Required("--body"), Required("--thread"), Option("--reply-to"), IdempotencyKey: Option("--idempotency-key"))) },
        "inbox" => await service.GetMessagesAsync(Required("--agent"), Number("--limit",50), TimeSpan.FromSeconds(Number("--lease-seconds",300)), Option("--thread")),
        "ack" => new { acked = await service.AckMessageAsync(Required("--agent"), Required("--message")) },
        _ => throw new ArgumentException($"Unknown command: {arguments[0]}")
    };
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})); return 0;
}
catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

string? Option(string name) { var i=arguments.IndexOf(name); return i>=0 && i+1<arguments.Count ? arguments[i+1] : null; }
string Required(string name) => Option(name) ?? throw new ArgumentException($"Missing option: {name}");
int Number(string name,int fallback) => int.TryParse(Option(name),out var value) ? value : fallback;
static int Usage() { Console.WriteLine("itoguruma register|agents|send|inbox|ack|hook|auth|version [options]\nSet ITOGURUMA_DB or pass --db <path>."); return 0; }

async Task<int> RunHookAsync(MessagingService messagingService, string agentId)
{
    var input = await Console.In.ReadToEndAsync();
    var eventName = ParseEventName(input);
    var messages = await messagingService.GetMessagesAsync(agentId, Number("--limit",50),
        TimeSpan.FromSeconds(Number("--lease-seconds",300)), Option("--thread"));
    if (messages.Count == 0) return 0;
    var context = "Itoguruma inbox messages:\n" + JsonSerializer.Serialize(messages,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    if (string.Equals(eventName, "Stop", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(context);
        return 2;
    }
    Console.WriteLine(context);
    return 0;
}

static string? ParseEventName(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return null;
    try
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.TryGetProperty("hook_event_name", out var value) ? value.GetString() : null;
    }
    catch (JsonException) { return null; }
}
