using System.Security.Cryptography;
using Itoguruma.Core;

namespace Itoguruma.Cli;

public interface IHumanConfirmationConsole
{
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
    void WriteLine(string value);
    ConsoleKeyInfo ReadKey(bool intercept);
}

public sealed class SystemHumanConfirmationConsole : IHumanConfirmationConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public void WriteLine(string value) => Console.WriteLine(value);
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
}

public sealed class HumanConfirmation(
    IHumanConfirmationConsole console,
    TimeProvider? timeProvider = null,
    Func<int>? codeGenerator = null)
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Func<int> _codeGenerator = codeGenerator ?? (() => RandomNumberGenerator.GetInt32(10_000, 100_000));

    public void Require()
    {
        if (console.IsInputRedirected || console.IsOutputRedirected)
            throw new ProjectOperationException(ProjectErrorCodes.ConsoleRedirected,
                $"{ProjectErrorCodes.ConsoleRedirected}: Interactive input and output are required.");
        var code = _codeGenerator().ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        var expiresAt = _timeProvider.GetUtcNow() + Lifetime;
        console.WriteLine(AppLocalization.Text(
            $"Enter this confirmation code within 60 seconds: {code}",
            $"60秒以内に次の確認コードを入力してください: {code}"));
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var input = ReadFiveDigits(expiresAt);
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(input), System.Text.Encoding.ASCII.GetBytes(code))) return;
        }
        throw new ProjectOperationException(ProjectErrorCodes.ConfirmationFailed,
            $"{ProjectErrorCodes.ConfirmationFailed}: Confirmation failed after three attempts.");
    }

    private string ReadFiveDigits(DateTimeOffset expiresAt)
    {
        var input = new char[5];
        for (var index = 0; index < input.Length;)
        {
            if (_timeProvider.GetUtcNow() > expiresAt)
                throw new ProjectOperationException(ProjectErrorCodes.ConfirmationExpired,
                    $"{ProjectErrorCodes.ConfirmationExpired}: Confirmation code expired.");
            var key = console.ReadKey(true);
            if (char.IsAsciiDigit(key.KeyChar)) input[index++] = key.KeyChar;
        }
        console.WriteLine(string.Empty);
        if (_timeProvider.GetUtcNow() > expiresAt)
            throw new ProjectOperationException(ProjectErrorCodes.ConfirmationExpired,
                $"{ProjectErrorCodes.ConfirmationExpired}: Confirmation code expired.");
        return new string(input);
    }
}
