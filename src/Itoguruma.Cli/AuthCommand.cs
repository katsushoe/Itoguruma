using System.Security.Cryptography;
using Itoguruma.Core;

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
            _ => throw new ArgumentException(AppLocalization.Text($"Unknown auth command: {arguments[0]}", $"不明な認証コマンドです: {arguments[0]}"))
        };
    }

    private int Status()
    {
        output.WriteLine(tokenStore.IsConfigured
            ? AppLocalization.Text("Authentication token: configured.", "認証トークン: 設定済みです。")
            : AppLocalization.Text("Authentication token: not configured.", "認証トークン: 未設定です。"));
        return 0;
    }

    private int Rotate()
    {
        output.WriteLine(AppLocalization.Text("WARNING: Rotating the authentication token immediately invalidates the current token.", "警告: 認証トークンを更新すると、現在のトークンは直ちに無効になります。"));
        output.WriteLine(AppLocalization.Text("The server, Codex, Claude Code, and Hataori must be restarted or reconfigured.", "サーバー、Codex、Claude Code、Hataoriの再起動または再設定が必要です。"));
        output.Write(AppLocalization.Text($"Type {Confirmation} to continue: ", $"続行するには{Confirmation}と入力してください: "));
        if (!string.Equals(input.ReadLine(), Confirmation, StringComparison.Ordinal))
        {
            output.WriteLine(AppLocalization.Text("Token rotation cancelled.", "トークンの更新を中止しました。"));
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
            error.WriteLine(AppLocalization.Text($"Token rotation failed: {ex.Message}", $"トークンの更新に失敗しました: {ex.Message}"));
            return 2;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }

        output.WriteLine(AppLocalization.Text("Authentication token rotated. The token value is not displayed.", "認証トークンを更新しました。トークン値は表示しません。"));
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
