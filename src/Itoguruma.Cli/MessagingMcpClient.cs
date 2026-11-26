using System.Net.Http.Headers;
using System.Text.Json;
using Itoguruma.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Itoguruma.Cli;

/// <summary>稼働中ItogurumaサーバーへMCP経由で業務操作を依頼します。</summary>
public sealed class MessagingMcpClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly McpClient _client;

    private MessagingMcpClient(McpClient client) => _client = client;

    /// <summary>設定とWindows資格情報を使ってMCPサーバーへ接続します。</summary>
    public static async Task<MessagingMcpClient> ConnectAsync(
        string endpoint,
        string? credentialTarget,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var credential = new WindowsCredentialTokenStore(credentialTarget).Read()
            ?? throw new InvalidOperationException("The Itoguruma credential is not configured. Run 'itoguruma auth setup'.");
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Itoguruma-Token-Generation", credential.GenerationId);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint, UriKind.Absolute),
            Name = "itoguruma-cli"
        }, httpClient, loggerFactory, ownsHttpClient: true);
        try
        {
            var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory,
                cancellationToken: cancellationToken);
            return new MessagingMcpClient(client);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    /// <summary>名前付きMCP Toolを呼び出し、dataのJSON値を返します。</summary>
    public async Task<JsonElement> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);
        var result = await _client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        var structured = JsonSerializer.SerializeToElement(result.StructuredContent, JsonOptions);
        if (result.IsError is true)
        {
            var data = structured.ValueKind == JsonValueKind.Object && structured.TryGetProperty("data", out var errorData)
                ? errorData.Clone()
                : default;
            var detail = result.Content.OfType<TextContentBlock>().Select(block => block.Text).FirstOrDefault();
            throw new RemoteToolException(toolName, data, detail ?? $"The '{toolName}' operation failed.");
        }
        if (structured.ValueKind != JsonValueKind.Object || !structured.TryGetProperty("data", out var value))
            throw new InvalidOperationException($"The '{toolName}' tool returned no structured data.");
        return value.Clone();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

/// <summary>サーバーが返した構造化業務エラーです。</summary>
public sealed class RemoteToolException(string toolName, JsonElement details, string message) : InvalidOperationException(message)
{
    /// <summary>失敗したMCP Tool名です。</summary>
    public string ToolName { get; } = toolName;

    /// <summary>サーバーが返した構造化エラー情報です。</summary>
    public JsonElement Details { get; } = details;
}
