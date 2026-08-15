using System.Security.Cryptography;
using System.Text;

namespace Itoguruma.Core;

/// <summary>同じデータベースを使うサーバーの多重起動防止に使う Mutex 名を生成します。</summary>
public static class ServerSingleInstance
{
    private const string Scope = "Local\\";

    /// <summary>データベースの絶対パスに対応するセッションローカル Mutex 名を返します。</summary>
    public static string ForDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var normalizedPath = Path.GetFullPath(databasePath).ToLowerInvariant();
        return CreateName("database", normalizedPath);
    }

    /// <summary>HTTPサーバーURLに対応するセッションローカル Mutex 名を返します。</summary>
    public static string ForEndpoint(string serverUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);

        var uri = new Uri(serverUrl, UriKind.Absolute);
        var normalizedUrl = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped)
            .TrimEnd('/')
            .ToLowerInvariant();
        return CreateName("endpoint", normalizedUrl);
    }

    private static string CreateName(string resourceType, string resourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceId));
        return $"{Scope}itoguruma.{resourceType}.{Convert.ToHexString(hash, 0, 16).ToLowerInvariant()}.single";
    }
}
