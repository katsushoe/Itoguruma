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
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"{Scope}itoguruma.database.{Convert.ToHexString(hash, 0, 16).ToLowerInvariant()}.single";
    }
}
