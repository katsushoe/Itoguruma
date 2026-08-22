using System.Security.Cryptography;
using System.Text;
using Itoguruma.Core;
using Itoguruma.Server;
using ModelContextProtocol.Server;

AppLocalization.ConfigureFromEnvironment();

var configDirectory = Environment.GetEnvironmentVariable("ITOGURUMA_CONFIG_DIR")
    ?? AppContext.BaseDirectory;
var logDirectory = Environment.GetEnvironmentVariable("ITOGURUMA_LOG_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, $"itoguruma-server-{DateTimeOffset.Now:yyyyMMdd}.log");
using var logStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
using var logWriter = new StreamWriter(logStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true
};
var synchronizedLogWriter = TextWriter.Synchronized(logWriter);
Console.SetOut(synchronizedLogWriter);
Console.SetError(synchronizedLogWriter);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = configDirectory
});
var serverUrl = Environment.GetEnvironmentVariable("ITOGURUMA_URL")
    ?? builder.Configuration["Itoguruma:ServerUrl"]
    ?? throw new InvalidOperationException("Itoguruma:ServerUrl is required.");
if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri)
    || !serverUri.IsLoopback
    || serverUri.Scheme != Uri.UriSchemeHttp
    || serverUri.AbsolutePath != "/")
{
    throw new InvalidOperationException("Itoguruma:ServerUrl must be an HTTP loopback origin without a path.");
}
var databasePath = Environment.GetEnvironmentVariable("ITOGURUMA_DB")
    ?? builder.Configuration["Itoguruma:DatabasePath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itoguruma", "messages.db");
var authenticationToken = Environment.GetEnvironmentVariable("ITOGURUMA_AUTH_TOKEN")
    ?? builder.Configuration["Itoguruma:AuthenticationToken"];
if (string.IsNullOrWhiteSpace(authenticationToken))
{
    throw new InvalidOperationException("Itoguruma:AuthenticationToken is required.");
}

using var endpointInstance = new Mutex(
    initiallyOwned: true,
    ServerSingleInstance.ForEndpoint(serverUrl),
    out var endpointCreatedNew);
if (!endpointCreatedNew) return 1;

using var databaseInstance = new Mutex(
    initiallyOwned: true,
    ServerSingleInstance.ForDatabase(databasePath),
    out var databaseCreatedNew);
if (!databaseCreatedNew) return 1;

builder.WebHost.UseUrls(serverUrl);
var crRoot = Environment.GetEnvironmentVariable("ITOGURUMA_CR_ROOT")
    ?? builder.Configuration["Itoguruma:CrRoot"];
var changeRequestValidator = string.IsNullOrWhiteSpace(crRoot) ? null : new ChangeRequestValidator(crRoot);
var messagingService = new MessagingService(new SqliteMessageStore(databasePath), changeRequestValidator);
await messagingService.InitializeAsync();
builder.Services.AddSingleton(messagingService);
builder.Services.AddSingleton<IUserTokenStore, UserEnvironmentTokenStore>();
builder.Services.AddSingleton<AuthenticationTokenService>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "itoguruma", Version = ProductInfo.Version };
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<ItogurumaTools>();

var app = builder.Build();
app.Logger.LogInformation(AppLocalization.Text("[Startup] Itoguruma server starting at {ServerUrl}", "[起動] Itogurumaサーバーを開始します: {ServerUrl}"), serverUrl);
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/mcp"))
    {
        await next(context);
        return;
    }

    if (!IsAllowedOrigin(context.Request.Headers.Origin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    var suppliedToken = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authorization["Bearer ".Length..]
        : string.Empty;
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(suppliedToken),
            Encoding.UTF8.GetBytes(authenticationToken)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = ProductInfo.Version }));
app.MapMcp("/mcp");
await app.RunAsync();
return 0;

static bool IsAllowedOrigin(string? origin)
{
    if (string.IsNullOrWhiteSpace(origin)) return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
}
