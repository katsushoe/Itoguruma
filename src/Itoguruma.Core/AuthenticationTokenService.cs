using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace Itoguruma.Core;

/// <summary>認証トークンの永続化先です。</summary>
public interface IUserTokenStore
{
    /// <summary>トークンが設定済みかどうかを取得します。</summary>
    bool IsConfigured { get; }

    /// <summary>トークンを永続化します。</summary>
    void Save(string token);

    /// <summary>保存済みトークンを取得します。</summary>
    string? Read() => null;
}

/// <summary>Windows資格情報マネージャーへ認証トークンを保存します。</summary>
public sealed class WindowsCredentialTokenStore : IUserTokenStore
{
    private const string DefaultTargetName = "Itoguruma/McpBearerToken";
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private readonly string _targetName;

    /// <summary>資格情報の保存先を指定して初期化します。</summary>
    public WindowsCredentialTokenStore(string? targetName = null)
    {
        _targetName = string.IsNullOrWhiteSpace(targetName) ? DefaultTargetName : targetName;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Read());

    /// <inheritdoc />
    public void Save(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var bytes = System.Text.Encoding.Unicode.GetBytes(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = _targetName,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Windows Credential Manager rejected the token: {Marshal.GetLastWin32Error()}.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    /// <inheritdoc />
    public string? Read()
    {
        if (!CredRead(_targetName, GenericCredential, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / sizeof(char));
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
        const int NotFound = 1168;
        var error = Marshal.GetLastWin32Error();
        if (error != NotFound) throw new InvalidOperationException($"Windows Credential Manager rejected deletion: {error}.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
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
