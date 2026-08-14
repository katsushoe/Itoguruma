using System.Diagnostics;
using System.Text.Json;
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
            Request(1, "initialize", new { }),
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

        var result = await RunAsync("Itoguruma.Server", requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5, result.Output.Count);
        Assert.All(result.Output, response => Assert.False(response.RootElement.TryGetProperty("error", out _)));
        Assert.Equal("0.3.0", result.Output[0].RootElement.GetProperty("result")
            .GetProperty("serverInfo").GetProperty("version").GetString());
        var messages = StructuredData(result.Output[4]);
        var message = Assert.Single(messages.EnumerateArray());
        Assert.Equal("integration message", message.GetProperty("body").GetString());
        var messageId = message.GetProperty("messageId").GetString();

        var acknowledgement = await RunAsync("Itoguruma.Server",
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

        var result = await RunAsync("Itoguruma.Server", requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        var error = result.Output[1].RootElement.GetProperty("error");
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Contains("not registered", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        var data = error.GetProperty("data");
        Assert.Equal("reference_not_found", data.GetProperty("errorCode").GetString());
        Assert.Equal("sqlite/table/write/reference_key", data.GetProperty("category").GetString());
        Assert.Contains("Register every sender and recipient agent",
            data.GetProperty("suggestedAction").GetString(), StringComparison.Ordinal);
        Assert.True(data.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task McpServer_WhenToolsAreListed_DescribesErrorCategoryCatalog()
    {
        var result = await RunAsync("Itoguruma.Server",
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
    public async Task AgentCli_WhenVersionIsRequested_ReturnsProductVersion()
    {
        var result = await RunAsync("itoguruma", ["version"], Path.Combine(_directory, "version.db"), string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("itoguruma 0.3.0", result.StandardOutput.Trim());
        Assert.False(File.Exists(Path.Combine(_directory, "version.db")));
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

    private static async Task<ProcessResult> RunAsync(
        string application,
        IReadOnlyList<string> inputLines,
        string databasePath)
    {
        var result = await RunAsync(application, [], databasePath, string.Join(Environment.NewLine, inputLines));
        var output = result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        return result with { Output = output };
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
}
