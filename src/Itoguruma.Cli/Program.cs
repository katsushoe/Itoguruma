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
        return new AuthCommand(new WindowsCredentialTokenStore(), Console.In, Console.Out, Console.Error)
            .Run(arguments.Skip(1).ToList());
    }
    if (arguments[0] is "configure" or "unconfigure")
    {
        if (arguments.Count == 2 && (arguments[1] is "-h" or "--help"))
        {
            Console.WriteLine("itoguruma configure|unconfigure codex|claude [--url <server-url>] [--proxy-path <path>]");
            return 0;
        }
        if (arguments.Count < 2) throw new ArgumentException("Specify a client: codex or claude.");
        var isConfigure = arguments[0] == "configure";
        ValidateConfigurationArguments(arguments.Skip(2), isConfigure);
        var proxyPath = isConfigure ? Option("--proxy-path") ?? ResolveProxyPath() : string.Empty;
        if (isConfigure && !File.Exists(proxyPath))
            throw new FileNotFoundException("The Itoguruma MCP proxy was not found. Pass --proxy-path.", proxyPath);
        var configurationResult = new ClientConfigurationService().Run(arguments[0], arguments[1],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.ProcessPath ?? throw new InvalidOperationException("The CLI executable path is unavailable."),
            isConfigure ? Path.GetFullPath(proxyPath) : string.Empty,
            isConfigure ? ResolveEndpoint(Option("--url")) : string.Empty);
        Console.WriteLine($"{configurationResult.Operation} {configurationResult.Client}: updated {configurationResult.FilesChanged} user configuration file(s).");
        return 0;
    }
    if (arguments.Contains("--db", StringComparer.Ordinal))
        throw new ArgumentException("Business data is managed by the Itoguruma server; --db is no longer supported.");

    await using var service = await MessagingMcpClient.ConnectAsync(
        ResolveEndpoint(Option("--url")),
        Option("--credential-target"),
        loggerFactory);
    if (arguments[0] == "project") return await RunProjectAsync(service);
    if (arguments[0] == "hook") return await RunHookAsync(service);

    var result = arguments[0] switch
    {
        "register" => await service.CallAsync("register_agent", Values(
            ("agent_id", Required("--agent")), ("agent_type", Required("--type")),
            ("project_id", Required("--project")), ("name", Option("--name")),
            ("session_id", Option("--session")), ("metadata_json", Option("--metadata")))),
        "agents" => await service.CallAsync("list_agents", Values()),
        "unregister" => await service.CallAsync("unregister_agent", Values(("agent_id", Required("--agent")))),
        "delete-agent-history" => await RunDeleteAgentHistoryAsync(service),
        "send" => await service.CallAsync("send_message", Values(
            ("sender_agent_id", Required("--from")), ("recipients", RequiredMany("--to")),
            ("body", Required("--body")), ("thread_id", Required("--thread")),
            ("provider", Required("--provider")), ("reply_to_message_id", Option("--reply-to")),
            ("message_type", Option("--message-type") ?? "message"),
            ("payload_json", Option("--payload-json")), ("idempotency_key", Option("--idempotency-key")))),
        "inbox" => await service.CallAsync("get_messages", Values(
            ("agent_id", Required("--agent")), ("limit", Number("--limit", 50)),
            ("lease_seconds", Number("--lease-seconds", 300)), ("thread_id", Option("--thread")),
            ("message_type", Option("--message-type")), ("consumer_agent_id", Required("--consumer-agent")))),
        "ack" => await service.CallAsync("ack_message", Values(
            ("agent_id", Required("--agent")), ("consumer_agent_id", Required("--consumer-agent")),
            ("message_id", Required("--message")), ("lease_id", Required("--lease-id")))),
        "history" => await service.CallAsync("get_conversation_history", Values(
            ("thread_id", Required("--thread")), ("limit", Number("--limit", 100)),
            ("offset", Number("--offset", 0)))),
        "inspect-change-request" => await service.CallAsync("inspect_change_request", Values(
            ("payload_json", Required("--payload-json")))),
        _ => throw new ArgumentException(AppLocalization.Text($"Unknown command: {arguments[0]}", $"不明なコマンドです: {arguments[0]}"))
    };
    WriteJson(result);
    return 0;
}
catch (RemoteToolException ex)
{
    logger.LogError("[RemoteToolFailure] The Itoguruma server rejected the CLI operation {ToolName} ({ErrorCode}).",
        ex.ToolName, StringProperty(ex.Details, "errorCode") ?? "unknown");
    WriteRemoteError(ex);
    return 2;
}
catch (Exception ex) when (arguments.Count > 0 && arguments[0] == "hook" && IsServerUnreachable(ex))
{
    // A stopped server must not block the client: exit code 2 stops a Claude Code prompt or a Codex turn.
    // Undelivered messages stay in the database and are leased by the next hook run.
    logger.LogWarning("[HookServerUnavailable] The Itoguruma server could not be reached; the hook skipped message delivery.");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "[CommandFailure] The CLI command failed.");
    Console.Error.WriteLine(AppLocalization.Text(
        "Itoguruma could not complete the command. See the error log output for details.",
        "Itogurumaはコマンドを完了できませんでした。詳細はエラーログ出力を確認してください。"));
    return 2;
}

