using System.Diagnostics;
using System.Text.Json;

namespace Itoguruma.Viewer;

/// <summary>MCPサーバーの稼働状態です。</summary>
internal enum ServerState
{
    /// <summary>Healthエンドポイントが <c>status=ok</c> を返しました。</summary>
    Running,
    /// <summary>サーバープロセスは存在しますが、Healthエンドポイントが応答しません。</summary>
    NotResponding,
    /// <summary>サーバープロセスもHealth応答もありません。</summary>
    Stopped
}

/// <summary>状態確認の結果です。<see cref="Version"/> は <see cref="ServerState.Running"/> のときだけ設定されます。</summary>
internal sealed record ServerStatus(ServerState State, string? Version);

/// <summary>
/// インストール済みのItoguruma MCPサーバーの状態確認・起動・停止を行います。
/// 起動はインストーラーが登録したタスク <c>ItogurumaServer</c> を優先し、タスクがない場合だけ実行ファイルを直接起動します。
/// </summary>
internal sealed class ServerController
{
    internal const string TaskName = "ItogurumaServer";
    internal const string DefaultServerUrl = "http://127.0.0.1:47631";
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly string _serverPath;
    private readonly string _configDirectory;
    private readonly string _logDirectory;
    private readonly Uri _healthUri;

    public ServerController(string installationRoot, string configDirectory)
    {
        _serverPath = Path.Combine(installationRoot, "bin", "server", "Itoguruma.Server.exe");
        _configDirectory = configDirectory;
        _logDirectory = Path.Combine(installationRoot, "logs");
        _healthUri = ResolveHealthUri(configDirectory);
    }

    /// <summary>設定ディレクトリの appsettings.json の <c>Itoguruma:ServerUrl</c> からHealthエンドポイントを求めます。読めない場合は既定URLを使います。</summary>
    internal static Uri ResolveHealthUri(string configDirectory)
    {
        var serverUrl = DefaultServerUrl;
        var path = Path.Combine(configDirectory, "appsettings.json");
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("Itoguruma", out var section)
                    && section.TryGetProperty("ServerUrl", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                    serverUrl = value.GetString()!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            uri = new Uri(DefaultServerUrl);
        return new UriBuilder(uri.Scheme, uri.Host, uri.Port, "/health").Uri;
    }

    /// <summary>Health応答本文から稼働中かどうかと版を読み取ります。<c>status</c> が <c>ok</c> でなければ null を返します。</summary>
    internal static string? ParseHealthVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String
                || !string.Equals(status.GetString(), "ok", StringComparison.Ordinal)) return null;
            return root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException) { return null; }
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var version = await ProbeHealthAsync(cancellationToken);
        if (version is not null) return new(ServerState.Running, version);
        return FindServerProcesses().Count > 0
            ? new(ServerState.NotResponding, null)
            : new(ServerState.Stopped, null);
    }

    /// <summary>サーバーを起動し、Healthが応答するまで待ちます。既に稼働中なら何もしません。</summary>
    public async Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetStatusAsync(cancellationToken);
        if (current.State == ServerState.Running) return current;
        if (!File.Exists(_serverPath))
            throw new FileNotFoundException("The Itoguruma server executable was not found.", _serverPath);

        // タスク経由で起動すると、サインイン時と同じ文脈でドライブやジャンクションを参照できます。
        if (await Task.Run(() => RunSchtasks("/Run", "/TN", TaskName), cancellationToken) != 0)
        {
            Process.Start(new ProcessStartInfo(_serverPath)
            {
                ArgumentList = { "--config-dir", _configDirectory, "--log-dir", _logDirectory },
                WorkingDirectory = Path.GetDirectoryName(_serverPath)!,
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        var deadline = DateTime.UtcNow + StartTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var version = await ProbeHealthAsync(cancellationToken);
            if (version is not null) return new(ServerState.Running, version);
            await Task.Delay(500, cancellationToken);
        }
        return await GetStatusAsync(cancellationToken);
    }

    /// <summary>タスクを終了し、インストール先のサーバープロセスが残っていれば終了させます。</summary>
    public async Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => RunSchtasks("/End", "/TN", TaskName), cancellationToken);
        var deadline = DateTime.UtcNow + StopTimeout;
        while (DateTime.UtcNow < deadline && FindServerProcesses().Count > 0)
            await Task.Delay(250, cancellationToken);
        foreach (var process in FindServerProcesses())
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (InvalidOperationException) { }
            }
        }
        return await GetStatusAsync(cancellationToken);
    }

    private async Task<string?> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = HealthTimeout };
        try
        {
            using var response = await client.GetAsync(_healthUri, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return ParseHealthVersion(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private List<Process> FindServerProcesses()
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName("Itoguruma.Server"))
        {
            string? path = null;
            try { path = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            if (path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_serverPath), StringComparison.OrdinalIgnoreCase))
                result.Add(process);
            else
                process.Dispose();
        }
        return result;
    }

    private static int RunSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo);
        if (process is null) return -1;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
