using System.Text.Json;
using System.Text.Json.Serialization;

namespace Itoguruma.Core;

/// <summary>CR payloadのバージョン1を表します。</summary>
public sealed record ChangeRequestPayload(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("cr_path")] string CrPath,
    [property: JsonPropertyName("source_project")] string SourceProject,
    [property: JsonPropertyName("target_project")] string TargetProject,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("status")] string Status);

/// <summary>CRファイルとpayloadの整合性検証結果です。</summary>
public sealed record ChangeRequestInspection(
    string CrPath,
    string SourceProject,
    string TargetProject,
    string Priority,
    string RecordedStatus,
    string CurrentStatus,
    bool StatusMatches);

/// <summary>共有CR領域にあるCRファイルを検証します。</summary>
public sealed class ChangeRequestValidator(string crRoot)
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredHeadings = ["## 背景", "## 依頼内容", "## 完了条件", "## 受け取り結果"];
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
        { "未着手", "対応中", "完了", "分割済み" };
    private readonly string _crRoot = Path.GetFullPath(
        string.IsNullOrWhiteSpace(crRoot) ? throw new ArgumentException("CR root is required.", nameof(crRoot)) : crRoot);

    /// <summary>CR payloadを解析し、ファイルと内容の整合性を検証します。</summary>
    public async Task<ChangeRequestInspection> InspectAsync(
        string? payloadJson,
        bool requireStatusMatch = true,
        CancellationToken cancellationToken = default)
    {
        var payload = ParsePayload(payloadJson);
        ValidatePayload(payload);
        var path = ValidatePath(payload);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var source = ReadField(content, "依頼元");
        var target = ReadField(content, "依頼先");
        var priority = ReadField(content, "優先度");
        var currentStatus = ReadField(content, "状態");
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var heading in RequiredHeadings)
            if (!lines.Contains(heading))
                throw new ArgumentException($"CR file is missing required heading: {heading}", nameof(payloadJson));
        RequireEqual(payload.SourceProject, source, "source_project", "依頼元", payloadJson);
        RequireEqual(payload.TargetProject, target, "target_project", "依頼先", payloadJson);
        RequireEqual(payload.Priority, priority, "priority", "優先度", payloadJson);
        if (requireStatusMatch) RequireEqual(payload.Status, currentStatus, "status", "状態", payloadJson);
        return new(path, source, target, priority, payload.Status, currentStatus,
            string.Equals(payload.Status, currentStatus, StringComparison.Ordinal));
    }

    private static ChangeRequestPayload ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("change_request requires payload_json.", nameof(payloadJson));
        try
        {
            return JsonSerializer.Deserialize<ChangeRequestPayload>(payloadJson, JsonOptions)
                ?? throw new ArgumentException("change_request payload_json must be an object.", nameof(payloadJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("change_request payload_json does not match schema version 1.", nameof(payloadJson), exception);
        }
    }

    private static void ValidatePayload(ChangeRequestPayload payload)
    {
        if (payload.SchemaVersion != SupportedSchemaVersion)
            throw new ArgumentException("Unsupported change_request schema_version.", nameof(payload));
        RequireText(payload.CrPath, "cr_path");
        RequireText(payload.SourceProject, "source_project");
        RequireText(payload.TargetProject, "target_project");
        RequireText(payload.Priority, "priority");
        RequireText(payload.Status, "status");
        if (!AllowedStatuses.Contains(payload.Status))
            throw new ArgumentException("Unsupported change_request status.", nameof(payload));
        if (payload.TargetProject.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || payload.TargetProject is "." or "..")
            throw new ArgumentException("target_project must be a single directory name.", nameof(payload));
    }

    private string ValidatePath(ChangeRequestPayload payload)
    {
        if (!Path.IsPathFullyQualified(payload.CrPath))
            throw new ArgumentException("cr_path must be an absolute path.", nameof(payload));
        var path = Path.GetFullPath(payload.CrPath);
        var expectedDirectory = Path.GetFullPath(Path.Combine(_crRoot, "inbox", payload.TargetProject));
        var relative = Path.GetRelativePath(expectedDirectory, path);
        if (relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("cr_path must be directly under inbox/<target_project>.", nameof(payload));
        if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("cr_path must reference a Markdown file.", nameof(payload));
        if (!File.Exists(path)) throw new ArgumentException("cr_path does not reference an existing file.", nameof(payload));
        var parent = Path.GetDirectoryName(path);
        if (!string.Equals(parent, expectedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("cr_path target directory does not match target_project.", nameof(payload));
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(expectedDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("cr_path must not traverse a symbolic link or junction.", nameof(payload));
        return path;
    }

    private static string ReadField(string content, string name)
    {
        var prefix = $"- {name}:";
        var line = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var value = line?[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"CR file is missing required field: {name}", nameof(content))
            : value;
    }

    private static void RequireEqual(string expected, string actual, string payloadName, string fileName, string? payloadJson)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new ArgumentException($"change_request {payloadName} does not match CR field {fileName}.", nameof(payloadJson));
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", nameof(value));
    }
}
