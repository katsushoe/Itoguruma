using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace Itoguruma.Core;

/// <summary>Bearerトークンと診断用世代IDの組です。トークン値は秘密です。</summary>
public sealed record StoredAuthenticationToken(string Token, string GenerationId);

/// <summary>認証トークンの永続化先です。</summary>
public interface IUserTokenStore
{
    /// <summary>トークンが設定済みかどうかを取得します。</summary>
    bool IsConfigured { get; }
    /// <summary>トークンと世代IDを一組で永続化します。</summary>
    void Save(StoredAuthenticationToken credential);
    /// <summary>保存済みトークンと世代IDを取得します。</summary>
    StoredAuthenticationToken? Read();
}

/// <summary>Windows資格情報マネージャーへ認証トークンと世代IDを保存します。</summary>
public sealed class WindowsCredentialTokenStore : IUserTokenStore
{
    private const string DefaultTargetName = "Itoguruma/McpBearerToken";
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private readonly string _targetName;

    /// <summary>資格情報の保存先を指定して初期化します。</summary>
    public WindowsCredentialTokenStore(string? targetName = null) =>
        _targetName = string.IsNullOrWhiteSpace(targetName) ? DefaultTargetName : targetName;

    /// <inheritdoc />
    public bool IsConfigured => Read() is not null;

    /// <inheritdoc />
    public void Save(StoredAuthenticationToken credential)
        => WithStoreLock(() => { SaveCore(credential); return true; });

    private void SaveCore(StoredAuthenticationToken credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        Validate(credential);
        var bytes = System.Text.Encoding.Unicode.GetBytes(credential.Token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var native = new Credential
            {
                Type = GenericCredential,
                TargetName = _targetName,
                Comment = $"itoguruma-generation:{credential.GenerationId}",
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref native, 0))
                throw new InvalidOperationException($"Windows Credential Manager rejected the token: {Marshal.GetLastWin32Error()}.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    /// <inheritdoc />
    public StoredAuthenticationToken? Read()
        => WithStoreLock(ReadCore);

    private StoredAuthenticationToken? ReadCore()
    {
        if (!CredRead(_targetName, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new InvalidOperationException($"Windows Credential Manager rejected reading the token: {error}.");
        }
        try
        {
            var native = Marshal.PtrToStructure<Credential>(pointer);
            if (native.CredentialBlob == IntPtr.Zero || native.CredentialBlobSize <= 0 || native.CredentialBlobSize % sizeof(char) != 0)
                return null;
            var token = Marshal.PtrToStringUni(native.CredentialBlob, native.CredentialBlobSize / sizeof(char));
            if (string.IsNullOrEmpty(token)) return null;
            var generationId = ParseGeneration(native.Comment);
            if (generationId is null)
            {
                generationId = NewGenerationId();
                SaveCore(new StoredAuthenticationToken(token, generationId));
            }
            return new StoredAuthenticationToken(token, generationId);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>保存済み資格情報を削除します。</summary>
    public void Delete()
    {
        if (CredDelete(_targetName, GenericCredential, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound) throw new InvalidOperationException($"Windows Credential Manager rejected deletion: {error}.");
    }

    private static void Validate(StoredAuthenticationToken credential)
    {
        if (string.IsNullOrWhiteSpace(credential.Token) || credential.Token.Length is < 32 or > 256
            || credential.Token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("The credential token has an invalid format.", nameof(credential));
        if (!Guid.TryParseExact(credential.GenerationId, "N", out _))
            throw new ArgumentException("The credential generation ID has an invalid format.", nameof(credential));
    }

    private static string? ParseGeneration(string? comment)
    {
        const string prefix = "itoguruma-generation:";
        if (comment is null || comment.Length != prefix.Length + 32 || !comment.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var value = comment[prefix.Length..];
        return Guid.TryParseExact(value, "N", out var id) ? id.ToString("N") : null;
    }

    private static string NewGenerationId() => Guid.NewGuid().ToString("N");

    private static T WithStoreLock<T>(Func<T> operation)
    {
        using var mutex = new Mutex(false, "Local\\Itoguruma.CredentialTokenStore");
        var ownsMutex = false;
        try
        {
            try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new InvalidOperationException("The credential store is busy.");
            return operation();
        }
        finally
        {
            if (ownsMutex) mutex.ReleaseMutex();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags; public int Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize; public IntPtr CredentialBlob; public int Persist;
        public int AttributeCount; public IntPtr Attributes; public IntPtr TargetAlias; public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}

/// <summary>認証トークンの状態確認とローテーションを提供します。</summary>
public sealed class AuthenticationTokenService(IUserTokenStore tokenStore, Func<byte[]>? tokenFactory = null,
    Func<string>? generationFactory = null)
{
    private readonly Func<byte[]> _tokenFactory = tokenFactory ?? (() => RandomNumberGenerator.GetBytes(32));
    private readonly Func<string> _generationFactory = generationFactory ?? (() => Guid.NewGuid().ToString("N"));

    /// <summary>現在の資格情報を取得します。</summary>
    public StoredAuthenticationToken? Current => tokenStore.Read();
    /// <summary>トークンが設定済みかどうかを取得します。</summary>
    public bool IsConfigured => Current is not null;

    /// <summary>トークンを更新します。トークン値は返しません。</summary>
    public string Rotate()
    {
        byte[] token = _tokenFactory();
        try
        {
            if (token.Length < 32) throw new InvalidOperationException("The token generator returned fewer than 32 bytes.");
            var generationId = _generationFactory();
            if (!Guid.TryParseExact(generationId, "N", out var parsed))
                throw new InvalidOperationException("The generation ID generator returned an invalid value.");
            generationId = parsed.ToString("N");
            tokenStore.Save(new StoredAuthenticationToken(
                Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_'), generationId));
            return generationId;
        }
        finally { CryptographicOperations.ZeroMemory(token); }
    }
}