string? Option(string name)
{
    var index = arguments.IndexOf(name);
    return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
}

string ResolveProxyPath()
{
    var proxyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "mcp-proxy",
        ProductInfo.Version, "Itoguruma.McpProxy.exe"));
    return proxyPath;
}

static void ValidateConfigurationArguments(IEnumerable<string> options, bool isConfigure)
{
    var allowed = isConfigure ? new HashSet<string>(["--url", "--proxy-path"], StringComparer.Ordinal) : [];
    var seen = new HashSet<string>(StringComparer.Ordinal);
    using var enumerator = options.GetEnumerator();
    while (enumerator.MoveNext())
    {
        var option = enumerator.Current;
        if (!allowed.Contains(option)) throw new ArgumentException($"Unknown configuration option: {option}");
        if (!seen.Add(option)) throw new ArgumentException($"Configuration option was specified more than once: {option}");
        if (!enumerator.MoveNext() || string.IsNullOrWhiteSpace(enumerator.Current)
            || enumerator.Current.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Missing value for configuration option: {option}");
    }
}

string Required(string name) => Option(name)
    ?? throw new ArgumentException(AppLocalization.Text($"Missing option: {name}", $"必須オプションがありません: {name}"));

IReadOnlyList<string> RequiredMany(string name)
{
    var values = arguments.Select((value, index) => (value, index))
        .Where(item => item.value == name && item.index + 1 < arguments.Count)
        .Select(item => arguments[item.index + 1]).ToArray();
    return values.Length > 0
        ? values
        : throw new ArgumentException(AppLocalization.Text($"Missing option: {name}", $"必須オプションがありません: {name}"));
}

int Number(string name, int fallback) => int.TryParse(Option(name), out var value) ? value : fallback;

static int Usage()
{
    Console.WriteLine(AppLocalization.Text(
        "itoguruma configure|unconfigure codex|claude [--url <server-url>] [--proxy-path <path>] | register|agents|unregister|delete-agent-history|send|inbox|ack|history|inspect-change-request|hook|auth|project|version [options]\nBusiness commands connect to the running Itoguruma server. Configure Itoguruma:ServerUrl in the shared appsettings.json or pass --url <server-url>.",
        "itoguruma configure|unconfigure codex|claude [--url <server-url>] [--proxy-path <path>] | register|agents|unregister|delete-agent-history|send|inbox|ack|history|inspect-change-request|hook|auth|project|version [options]\n業務コマンドは起動中のItogurumaサーバーへ接続します。共有appsettings.jsonにItoguruma:ServerUrlを設定するか、--url <server-url>を指定してください。"));
    return 0;
}

async Task<JsonElement> RunDeleteAgentHistoryAsync(MessagingMcpClient client)
{
    var dryRun = arguments.Contains("--dry-run", StringComparer.Ordinal);
    if (!dryRun) new HumanConfirmation(new SystemHumanConfirmationConsole()).Require();
    return await client.CallAsync("delete_agent_history", Values(("agent_id", Required("--agent")), ("dry_run", dryRun)));
}

async Task<int> RunProjectAsync(MessagingMcpClient client)
{
    if (arguments.Count < 2) throw new ArgumentException("Missing project operation.");
    var operation = arguments[1];
    var projectId = arguments.Count > 2 ? arguments[2] : null;
    JsonElement result;
    if (operation is "list") result = await client.CallAsync("list_projects", Values());
    else if (operation is "show") result = await client.CallAsync("get_project", Values(
        ("project_id", projectId ?? throw new ArgumentException("Missing project-id."))));
    else
    {
        new HumanConfirmation(new SystemHumanConfirmationConsole()).Require();
        var id = projectId ?? throw new ArgumentException("Missing project-id.");
        result = operation switch
        {
            "add" => await client.CallAsync("add_project", Values(("project_id", id),
                ("display_name", Option("--display-name")), ("inbox_agent_id", Required("--inbox-agent")))),
            "update" => await client.CallAsync("update_project", Values(("project_id", id),
                ("display_name", Option("--display-name")), ("inbox_agent_id", Option("--inbox-agent")))),
            "enable" => await client.CallAsync("set_project_enabled", Values(("project_id", id), ("enabled", true))),
            "disable" => await client.CallAsync("set_project_enabled", Values(("project_id", id), ("enabled", false))),
            "delete" => await client.CallAsync("delete_project", Values(("project_id", id))),
            _ => throw new ArgumentException($"Unknown project operation: {operation}")
        };
    }
    WriteJson(result);
    return 0;
}

async Task<int> RunHookAsync(MessagingMcpClient client)
{
    var input = await Console.In.ReadToEndAsync();
    var eventName = ReadHookInputString(input, "hook_event_name");
    var projectInbox = arguments.Contains("--project-inbox", StringComparer.Ordinal);
    if (projectInbox == (Option("--agent") is not null))
        throw new ArgumentException(AppLocalization.Text("Specify exactly one of --agent or --project-inbox.",
            "--agentと--project-inboxのどちらか一方だけを指定してください。"));
    var parameters = projectInbox
        ? Values(("consumer_agent_id", Required("--consumer-agent")),
            ("working_directory", ReadHookInputString(input, "cwd") ?? Environment.CurrentDirectory),
            ("hook_event_name", eventName), ("limit", Number("--limit", 50)),
            ("lease_seconds", Number("--lease-seconds", 300)), ("thread_id", Option("--thread")),
            ("message_type", Option("--message-type")))
        : Values(("consumer_agent_id", Required("--consumer-agent")), ("agent_id", Required("--agent")),
            ("hook_event_name", eventName), ("limit", Number("--limit", 50)),
            ("lease_seconds", Number("--lease-seconds", 300)), ("thread_id", Option("--thread")),
            ("message_type", Option("--message-type")));
    var result = await client.CallAsync("get_hook_context", parameters);
    var context = result.TryGetProperty("context", out var contextElement) && contextElement.ValueKind == JsonValueKind.String
        ? contextElement.GetString()
        : null;
    if (context is null) return 0;
    var shouldStop = result.TryGetProperty("shouldStop", out var stopElement) && stopElement.ValueKind == JsonValueKind.True;
    if (shouldStop)
    {
        Console.Error.WriteLine(context);
        return 2;
    }
    Console.WriteLine(context);
    return 0;
}

static bool IsServerUnreachable(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is System.Net.Sockets.SocketException) return true;
        if (current is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError }) return true;
        if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(IsServerUnreachable)) return true;
    }
    return false;
}

static IReadOnlyDictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
    values.Where(value => value.Value is not null)
        .ToDictionary(value => value.Name, value => (object?)value.Value, StringComparer.Ordinal);

static void WriteJson(JsonElement value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

static void WriteRemoteError(RemoteToolException exception)
{
    if (exception.Details.ValueKind != JsonValueKind.Object)
    {
        Console.Error.WriteLine(exception.Message);
        return;
    }
    var details = exception.Details;
    var errorCode = StringProperty(details, "errorCode");
    var summary = StringProperty(details, "summary") ?? StringProperty(details, "message") ?? exception.Message;
    var attemptedRecipient = StringProperty(details, "attemptedRecipient") ?? StringProperty(details, "attemptedProjectId");
    object candidates = details.TryGetProperty("candidates", out var candidatesElement)
        ? candidatesElement.Clone()
        : Array.Empty<object>();
    var suggestedAction = StringProperty(details, "suggestedAction");
    if (errorCode is null)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(details, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return;
    }
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        error_code = errorCode,
        summary,
        attempted_recipient = attemptedRecipient,
        candidates,
        suggested_action = suggestedAction
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

static string? StringProperty(JsonElement element, string name) =>
    element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;

static string ResolveEndpoint(string? commandLineUrl)
{
    var serverUrl = commandLineUrl
        ?? Environment.GetEnvironmentVariable("Itoguruma__ServerUrl")
        ?? Environment.GetEnvironmentVariable("ITOGURUMA_SERVER_URL")
        ?? ReadConfiguredServerUrl();
    if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)
        || !uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp || uri.Query.Length > 0 || uri.Fragment.Length > 0
        || uri.AbsolutePath is not ("/" or "/mcp"))
        throw new InvalidOperationException("Itoguruma:ServerUrl must be an HTTP loopback origin or its /mcp endpoint.");
    var builder = new UriBuilder(uri) { Path = "/mcp", Query = string.Empty, Fragment = string.Empty };
    return builder.Uri.AbsoluteUri;
}

static string ReadConfiguredServerUrl()
{
    var configDirectory = Environment.GetEnvironmentVariable("ITOGURUMA_CONFIG_DIR");
    var candidates = new List<string>();
    if (!string.IsNullOrWhiteSpace(configDirectory)) candidates.Add(Path.Combine(configDirectory, "appsettings.json"));
    candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "config", "appsettings.json")));
    candidates.Add(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
    candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
        "Itoguruma.Server", "appsettings.json")));
    foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(path)) continue;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("Itoguruma", out var section)
            && section.TryGetProperty("ServerUrl", out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
    }
    throw new InvalidOperationException("Itoguruma:ServerUrl was not found in appsettings.json. Pass --url or set Itoguruma__ServerUrl.");
}

static string? ReadHookInputString(string input, string propertyName)
{
    if (string.IsNullOrWhiteSpace(input)) return null;
    try
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    catch (JsonException) { return null; }
}
