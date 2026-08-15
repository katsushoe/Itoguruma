using System.Diagnostics;
using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ProcessIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "itoguruma-process-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task McpServer_WhenToolsAreCalled_CompletesMessageLifecycle()
    {
        var databasePath = Path.Combine(_directory, "mcp.db");
        var requests = new[]
        {
            Request(1, "initialize", new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "itoguruma-tests", version = "1.0" }
            }),
            ToolRequest(2, "register_agent", new { agent_id = "sender", agent_type = "test" }),
            ToolRequest(3, "register_agent", new { agent_id = "recipient", agent_type = "test" }),
            ToolRequest(4, "send_message", new
            {
                sender_agent_id = "sender",
                recipient = "recipient",
                body = "integration message",
                thread_id = "integration"
            }),
            ToolRequest(5, "get_messages", new { agent_id = "recipient" })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5, result.Output.Count);
        Assert.All(result.Output, response => Assert.False(response.RootElement.TryGetProperty("error", out _)));
        Assert.Equal("0.3.2", result.Output[0].RootElement.GetProperty("result")
            .GetProperty("serverInfo").GetProperty("version").GetString());
        var messages = StructuredData(result.Output[4]);
        var message = Assert.Single(messages.EnumerateArray());
        Assert.Equal("integration message", message.GetProperty("body").GetString());
        var messageId = message.GetProperty("messageId").GetString();

        var acknowledgement = await RunMcpAsync(
        [
            ToolRequest(6, "ack_message", new { agent_id = "recipient", message_id = messageId }),
            ToolRequest(7, "get_messages", new { agent_id = "recipient" })
        ], databasePath);

        Assert.True(StructuredData(acknowledgement.Output[0]).GetProperty("acked").GetBoolean());
        Assert.Empty(StructuredData(acknowledgement.Output[1]).EnumerateArray());
    }

    [Fact]
    public async Task McpServer_WhenMessageReferencesUnknownAgent_ReturnsAiFriendlyJsonError()
    {
        var databasePath = Path.Combine(_directory, "mcp-error.db");
        var requests = new[]
        {
            ToolRequest(1, "register_agent", new { agent_id = "sender", agent_type = "test" }),
            ToolRequest(2, "send_message", new
            {
                sender_agent_id = "sender",
                recipient = "missing-recipient",
                body = "integration message",
                thread_id = "integration",
                idempotency_key = "integration-unknown-recipient"
            })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        var toolResult = result.Output[1].RootElement.GetProperty("result");
        Assert.True(toolResult.TryGetProperty("isError", out var isError), toolResult.GetRawText());
        Assert.True(isError.GetBoolean());
        using var errorDocument = JsonDocument.Parse(
            toolResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        var data = errorDocument.RootElement;
        Assert.Equal("reference_not_found", data.GetProperty("errorCode").GetString());
        Assert.Equal("sqlite/table/write/reference_key", data.GetProperty("category").GetString());
        Assert.Contains("Register every sender and recipient agent",
            data.GetProperty("suggestedAction").GetString(), StringComparison.Ordinal);
        Assert.True(data.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task McpServer_WhenToolsAreListed_DescribesErrorCategoryCatalog()
    {
        var result = await RunMcpAsync(
            [Request(1, "tools/list", new { })], Path.Combine(_directory, "mcp-tools.db"));

        Assert.Equal(0, result.ExitCode);
        var tools = result.Output[0].RootElement.GetProperty("result").GetProperty("tools");
        var sendMessage = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "send_message");
        var description = sendMessage.GetProperty("description").GetString();
        Assert.Contains("| Category | Meaning | Recommended response |", description, StringComparison.Ordinal);
        Assert.Contains("`sqlite/table/write/reference_key`", description, StringComparison.Ordinal);
        Assert.Contains("`internal`", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpServer_WhenVersionIsRequested_ReturnsRunningProductVersion()
    {
        var result = await RunMcpAsync(
            [ToolRequest(1, "get_version", new { })], Path.Combine(_directory, "mcp-version.db"));

        Assert.Equal(0, result.ExitCode);
        var version = StructuredData(result.Output[0]);
        Assert.Equal("itoguruma", version.GetProperty("name").GetString());
        Assert.Equal("0.3.2", version.GetProperty("version").GetString());
    }

    [Fact]
    public async Task McpServer_WhenAuthenticationIsMissing_ReturnsUnauthorized()
    {
        await using var server = await StartMcpServerAsync(Path.Combine(_directory, "mcp-auth.db"));
        using var client = new HttpClient { BaseAddress = new Uri(server.ServerUrl) };
        using var content = new StringContent(ToolRequest(1, "get_version", new { }));

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpServer_WhenOriginIsNotLoopback_ReturnsForbidden()
    {
        await using var server = await StartMcpServerAsync(Path.Combine(_directory, "mcp-origin.db"));
        using var client = new HttpClient { BaseAddress = new Uri(server.ServerUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthenticationToken);
        client.DefaultRequestHeaders.Add("Origin", "https://example.com");
        using var content = new StringContent(ToolRequest(1, "get_version", new { }));

        using var response = await client.PostAsync("/mcp", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AgentHook_WhenPromptAndStopEventsOccur_UsesExpectedOutputAndExitCode()
    {
        var databasePath = Path.Combine(_directory, "hook.db");
        var service = new MessagingService(new SqliteMessageStore(databasePath));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("sender", "test");
        await service.RegisterAgentAsync("recipient", "test");
        await service.SendMessageAsync(new("sender", ["recipient"], "prompt message", "hook"));

        var prompt = await RunAsync("itoguruma", ["hook", "--agent", "recipient"], databasePath,
            "{\"hook_event_name\":\"UserPromptSubmit\"}");

        Assert.Equal(0, prompt.ExitCode);
        Assert.Contains("prompt message", prompt.StandardOutput, StringComparison.Ordinal);
        await service.SendMessageAsync(new("sender", ["recipient"], "stop message", "hook"));

        var stop = await RunAsync("itoguruma", ["hook", "--agent", "recipient", "--lease-seconds", "-1"],
            databasePath, "{\"hook_event_name\":\"Stop\"}");

        Assert.Equal(2, stop.ExitCode);
        Assert.Contains("stop message", stop.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentCli_WhenProcessesSendConcurrently_PersistsEveryMessage()
    {
        const int messageCount = 12;
        var databasePath = Path.Combine(_directory, "process-stress.db");
        var service = new MessagingService(new SqliteMessageStore(databasePath));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("sender", "test");
        await service.RegisterAgentAsync("recipient", "test");

        var sends = await Task.WhenAll(Enumerable.Range(0, messageCount).Select(index =>
            RunAsync("itoguruma",
            [
                "send", "--from", "sender", "--to", "recipient", "--thread", "process-stress",
                "--body", $"message-{index}", "--idempotency-key", $"process-{index}"
            ], databasePath, string.Empty)));

        Assert.All(sends, result => Assert.Equal(0, result.ExitCode));
        var messageIds = sends.Select(result =>
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            return document.RootElement.GetProperty("message_id").GetString();
        });
        Assert.Equal(messageCount, messageIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(messageCount, (await service.GetMessagesAsync("recipient", limit: messageCount)).Count);
    }

    [Fact]
    public async Task AgentCli_WhenVersionIsRequested_ReturnsRunningProductVersion()
    {
        var result = await RunAsync("itoguruma", ["version"], Path.Combine(_directory, "version.db"), string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("itoguruma 0.3.2", result.StandardOutput.Trim());
        Assert.False(File.Exists(Path.Combine(_directory, "version.db")));
    }

    [Fact]
    public async Task McpServer_WhenDatabaseIsAlreadyInUse_SecondProcessExits()
    {
        var databasePath = Path.Combine(_directory, "single-instance.db");
        await using var first = await StartMcpServerAsync(databasePath);
        var secondStartInfo = CreateMcpServerStartInfo(databasePath, first.ServerUrl, first.AuthenticationToken);
        using var second = Process.Start(secondStartInfo) ?? throw new InvalidOperationException("Process did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await second.WaitForExitAsync(timeout.Token);

        Assert.Equal(1, second.ExitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static string Request(int id, string method, object parameters) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

    private static string ToolRequest(int id, string name, object arguments) =>
        Request(id, "tools/call", new { name, arguments });

    private static JsonElement StructuredData(JsonDocument response)
    {
        return response.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("data");
    }

    private async Task<ProcessResult> RunMcpAsync(IReadOnlyList<string> requests, string databasePath)
    {
        await using var server = await StartMcpServerAsync(databasePath);
        using var client = new HttpClient { BaseAddress = new Uri(server.ServerUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthenticationToken);
        var output = new List<JsonDocument>();
        foreach (var request in requests)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            message.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
            message.Content = new StringContent(request, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(message);
            response.EnsureSuccessStatusCode();
            output.Add(ParseMcpResponse(await response.Content.ReadAsStringAsync()));
        }

        return new(0, string.Empty, string.Empty, output);
    }

    private async Task<RunningMcpServer> StartMcpServerAsync(string databasePath)
    {
        Directory.CreateDirectory(_directory);
        var serverUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var authenticationToken = Guid.NewGuid().ToString("N");
        var startInfo = CreateMcpServerStartInfo(databasePath, serverUrl, authenticationToken);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var server = new RunningMcpServer(process, standardOutput, standardError, serverUrl, authenticationToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(serverUrl) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                try
                {
                    using var response = await client.GetAsync("/health", timeout.Token);
                    if (response.StatusCode == HttpStatusCode.OK) return server;
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    private static ProcessStartInfo CreateMcpServerStartInfo(
        string databasePath,
        string serverUrl,
        string authenticationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(FindApplicationAssembly("Itoguruma.Server"));
        startInfo.Environment["ITOGURUMA_DB"] = databasePath;
        startInfo.Environment["ITOGURUMA_URL"] = serverUrl;
        startInfo.Environment["ITOGURUMA_AUTH_TOKEN"] = authenticationToken;
        return startInfo;
    }

    private static JsonDocument ParseMcpResponse(string responseBody)
    {
        const string dataPrefix = "data: ";
        var json = responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith(dataPrefix, StringComparison.Ordinal))?[dataPrefix.Length..]
            ?? responseBody;
        return JsonDocument.Parse(json);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<ProcessResult> RunAsync(
        string application,
        IReadOnlyList<string> arguments,
        string databasePath,
        string standardInput)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var assemblyPath = FindApplicationAssembly(application);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--db");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.Environment["ITOGURUMA_DB"] = databasePath;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process did not start.");
        await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        return new(process.ExitCode, await standardOutput, await standardError, []);
    }

    private static string FindApplicationAssembly(string application)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Build configuration was not found.");
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = application == "itoguruma"
            ? Path.Combine(repositoryRoot, "src", "Itoguruma.Cli")
            : Path.Combine(repositoryRoot, "src", application);
        var path = Path.Combine(projectDirectory, "bin", configuration, "net8.0", $"{application}.dll");
        return File.Exists(path) ? path : throw new FileNotFoundException("Application assembly was not found.", path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Itoguruma.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        IReadOnlyList<JsonDocument> Output);

    private sealed class RunningMcpServer(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        string serverUrl,
        string authenticationToken) : IAsyncDisposable
    {
        public string ServerUrl { get; } = serverUrl;
        public string AuthenticationToken { get; } = authenticationToken;

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            await standardOutput;
            await standardError;
            process.Dispose();
        }
    }
}
