using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Itoguruma.Core;

try
{
    var endpoint = GetOption(args, "--url")
        ?? throw new ArgumentException("Missing required option: --url.");
    var token = new WindowsCredentialTokenStore(GetOption(args, "--credential-target")).Read()
        ?? throw new InvalidOperationException("The Itoguruma credential is not configured.");
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    await foreach (var requestJson in ReadRequestsAsync(Console.In))
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) continue;
            if (!response.IsSuccessStatusCode)
            {
                await WriteErrorResponseAsync(requestJson,
                    $"Itoguruma server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                continue;
            }
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                await Console.Out.WriteLineAsync(await response.Content.ReadAsStringAsync());
                await Console.Out.FlushAsync();
                continue;
            }
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync() is { } responseLine)
            {
                if (!responseLine.StartsWith("data:", StringComparison.Ordinal)) continue;
                await Console.Out.WriteLineAsync(responseLine[5..].TrimStart());
                await Console.Out.FlushAsync();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Itoguruma MCP proxy request failed: {exception}");
            await WriteErrorResponseAsync(requestJson, "Itoguruma server communication failed.");
        }
    }
    return 0;
}

catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Itoguruma MCP proxy failed: {exception}");
    return 1;
}

static async Task WriteErrorResponseAsync(string requestJson, string message)
{
    using var request = JsonDocument.Parse(requestJson);
    if (!request.RootElement.TryGetProperty("id", out var id)) return;
    var response = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = id.Clone(),
        error = new { code = -32000, message }
    });
    await Console.Out.WriteLineAsync(response);
    await Console.Out.FlushAsync();
}

static async IAsyncEnumerable<string> ReadRequestsAsync(
    TextReader reader,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var request = new StringBuilder();
    var buffer = new char[1];
    var depth = 0;
    var inString = false;
    var escaped = false;
    while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) > 0)
    {
        var character = buffer[0];
        if (depth == 0 && char.IsWhiteSpace(character)) continue;
        request.Append(character);
        if (inString)
        {
            if (escaped) escaped = false;
            else if (character == '\\') escaped = true;
            else if (character == '"') inString = false;
            continue;
        }
        if (character == '"') inString = true;
        else if (character is '{' or '[') depth++;
        else if (character is '}' or ']') depth--;
        if (depth != 0) continue;
        yield return request.ToString();
        request.Clear();
    }
    if (request.Length > 0)
        throw new JsonException("The MCP stdio stream ended with an incomplete JSON request.");
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}
