using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Itoguruma.Core;

namespace Itoguruma.Viewer;

internal sealed class ReleaseUpdater(HttpClient? httpClient = null)
{
    private const string Repository = "katsushoe/Itoguruma";
    private const long MaximumInstallerBytes = 250L * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient = httpClient ?? SharedHttpClient;

    public async Task<bool> InstallUpdateIfAvailableAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var releaseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var release = releaseDocument.RootElement;
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return false;

        var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
            || !Version.TryParse(ProductInfo.Version, out var current)
            || latest <= current) return false;

        var assetName = $"Itoguruma-{latest}-win-x64.msi";
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var msiUrl = FindAsset(assets, assetName);
        var sumsUrl = FindAsset(assets, "SHA256SUMS.txt");
        if (msiUrl is null || sumsUrl is null) throw new InvalidDataException("The release is missing its installer or checksum file.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "itoguruma-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var installerPath = Path.Combine(temporaryDirectory, assetName);
            using (var installerResponse = await _httpClient.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                installerResponse.EnsureSuccessStatusCode();
                if (installerResponse.Content.Headers.ContentLength is > MaximumInstallerBytes)
                    throw new InvalidDataException("The installer exceeds the allowed download size.");
                await using var downloadStream = await installerResponse.Content.ReadAsStreamAsync(cancellationToken);
                await using var installerFile = File.Create(installerPath);
                var buffer = new byte[81920];
                long totalBytes = 0;
                while (true)
                {
                    var read = await downloadStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    totalBytes += read;
                    if (totalBytes > MaximumInstallerBytes)
                        throw new InvalidDataException("The installer exceeds the allowed download size.");
                    await installerFile.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            var sums = await _httpClient.GetStringAsync(sumsUrl, cancellationToken);
            var line = sums.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.EndsWith("  " + assetName, StringComparison.Ordinal));
            var match = line is null ? Match.Empty : Regex.Match(line, "^([0-9a-fA-F]{64})  ");
            if (!match.Success) throw new InvalidDataException("The installer checksum entry is missing or invalid.");

            await using var installerStream = File.OpenRead(installerPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(match.Groups[1].Value)))
                throw new InvalidDataException("The installer checksum does not match.");

            var language = AppLocalization.IsJapanese ? "ja" : "en";
            var start = new ProcessStartInfo("msiexec.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("/i");
            start.ArgumentList.Add(installerPath);
            start.ArgumentList.Add("/qn");
            start.ArgumentList.Add("/norestart");
            start.ArgumentList.Add($"ITOGURUMA_LANGUAGE={language}");
            if (Process.Start(start) is null) throw new InvalidOperationException("Windows Installer could not be started.");
            return true;
        }
        catch
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch (IOException) { }
            throw;
        }
    }

    private static string? FindAsset(JsonElement[] assets, string name)
    {
        var asset = assets.FirstOrDefault(candidate => candidate.TryGetStringProperty("name") == name);
        var value = asset.TryGetStringProperty("browser_download_url");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Itoguruma", ProductInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

internal static class JsonElementExtensions
{
    public static string? TryGetStringProperty(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
}
