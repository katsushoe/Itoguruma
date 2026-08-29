using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Itoguruma.Core;
using Itoguruma.Server;
using ModelContextProtocol.Server;

StartupExceptionReporter.Register();
try
{
    return await RunServerAsync(args);
}
catch (Exception exception)
{
    StartupExceptionReporter.Report(exception);
    return 1;
}

static async Task<int> RunServerAsync(string[] args)
{
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
    var singleInstanceWaitSecondsText = Environment.GetEnvironmentVariable("ITOGURUMA_SINGLE_INSTANCE_WAIT_SECONDS")
        ?? builder.Configuration["Itoguruma:SingleInstanceWaitSeconds"]
        ?? "5";
    if (!int.TryParse(singleInstanceWaitSecondsText, out var singleInstanceWaitSeconds)
        || singleInstanceWaitSeconds is < 0 or > 60)
    {
        throw new InvalidOperationException("Itoguruma:SingleInstanceWaitSeconds must be between 0 and 60.");
    }
    var authenticationToken = Environment.GetEnvironmentVariable("ITOGURUMA_AUTH_TOKEN")
        ?? builder.Configuration["Itoguruma:AuthenticationToken"];
    if (string.IsNullOrWhiteSpace(authenticationToken))
    {
        throw new InvalidOperationException("Itoguruma:AuthenticationToken is required.");
    }

    var singleInstanceWait = TimeSpan.FromSeconds(singleInstanceWaitSeconds);
    using var endpointInstance = await AcquireMutexAsync(
        ServerSingleInstance.ForEndpoint(serverUrl), singleInstanceWait);
    if (endpointInstance is null) return 1;

    using var databaseInstance = await AcquireMutexAsync(
        ServerSingleInstance.ForDatabase(databasePath), singleInstanceWait);
    if (databaseInstance is null) return 1;

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
            options.ServerInstructions = ItogurumaPrompts.ServerInstructions;
        })
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<ItogurumaTools>()
        .WithPrompts<ItogurumaPrompts>();

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
}

static bool IsAllowedOrigin(string? origin)
{
    if (string.IsNullOrWhiteSpace(origin)) return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
}

static async Task<Mutex?> AcquireMutexAsync(string name, TimeSpan timeout)
{
    var stopwatch = Stopwatch.StartNew();
    while (true)
    {
        var instance = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew) return instance;
        instance.Dispose();
        if (stopwatch.Elapsed >= timeout) return null;
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }
}

internal static class StartupExceptionReporter
{
    private static readonly TextWriter StandardError = Console.Error;
    private static readonly object SyncRoot = new();

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception) Report(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Report(eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    public static void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = $"{DateTimeOffset.Now:O} [E] [FatalStartup] {exception} Program.cs（{exception.TargetSite?.Name ?? "unknown"}）";

        lock (SyncRoot)
        {
            TryWriteStandardError(message);
            TryWriteFallbackLog(message);
        }
    }

    private static void TryWriteStandardError(string message)
    {
        try
        {
            StandardError.WriteLine(message);
            StandardError.Flush();
        }
        catch (Exception)
        {
            // The fallback file remains available when standard error cannot be written.
        }
    }

    private static void TryWriteFallbackLog(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Itoguruma",
                "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"itoguruma-server-fatal-{DateTimeOffset.Now:yyyyMMdd}.log");
            File.AppendAllText(path, message + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Reporting must not replace the original startup failure with another exception.
        }
    }
}
