using System.Text.Json;
using Itoguruma.Cli;
using Itoguruma.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

AppLocalization.ConfigureFromEnvironment();

var arguments = args.ToList();
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz ";
    });
    builder.Services.Configure<ConsoleLoggerOptions>(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);
});
var logger = loggerFactory.CreateLogger("Itoguruma.Cli");
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception ex)
        logger.LogCritical(ex, "[UnhandledException] The CLI terminated because of an unhandled exception.");
};
TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    logger.LogError(eventArgs.Exception, "[UnobservedTaskException] An unobserved task exception occurred.");
    eventArgs.SetObserved();
};

try
{
    if (arguments.Count == 0 || arguments[0] is "-h" or "--help") return Usage();
    if (arguments[0] is "version" or "--version")
    {
        Console.WriteLine($"itoguruma {ProductInfo.Version}");
        return 0;
    }
    if (arguments[0] == "auth")
    {
        return new AuthCommand(new UserEnvironmentTokenStore(), Console.In, Console.Out, Console.Error)
            .Run(arguments.Skip(1).ToList());
    }
    var db = Option("--db") ?? Environment.GetEnvironmentVariable("ITOGURUMA_DB")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itoguruma", "messages.db");
    var crRoot = Environment.GetEnvironmentVariable("ITOGURUMA_CR_ROOT");
    var changeRequestValidator = string.IsNullOrWhiteSpace(crRoot) ? null : new ChangeRequestValidator(crRoot);
    var service = new MessagingService(new SqliteMessageStore(db), changeRequestValidator);
    await service.InitializeAsync();
    if (arguments[0] == "project") return await RunProjectAsync(service);
    if (arguments[0] == "hook") return await RunHookAsync(service, Required("--agent"));
    object result = arguments[0] switch
    {
        "register" => await service.RegisterAgentAsync(Required("--agent"), Required("--type"), Option("--name"), Option("--session"), Option("--metadata")),
        "agents" => await service.ListAgentsAsync(),
        "unregister" => new { unregistered = await service.UnregisterAgentAsync(Required("--agent")) },
        "delete-agent-history" => await RunDeleteAgentHistoryAsync(service),
        "send" => new { message_id = await service.SendMessageAsync(new(Required("--from"), RequiredMany("--to"), Required("--body"), Required("--thread"), Required("--provider"), Option("--reply-to"), Option("--message-type") ?? "message", Option("--payload-json"), Option("--idempotency-key"))) },
        "inbox" => await service.GetMessagesAsync(Required("--agent"), Number("--limit",50), TimeSpan.FromSeconds(Number("--lease-seconds",300)), Option("--thread"), Option("--message-type")),
        "ack" => new { acked = await service.AckMessageAsync(Required("--agent"), Required("--message")) },
        "history" => await service.GetConversationHistoryAsync(Required("--thread"), Number("--limit", 100), Number("--offset", 0)),
        "inspect-change-request" => await service.InspectChangeRequestAsync(Required("--payload-json")),
        _ => throw new ArgumentException(AppLocalization.Text($"Unknown command: {arguments[0]}", $"不明なコマンドです: {arguments[0]}"))
    };
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})); return 0;
}
catch(Exception ex)
{
    logger.LogError(ex, "[CommandFailure] The CLI command failed.");
    Console.Error.WriteLine(AppLocalization.Text(
        "Itoguruma could not complete the command. See the error log output for details.",
        "Itogurumaはコマンドを完了できませんでした。詳細はエラーログ出力を確認してください。"));
    return 2;
}

string? Option(string name) { var i=arguments.IndexOf(name); return i>=0 && i+1<arguments.Count ? arguments[i+1] : null; }
string Required(string name) => Option(name) ?? throw new ArgumentException(AppLocalization.Text($"Missing option: {name}", $"必須オプションがありません: {name}"));
IReadOnlyList<string> RequiredMany(string name)
{
    var values = arguments.Select((value, index) => (value, index))
        .Where(item => item.value == name && item.index + 1 < arguments.Count)
        .Select(item => arguments[item.index + 1]).ToArray();
    return values.Length > 0
        ? values
        : throw new ArgumentException(AppLocalization.Text($"Missing option: {name}", $"必須オプションがありません: {name}"));
}
int Number(string name,int fallback) => int.TryParse(Option(name),out var value) ? value : fallback;
static int Usage() { Console.WriteLine(AppLocalization.Text("itoguruma register|agents|unregister|delete-agent-history|send|inbox|ack|history|inspect-change-request|hook|auth|project|version [options]\nSet ITOGURUMA_DB or pass --db <path>.", "itoguruma register|agents|unregister|delete-agent-history|send|inbox|ack|history|inspect-change-request|hook|auth|project|version [options]\nITOGURUMA_DBを設定するか、--db <path>を指定してください。")); return 0; }

async Task<AgentHistoryDeleteResult> RunDeleteAgentHistoryAsync(MessagingService messagingService)
{
    var dryRun = arguments.Contains("--dry-run", StringComparer.Ordinal);
    if (!dryRun) new HumanConfirmation(new SystemHumanConfirmationConsole()).Require();
    return await messagingService.DeleteAgentHistoryAsync(Required("--agent"), dryRun);
}

async Task<int> RunProjectAsync(MessagingService messagingService)
{
    if (arguments.Count < 2) throw new ArgumentException("Missing project operation.");
    var operation = arguments[1];
    var projectId = arguments.Count > 2 ? arguments[2] : null;
    object result;
    if (operation is "list") result = await messagingService.ListProjectsAsync();
    else if (operation is "show") result = await messagingService.GetProjectAsync(projectId ?? throw new ArgumentException("Missing project-id."))
        ?? throw new ProjectOperationException(ProjectErrorCodes.UnknownProject, $"{ProjectErrorCodes.UnknownProject}: Unknown project.");
    else
    {
        new HumanConfirmation(new SystemHumanConfirmationConsole()).Require();
        var id = projectId ?? throw new ArgumentException("Missing project-id.");
        result = operation switch
        {
            "add" => await messagingService.AddProjectAsync(new(id, Option("--display-name"), Required("--inbox-agent"))),
            "update" => await messagingService.UpdateProjectAsync(new(id, Option("--display-name"), Option("--inbox-agent"))),
            "enable" => await messagingService.SetProjectEnabledAsync(id, true),
            "disable" => await messagingService.SetProjectEnabledAsync(id, false),
            "delete" => new { deleted = await messagingService.DeleteProjectAsync(id) },
            _ => throw new ArgumentException($"Unknown project operation: {operation}")
        };
    }
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return 0;
}

async Task<int> RunHookAsync(MessagingService messagingService, string agentId)
{
    var input = await Console.In.ReadToEndAsync();
    var eventName = ParseEventName(input);
    var messages = await messagingService.GetMessagesAsync(agentId, Number("--limit",50),
        TimeSpan.FromSeconds(Number("--lease-seconds",300)), Option("--thread"), Option("--message-type"));
    if (messages.Count == 0) return 0;
    var context = AppLocalization.Text("Itoguruma inbox messages:\n", "Itoguruma受信メッセージ:\n") + JsonSerializer.Serialize(messages,
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
