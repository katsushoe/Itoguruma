using System.Security.Cryptography;

namespace Itoguruma.Cli;

/// <summary>認証トークンの状態確認とローテーションを実行します。</summary>
public sealed class AuthCommand(
    IUserTokenStore tokenStore,
    TextReader input,
    TextWriter output,
    TextWriter error,
    Func<byte[]>? tokenFactory = null)
{
    private const string Confirmation = "ROTATE";
    private readonly Func<byte[]> _tokenFactory = tokenFactory ?? CreateToken;

    /// <summary>指定されたauthサブコマンドを実行します。</summary>
    public int Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0 || arguments[0] is "-h" or "--help")
        {
            output.WriteLine("itoguruma auth status|rotate");
            return 0;
        }

        return arguments[0] switch
        {
            "status" => Status(),
            "rotate" => Rotate(),
            _ => throw new ArgumentException($"Unknown auth command: {arguments[0]}")
        };
    }

    private int Status()
    {
        output.WriteLine(tokenStore.IsConfigured
            ? "Authentication token: configured."
            : "Authentication token: not configured.");
        return 0;
    }

    private int Rotate()
    {
        output.WriteLine("WARNING: Rotating the authentication token immediately invalidates the current token.");
        output.WriteLine("The server, Codex, Claude Code, and Hataori must be restarted or reconfigured.");
        output.Write($"Type {Confirmation} to continue: ");
        if (!string.Equals(input.ReadLine(), Confirmation, StringComparison.Ordinal))
        {
            output.WriteLine("Token rotation cancelled.");
            return 1;
        }

        byte[] token = _tokenFactory();
        try
        {
            if (token.Length < 32)
            {
                throw new InvalidOperationException("The token generator returned fewer than 32 bytes.");
            }

            tokenStore.Save(Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        }
        catch (Exception ex)
        {
            error.WriteLine($"Token rotation failed: {ex.Message}");
            return 2;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }

        output.WriteLine("Authentication token rotated. The token value is not displayed.");
        output.WriteLine("Next: restart the ItogurumaServer scheduled task, open a new terminal, and restart Codex and Claude Code.");
        output.WriteLine("Reconfigure clients that store the bearer token directly, including Claude Code and Hataori.");
        return 0;
    }

    private static byte[] CreateToken() => RandomNumberGenerator.GetBytes(32);
}

/// <summary>ユーザー環境の認証トークンを読み書きします。</summary>
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
