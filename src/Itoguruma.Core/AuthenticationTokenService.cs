using System.Security.Cryptography;

namespace Itoguruma.Core;

/// <summary>認証トークンの永続化先です。</summary>
public interface IUserTokenStore
{
    /// <summary>トークンが設定済みかどうかを取得します。</summary>
    bool IsConfigured { get; }

    /// <summary>トークンを永続化します。</summary>
    void Save(string token);
}

/// <summary>ユーザー環境変数へ認証トークンを保存します。</summary>
public sealed class UserEnvironmentTokenStore : IUserTokenStore
{
    private const string VariableName = "ITOGURUMA_AUTH_TOKEN";

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable(VariableName, EnvironmentVariableTarget.User));

    /// <inheritdoc />
    public void Save(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Environment.SetEnvironmentVariable(VariableName, token, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(VariableName, token, EnvironmentVariableTarget.Process);
    }
}

/// <summary>認証トークンの状態確認とローテーションを提供します。</summary>
public sealed class AuthenticationTokenService(IUserTokenStore tokenStore, Func<byte[]>? tokenFactory = null)
{
    private readonly Func<byte[]> _tokenFactory = tokenFactory ?? (() => RandomNumberGenerator.GetBytes(32));

    /// <summary>トークンが設定済みかどうかを取得します。</summary>
    public bool IsConfigured => tokenStore.IsConfigured;

    /// <summary>トークンを更新します。トークン値は返しません。</summary>
    public void Rotate()
    {
        byte[] token = _tokenFactory();
        try
        {
            if (token.Length < 32)
            {
                throw new InvalidOperationException("The token generator returned fewer than 32 bytes.");
            }

            tokenStore.Save(Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }
}
