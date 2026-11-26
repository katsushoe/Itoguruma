using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Itoguruma.Cli;

/// <summary>Manages Itoguruma's user-scoped MCP and lifecycle-hook settings.</summary>
public sealed class ClientConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] CodexEvents = ["SessionStart", "UserPromptSubmit", "Stop"];
    private const string ServerName = "itoguruma";

    /// <summary>Configures or removes Itoguruma from a supported AI client.</summary>
    public ConfigurationResult Run(
        string operation,
        string client,
        string userProfile,
        string cliPath,
        string proxyPath,
        string serverUrl)
    {
        if (operation is not ("configure" or "unconfigure"))
            throw new ArgumentException("Operation must be configure or unconfigure.", nameof(operation));
        if (client is not ("codex" or "claude"))
            throw new ArgumentException("Client must be codex or claude.", nameof(client));

        var files = client == "codex"
            ? BuildCodexFiles(userProfile, cliPath, proxyPath, serverUrl, operation == "configure")
            : BuildClaudeFiles(userProfile, cliPath, proxyPath, serverUrl, operation == "configure");
        AtomicFileTransaction.Apply(files);
        return new ConfigurationResult(operation, client, files.Count);
    }

    private static Dictionary<string, string?> BuildCodexFiles(
        string userProfile, string cliPath, string proxyPath, string serverUrl, bool configure)
    {
        var root = Path.Combine(userProfile, ".codex");
        var mcpPath = Path.Combine(root, "config.toml");
        var hooksPath = Path.Combine(root, "hooks.json");
        var currentMcp = ReadOptionalText(mcpPath);
        var nextMcp = UpdateCodexMcp(currentMcp ?? string.Empty, proxyPath, serverUrl, configure);
        var currentHooks = ReadOptionalText(hooksPath);
        var nextHooks = UpdateHooks(currentHooks, cliPath, "codex", CodexEvents, configure);
        if (!configure && currentMcp is null) nextMcp = null;
        return ChangedFiles((mcpPath, currentMcp, nextMcp), (hooksPath, currentHooks, nextHooks));
    }

    private static Dictionary<string, string?> BuildClaudeFiles(
        string userProfile, string cliPath, string proxyPath, string serverUrl, bool configure)
    {
        var mcpPath = Path.Combine(userProfile, ".claude.json");
        var hooksPath = Path.Combine(userProfile, ".claude", "settings.json");
        var currentMcp = ReadOptionalText(mcpPath);
        var nextMcp = UpdateClaudeMcp(currentMcp, proxyPath, serverUrl, configure);
        var currentHooks = ReadOptionalText(hooksPath);
        var nextHooks = UpdateHooks(currentHooks, cliPath, "claude-code", ["UserPromptSubmit"], configure);
        return ChangedFiles((mcpPath, currentMcp, nextMcp), (hooksPath, currentHooks, nextHooks));
    }

    private static Dictionary<string, string?> ChangedFiles(params (string Path, string? Current, string? Next)[] entries) =>
        entries.Where(entry => !string.Equals(entry.Current, entry.Next, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Path, entry => entry.Next, StringComparer.OrdinalIgnoreCase);

    private static string? ReadOptionalText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static string UpdateCodexMcp(string text, string proxyPath, string serverUrl, bool configure)
    {
        var (withoutServer, removed) = RemoveTomlTable(text, "mcp_servers.itoguruma");
        if (!configure && !removed) return text;
        if (!configure) return withoutServer;

        var command = JsonSerializer.Serialize(proxyPath);
        var url = JsonSerializer.Serialize(serverUrl);
        var section = $"[mcp_servers.{ServerName}]\ncommand = {command}\nargs = [\"--url\", {url}]\n";
        return AppendSection(withoutServer, section);
    }

    private static (string Text, bool Removed) RemoveTomlTable(string text, string tableName)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var result = new List<string>(lines.Length);
        var removing = false;
        var removed = false;
        foreach (var line in lines)
        {
            var header = Regex.Match(line, "^\\s*\\[\\[?([^\\]]+)\\]\\]?\\s*(?:#.*)?$");
            if (header.Success)
            {
                var name = Regex.Replace(header.Groups[1].Value.Trim(), "\"([^\"]+)\"", "$1", RegexOptions.CultureInvariant);
                removing = name.Equals(tableName, StringComparison.Ordinal)
                    || name.StartsWith(tableName + ".", StringComparison.Ordinal);
                removed |= removing;
            }
            if (!removing) result.Add(line);
        }
        return (string.Join("\n", result).TrimEnd('\n'), removed);
    }

    private static string AppendSection(string text, string section) =>
        string.IsNullOrWhiteSpace(text) ? section : text.TrimEnd() + "\n\n" + section;

    private static string? UpdateClaudeMcp(string? text, string proxyPath, string serverUrl, bool configure)
    {
        if (text is null && !configure) return null;
        var root = ParseObject(text, ".claude.json");
        if (configure)
        {
            var servers = EnsureObject(root, "mcpServers");
            servers[ServerName] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = proxyPath,
                ["args"] = new JsonArray("--url", serverUrl)
            };
        }
        else if (root["mcpServers"] is JsonObject servers)
        {
            if (!servers.Remove(ServerName)) return text;
            if (servers.Count == 0) root.Remove("mcpServers");
        }
        else if (root["mcpServers"] is not null)
            throw new InvalidDataException("The mcpServers setting in .claude.json must be a JSON object.");
        else if (!configure) return text;
        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static string? UpdateHooks(
        string? text, string cliPath, string consumer, IReadOnlyList<string> events, bool configure)
    {
        if (text is null && !configure) return null;
        var root = ParseObject(text, "hook settings");
        if (!configure && root["hooks"] is null) return text;
        var hooks = EnsureObject(root, "hooks");
        var changed = false;
        foreach (var eventName in events)
        {
            if (hooks[eventName] is JsonArray currentEntries)
            {
                for (var index = currentEntries.Count - 1; index >= 0; index--)
                {
                    if (currentEntries[index] is not JsonObject group || group["hooks"] is not JsonArray handlers) continue;
                    for (var handlerIndex = handlers.Count - 1; handlerIndex >= 0; handlerIndex--)
                    {
                        if (!IsOwnedHookHandler(handlers[handlerIndex], consumer)) continue;
                        handlers.RemoveAt(handlerIndex);
                        changed = true;
                    }
                    if (handlers.Count != 0) continue;
                    currentEntries.RemoveAt(index);
                    changed = true;
                }
                if (currentEntries.Count == 0) changed |= hooks.Remove(eventName);
            }
            else if (hooks[eventName] is not null)
            {
                throw new InvalidDataException($"The {eventName} hook setting must be a JSON array.");
            }
            if (configure)
            {
                if (hooks[eventName] is not JsonArray targetEntries)
                {
                    targetEntries = [];
                    hooks[eventName] = targetEntries;
                }
                targetEntries.Add(CreateHookEntry(cliPath, consumer));
                changed = true;
            }
        }
        if (hooks.Count == 0) changed |= root.Remove("hooks");
        if (!changed) return text;
        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static bool IsOwnedHookHandler(JsonNode? entry, string consumer)
    {
        return entry is JsonObject command
            && command["type"] is JsonValue typeValue
            && typeValue.TryGetValue<string>(out var type)
            && type == "command"
            && command["command"] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && text.Contains(" hook ", StringComparison.OrdinalIgnoreCase)
            && text.Contains("--project-inbox", StringComparison.Ordinal)
            && text.Contains("--consumer-agent " + consumer, StringComparison.Ordinal);
    }

    private static JsonObject CreateHookEntry(string cliPath, string consumer)
    {
        var command = QuoteCommandArgument(cliPath) + " hook --project-inbox --consumer-agent " + consumer;
        var powershell = cliPath.Replace("'", "''", StringComparison.Ordinal);
        var commandWindows = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"& '" + powershell
            + "' hook --project-inbox --consumer-agent " + consumer + "\"";
        return new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["commandWindows"] = commandWindows,
                ["timeout"] = 15
            })
        };
    }

    private static string QuoteCommandArgument(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static JsonObject ParseObject(string? text, string description)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        return node as JsonObject
            ?? throw new InvalidDataException($"The {description} root must be a JSON object.");
    }

    private static JsonObject EnsureObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject value) return value;
        if (parent[key] is not null) throw new InvalidDataException($"The {key} setting must be a JSON object.");
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }
}

/// <summary>Summarizes the configuration operation without exposing settings content.</summary>
public sealed record ConfigurationResult(string Operation, string Client, int FilesChanged);

internal static class AtomicFileTransaction
{
    public static void Apply(IReadOnlyDictionary<string, string?> updates)
    {
        if (updates.Count == 0) return;
        var originals = updates.Keys.ToDictionary(path => path,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null, StringComparer.OrdinalIgnoreCase);
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replaced = new List<string>();
        try
        {
            foreach (var (path, contents) in updates)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (contents is null) continue;
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(contents);
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                staged[path] = temporary;
            }
            foreach (var (path, contents) in updates)
            {
                if (contents is null) File.Delete(path);
                else File.Move(staged[path], path, true);
                replaced.Add(path);
            }
        }
        catch (Exception updateException)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var path in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    var original = originals[path];
                    if (original is null) File.Delete(path);
                    else RestoreAtomically(path, original);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException);
                }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("Configuration update failed and rollback was incomplete.",
                    [updateException, .. rollbackErrors]);
            throw new IOException("Configuration update failed; previous settings were restored.", updateException);
        }
        finally
        {
            foreach (var temporary in staged.Values)
                if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RestoreAtomically(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
