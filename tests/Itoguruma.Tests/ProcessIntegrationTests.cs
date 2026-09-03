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
            ToolRequest(2, "register_project_inbox", new { project_id = "testproject", display_name = "Test Project" }),
            ToolRequest(3, "register_agent", new { agent_id = "sender", agent_type = "test", project_id = "testproject" }),
            ToolRequest(4, "register_agent", new { agent_id = "recipient", agent_type = "test", project_id = "testproject" }),
            ToolRequest(5, "send_message", new
            {
                sender_agent_id = "sender",
                recipient = "recipient",
                provider = "codex",
                body = "integration message",
                thread_id = "integration"
            }),
            ToolRequest(6, "get_messages", new { agent_id = "recipient" })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(6, result.Output.Count);
        Assert.All(result.Output, response => Assert.False(response.RootElement.TryGetProperty("error", out _)));
        Assert.Equal(ProductInfo.Version, result.Output[0].RootElement.GetProperty("result")
            .GetProperty("serverInfo").GetProperty("version").GetString());
        var messages = StructuredData(result.Output[5]);
        var message = Assert.Single(messages.EnumerateArray());
        Assert.Equal("integration message", message.GetProperty("body").GetString());
        Assert.Equal("codex", message.GetProperty("provider").GetString());
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
    public async Task McpServer_WhenConversationHistoryIsRequested_ReturnsMessagesInChronologicalOrder()
    {
        var databasePath = Path.Combine(_directory, "mcp-history.db");
        var requests = new[]
        {
            ToolRequest(0, "register_project_inbox", new { project_id = "testproject", display_name = "Test Project" }),
            ToolRequest(1, "register_agent", new { agent_id = "sender", agent_type = "test", project_id = "testproject" }),
            ToolRequest(2, "register_agent", new { agent_id = "recipient", agent_type = "test", project_id = "testproject" }),
            ToolRequest(3, "send_message", new
            {
                sender_agent_id = "sender", recipient = "recipient", provider = "codex",
                body = "first", thread_id = "history"
            }),
            ToolRequest(4, "send_message", new
            {
                sender_agent_id = "recipient", recipient = "sender", provider = "claude-code",
                body = "second", thread_id = "history"
            }),
            ToolRequest(5, "get_conversation_history", new { thread_id = "history" })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        var history = StructuredData(result.Output[5]).EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal("first", history[0].GetProperty("body").GetString());
        Assert.Equal("codex", history[0].GetProperty("provider").GetString());
        Assert.Equal("second", history[1].GetProperty("body").GetString());
        Assert.Equal("claude-code", history[1].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task McpServer_WhenConversationHistoryThreadDoesNotExist_ReturnsEmptyArray()
    {
        var databasePath = Path.Combine(_directory, "mcp-history-missing.db");

        var result = await RunMcpAsync(
            [ToolRequest(1, "get_conversation_history", new { thread_id = "missing-thread" })], databasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(StructuredData(result.Output[0]).EnumerateArray());
    }

    [Fact]
    public async Task McpServer_WhenAgentHistoryIsDeleted_SupportsDryRunAndUnregister()
    {
        var databasePath = Path.Combine(_directory, "mcp-agent-history.db");
        var result = await RunMcpAsync([
            ToolRequest(0, "register_project_inbox", new { project_id = "testproject", display_name = "Test Project" }),
            ToolRequest(1, "register_agent", new { agent_id = "target", agent_type = "test", project_id = "testproject" }),
            ToolRequest(2, "register_agent", new { agent_id = "other", agent_type = "test", project_id = "testproject" }),
            ToolRequest(3, "send_message", new
            {
                sender_agent_id = "target", recipient = "other", provider = "codex",
                body = "secret-body", thread_id = "delete-thread"
            }),
            ToolRequest(4, "delete_agent_history", new { agent_id = "target", dry_run = true }),
            ToolRequest(5, "delete_agent_history", new { agent_id = "target", dry_run = false }),
            ToolRequest(6, "unregister_agent", new { agent_id = "target" })
        ], databasePath);

        var preview = StructuredData(result.Output[4]);
        var deleted = StructuredData(result.Output[5]);
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, preview.GetProperty("messageCount").GetInt32());
        Assert.False(deleted.GetProperty("dryRun").GetBoolean());
        Assert.True(StructuredData(result.Output[6]).GetProperty("unregistered").GetBoolean());
        Assert.DoesNotContain("secret-body", result.Output[3].RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-body", result.Output[4].RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpServer_WhenMessageReferencesUnknownProject_AutoRegistersAndDelivers()
    {
        var databasePath = Path.Combine(_directory, "mcp-error.db");
        var requests = new[]
        {
            ToolRequest(0, "register_project_inbox", new { project_id = "testproject", display_name = "Test Project" }),
            ToolRequest(1, "register_agent", new { agent_id = "sender", agent_type = "test", project_id = "testproject" }),
            ToolRequest(2, "send_message", new
            {
                sender_agent_id = "sender",
                recipient = "missingrecipient",
                provider = "codex",
                body = "integration message",
                thread_id = "integration",
                idempotency_key = "integration-unknown-recipient"
            }),
            ToolRequest(3, "get_messages", new { agent_id = "missingrecipient" })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Output[2].RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        var message = Assert.Single(StructuredData(result.Output[3]).EnumerateArray());
        Assert.Equal("integration message", message.GetProperty("body").GetString());
    }

    [Fact]
    public async Task McpServer_WhenProjectIdIsInvalid_ReturnsRegisteredProjectCandidates()
    {
        var databasePath = Path.Combine(_directory, "mcp-invalid-project.db");
        var store = new SqliteMessageStore(databasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.AddProjectAsync(new("moyai", "Moyai", "project-inbox-moyai"));

        var result = await RunMcpAsync([
            ToolRequest(1, "send_message", new
            {
                sender_agent_id = "sender", recipient = "moyai-codex-root", provider = "codex",
                body = "blocked", thread_id = "invalid-project", idempotency_key = "invalid-project-1"
            })], databasePath);

        var toolResult = result.Output[0].RootElement.GetProperty("result");
        Assert.True(toolResult.GetProperty("isError").GetBoolean());
        using var errorDocument = JsonDocument.Parse(
            toolResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        var error = errorDocument.RootElement;
        Assert.Equal(ProjectErrorCodes.InvalidProjectId, error.GetProperty("errorCode").GetString());
        Assert.Equal("moyai-codex-root", error.GetProperty("attemptedRecipient").GetString());
        var candidate = Assert.Single(error.GetProperty("candidates").EnumerateArray());
        Assert.Equal("moyai", candidate.GetProperty("projectId").GetString());
        Assert.True(candidate.GetProperty("enabled").GetBoolean());
        Assert.Empty(await store.GetConversationHistoryAsync("invalid-project"));
    }

    [Fact]
    public async Task McpServer_WhenProjectsAreListed_ReturnsCanonicalRegistry()
    {
        var databasePath = Path.Combine(_directory, "mcp-list-projects.db");
        var store = new SqliteMessageStore(databasePath);
        await store.InitializeAsync();
        await store.AddProjectAsync(new("Moyai", "Moyai", "project-inbox-moyai"));

        var result = await RunMcpAsync([
            ToolRequest(1, "list_projects", new { })
        ], databasePath);

        var project = Assert.Single(StructuredData(result.Output[0]).EnumerateArray());
        Assert.Equal("moyai", project.GetProperty("projectId").GetString());
        Assert.Equal("project-inbox-moyai", project.GetProperty("inboxAgentId").GetString());
        Assert.True(project.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task McpServer_RegisterProjectInbox_ReflectsGithubieSelfTestInBothLists()
    {
        var databasePath = Path.Combine(_directory, "mcp-register-project-inbox.db");
        var result = await RunMcpAsync([
            ToolRequest(1, "register_project_inbox", new
                { project_id = "GithubieSelfTest", display_name = "Githubie Self Test" }),
            ToolRequest(2, "register_project_inbox", new
                { project_id = "githubieselftest", display_name = "Githubie Self Test" }),
            ToolRequest(3, "list_projects", new { }),
            ToolRequest(4, "list_agents", new { })
        ], databasePath);

        Assert.All(result.Output, response => Assert.False(response.RootElement.TryGetProperty("error", out _)));
        var project = Assert.Single(StructuredData(result.Output[2]).EnumerateArray());
        var agent = Assert.Single(StructuredData(result.Output[3]).EnumerateArray());
        Assert.Equal("githubieselftest", project.GetProperty("projectId").GetString());
        Assert.Equal("githubieselftest", agent.GetProperty("agentId").GetString());
        Assert.Equal("githubieselftest", agent.GetProperty("projectId").GetString());
        Assert.Equal("project_inbox", agent.GetProperty("agentType").GetString());
    }

    [Fact]
    public async Task McpServer_WhenProviderIsInvalid_ReturnsProviderErrorWithoutPersistingMessage()
    {
        var databasePath = Path.Combine(_directory, "mcp-provider-error.db");
        var store = new SqliteMessageStore(databasePath);
        await store.InitializeAsync();
        await store.RegisterAgentAsync("sender", "test");
        await store.RegisterAgentAsync("recipient", "test");

        var result = await RunMcpAsync([
            ToolRequest(1, "send_message", new
            {
                sender_agent_id = "sender", recipient = "recipient", provider = "unknown",
                body = "blocked", thread_id = "provider"
            })], databasePath);

        var toolResult = result.Output[0].RootElement.GetProperty("result");
        Assert.True(toolResult.GetProperty("isError").GetBoolean());
        using var errorDocument = JsonDocument.Parse(
            toolResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal("invalid_provider",
            errorDocument.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(await store.GetConversationHistoryAsync("provider"));
    }

    [Fact]
    public async Task McpServer_WhenMessageHasNoRecipient_ReturnsAiFriendlyJsonError()
    {
        var databasePath = Path.Combine(_directory, "mcp-no-recipient.db");
        var requests = new[]
        {
            ToolRequest(0, "register_project_inbox", new { project_id = "testproject", display_name = "Test Project" }),
            ToolRequest(1, "register_agent", new { agent_id = "sender", agent_type = "test", project_id = "testproject" }),
            ToolRequest(2, "send_message", new
            {
                sender_agent_id = "sender",
                provider = "codex",
                body = "integration message",
                thread_id = "integration"
            })
        };

        var result = await RunMcpAsync(requests, databasePath);

        Assert.Equal(0, result.ExitCode);
        var toolResult = result.Output[2].RootElement.GetProperty("result");
        Assert.True(toolResult.TryGetProperty("isError", out var isError), toolResult.GetRawText());
        Assert.True(isError.GetBoolean());
        using var errorDocument = JsonDocument.Parse(
            toolResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        var data = errorDocument.RootElement;
        Assert.Equal("invalid_argument", data.GetProperty("errorCode").GetString());
        Assert.Equal("validation/argument", data.GetProperty("category").GetString());
        Assert.Contains("recipient", data.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("`validation/argument`", description, StringComparison.Ordinal);
        Assert.Contains("`validation/provider`", description, StringComparison.Ordinal);
        Assert.Contains("`internal`", description, StringComparison.Ordinal);
        Assert.True(sendMessage.GetProperty("inputSchema").GetProperty("properties")
            .TryGetProperty("provider", out _));
        Assert.Contains(sendMessage.GetProperty("inputSchema").GetProperty("required").EnumerateArray(),
            item => item.GetString() == "provider");

        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.Contains("get_hook_context", names);
        Assert.Contains("get_auth_status", names);
        Assert.Contains("rotate_auth_token", names);
        Assert.Contains("delete_agent_history", names);
        Assert.Contains("list_projects", names);
        Assert.Contains("register_project_inbox", names);
        var registerAgent = tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "register_agent");
        Assert.Contains(registerAgent.GetProperty("inputSchema").GetProperty("required").EnumerateArray(),
            item => item.GetString() == "project_id");
        var deleteHistory = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "delete_agent_history");
        Assert.True(deleteHistory.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
        Assert.Contains(deleteHistory.GetProperty("inputSchema").GetProperty("required").EnumerateArray(),
            item => item.GetString() == "dry_run");
    }

    [Fact]
    public async Task McpServer_WhenInitializedAndPromptRequested_DescribesPurposeAndWorkflow()
    {
        var requests = new[]
        {
            Request(1, "initialize", new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "itoguruma-tests", version = "1.0" }
            }),
            Request(2, "prompts/list", new { }),
            Request(3, "prompts/get", new { name = "itoguruma_guide" })
        };

        var result = await RunMcpAsync(requests, Path.Combine(_directory, "mcp-prompts.db"));

        Assert.Equal(0, result.ExitCode);
        var instructions = result.Output[0].RootElement.GetProperty("result").GetProperty("instructions").GetString();
        Assert.Contains("message relay", instructions, StringComparison.Ordinal);
        Assert.Contains("ack_message", instructions, StringComparison.Ordinal);
        Assert.Contains("change requests", instructions, StringComparison.Ordinal);
        Assert.Contains("list_projects", instructions, StringComparison.Ordinal);
        Assert.Contains("^[a-z][a-z0-9]*$", instructions, StringComparison.Ordinal);

        var prompts = result.Output[1].RootElement.GetProperty("result").GetProperty("prompts");
        var guide = prompts.EnumerateArray().Single(prompt =>
            prompt.GetProperty("name").GetString() == "itoguruma_guide");
        Assert.Contains("standard agent-to-agent messaging workflow", guide.GetProperty("description").GetString(),
            StringComparison.Ordinal);

        var messages = result.Output[2].RootElement.GetProperty("result").GetProperty("messages");
        var text = messages[0].GetProperty("content").GetProperty("text").GetString();
        Assert.Contains("register_agent", text, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", text, StringComparison.Ordinal);
        Assert.Contains("message_type=change_request", text, StringComparison.Ordinal);
        Assert.Contains("list_projects", text, StringComparison.Ordinal);
        Assert.Contains("^[a-z][a-z0-9]*$", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpServer_WhenVersionIsRequested_ReturnsRunningProductVersion()
    {
        var result = await RunMcpAsync(
            [ToolRequest(1, "get_version", new { })], Path.Combine(_directory, "mcp-version.db"));

        Assert.Equal(0, result.ExitCode);
        var version = StructuredData(result.Output[0]);
        Assert.Equal("itoguruma", version.GetProperty("name").GetString());
        Assert.Equal(ProductInfo.Version, version.GetProperty("version").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+(?:\.\d+)?$", version.GetProperty("version").GetString());
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
    public async Task McpServer_WhenStarted_WritesLogToConfiguredDirectory()
    {
        var databasePath = Path.Combine(_directory, "mcp-log.db");
        await using var server = await StartMcpServerAsync(databasePath);

        var logFiles = Directory.GetFiles(
            Path.Combine(_directory, "logs"), "itoguruma-server-*.log", SearchOption.TopDirectoryOnly);

        Assert.Single(logFiles);
        Assert.NotEqual(0, new FileInfo(logFiles[0]).Length);
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
        await service.SendMessageAsync(new("sender", ["recipient"], "prompt message", "hook", "codex"));

        var prompt = await RunAsync("itoguruma", ["hook", "--agent", "recipient"], databasePath,
            "{\"hook_event_name\":\"UserPromptSubmit\"}");

        Assert.Equal(0, prompt.ExitCode);
        Assert.Contains("prompt message", prompt.StandardOutput, StringComparison.Ordinal);
        await service.SendMessageAsync(new("sender", ["recipient"], "stop message", "hook", "codex"));

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
                "--provider", "codex", "--body", $"message-{index}",
                "--idempotency-key", $"process-{index}"
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
    public async Task AgentCli_WhenMultipleRecipientsAndHistoryAreRequested_MatchesMcpCapabilities()
    {
        var databasePath = Path.Combine(_directory, "cli-symmetry.db");
        var service = new MessagingService(new SqliteMessageStore(databasePath));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("sender", "test");
        await service.RegisterAgentAsync("recipienta", "test");
        await service.RegisterAgentAsync("recipientb", "test");

        var send = await RunAsync("itoguruma",
        [
            "send", "--from", "sender", "--to", "recipienta", "--to", "recipientb",
            "--provider", "codex", "--thread", "symmetry", "--body", "shared message"
        ], databasePath, string.Empty);
        var history = await RunAsync("itoguruma",
            ["history", "--thread", "symmetry"], databasePath, string.Empty);

        Assert.Equal(0, send.ExitCode);
        Assert.Equal(0, history.ExitCode);
        Assert.Single(await service.GetMessagesAsync("recipienta"));
        Assert.Single(await service.GetMessagesAsync("recipientb"));
        using var document = JsonDocument.Parse(history.StandardOutput);
        var message = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("shared message", message.GetProperty("body").GetString());
        Assert.Equal("codex", message.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task AgentCli_WhenProjectIdIsInvalid_ReturnsRegisteredProjectCandidates()
    {
        var databasePath = Path.Combine(_directory, "cli-invalid-project.db");
        var service = new MessagingService(new SqliteMessageStore(databasePath));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("sender", "test");
        await service.AddProjectAsync(new("moyai", "Moyai", "project-inbox-moyai"));

        var result = await RunAsync("itoguruma",
        [
            "send", "--from", "sender", "--to", "moyai-codex-root",
            "--provider", "codex", "--thread", "invalid-project", "--body", "blocked"
        ], databasePath, string.Empty);

        Assert.Equal(2, result.ExitCode);
        var jsonStart = result.StandardError.IndexOf('{', StringComparison.Ordinal);
        Assert.True(jsonStart >= 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardError[jsonStart..]);
        Assert.Equal(ProjectErrorCodes.InvalidProjectId,
            document.RootElement.GetProperty("error_code").GetString());
        var candidate = Assert.Single(document.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal("moyai", candidate.GetProperty("projectId").GetString());
    }

    [Fact]
    public async Task AgentCli_WhenAgentHistoryDryRunIsRequested_ReturnsPreviewWithoutConfirmation()
    {
        var databasePath = Path.Combine(_directory, "cli-agent-history.db");
        var service = new MessagingService(new SqliteMessageStore(databasePath));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("target", "test");
        await service.RegisterAgentAsync("other", "test");
        await service.SendMessageAsync(new("target", ["other"], "keep", "cli-delete", "codex"));

        var result = await RunAsync("itoguruma",
            ["delete-agent-history", "--agent", "target", "--dry-run"], databasePath, string.Empty);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.True(document.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("messageCount").GetInt32());
        Assert.Single(await service.GetConversationHistoryAsync("cli-delete"));
    }

    [Fact]
    public async Task AgentCli_WhenProviderIsMissing_RejectsSend()
    {
        var result = await RunAsync("itoguruma",
            ["send", "--from", "sender", "--to", "recipient", "--thread", "missing", "--body", "blocked"],
            Path.Combine(_directory, "cli-provider-missing.db"), string.Empty);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--provider", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentCli_WhenDatabaseCannotBeOpened_HandlesExceptionAtApplicationBoundary()
    {
        var databasePath = Path.Combine(_directory, "database-directory");
        Directory.CreateDirectory(databasePath);

        var result = await RunAsync("itoguruma", ["agents"], databasePath, string.Empty);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("CommandFailure", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Itoguruma could not complete the command", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentCli_WhenVersionIsRequested_ReturnsRunningProductVersion()
    {
        var result = await RunAsync("itoguruma", ["version"], Path.Combine(_directory, "version.db"), string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"itoguruma {ProductInfo.Version}", result.StandardOutput.Trim());
        Assert.False(File.Exists(Path.Combine(_directory, "version.db")));
    }

    [Fact]
    public void Installer_WhenConfiguringClaude_UsesEnvironmentTokenAndFallbackPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Install-Itoguruma.ps1"));

        Assert.Contains("Authorization: Bearer ${ITOGURUMA_AUTH_TOKEN}", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization: Bearer $authenticationToken", installer, StringComparison.Ordinal);
        Assert.Contains("npm\\claude.cmd", installer, StringComparison.Ordinal);
        Assert.Contains(".local\\bin\\claude.exe", installer, StringComparison.Ordinal);

        foreach (var document in new[] { "MCP_SETUP.md", "MCP_SETUP.ja.md" })
        {
            var content = File.ReadAllText(Path.Combine(repositoryRoot, document));
            Assert.Contains("Authorization: Bearer ${ITOGURUMA_AUTH_TOKEN}", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Installer_WhenStoppingExistingServer_WaitsForCompleteProcessExit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Install-Itoguruma.ps1"));

        Assert.Contains("[int]$ServerStopTimeoutSeconds = 30", installer, StringComparison.Ordinal);
        Assert.Contains("$installedServers.Count -eq 0", installer, StringComparison.Ordinal);
        Assert.Contains("did not stop within $ServerStopTimeoutSeconds seconds", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Wait-Process -Timeout 10 -ErrorAction SilentlyContinue", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_WhenStartingServer_UsesCurrentConfiguredEnvironment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Install-Itoguruma.ps1"));

        Assert.Contains("$serverProcess = Start-Process", installer, StringComparison.Ordinal);
        Assert.Contains("-FilePath $serverPath", installer, StringComparison.Ordinal);
        Assert.Contains("if ($serverProcess.HasExited)", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-ScheduledTask -TaskName $taskName", installer, StringComparison.Ordinal);
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

    [Fact]
    public async Task McpServer_WhenLogDirectoryCannotBeCreated_HandlesExceptionAtApplicationBoundary()
    {
        var invalidLogDirectory = Path.Combine(_directory, "log-file");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(invalidLogDirectory, "not a directory");
        var startInfo = CreateMcpServerStartInfo(
            Path.Combine(_directory, "startup-failure.db"),
            $"http://127.0.0.1:{GetAvailablePort()}",
            Guid.NewGuid().ToString("N"));
        startInfo.Environment["ITOGURUMA_LOG_DIR"] = invalidLogDirectory;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process did not start.");
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await process.WaitForExitAsync(timeout.Token);
        var error = await standardError;

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("[FatalStartup]", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception.", error, StringComparison.Ordinal);
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
        startInfo.Environment["ITOGURUMA_LOG_DIR"] = Path.Combine(
            Path.GetDirectoryName(databasePath)!, "logs");
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
