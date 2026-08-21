using System.Diagnostics;
using Itoguruma.Core;

namespace Itoguruma.StopCodex;

internal static class Program
{
    private const string ListOption = "--list";

    public static int Main(string[] args)
    {
        AppLocalization.ConfigureFromEnvironment();
        if (args.Length > 1 || (args.Length == 1 && !string.Equals(args[0], ListOption, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine(L("Usage: stop-codex [--list]", "使用方法: stop-codex [--list]"));
            return 2;
        }

        var listOnly = args.Length == 1;
        var targets = ProcessReader.ReadAll()
            .Where(process => process.ProcessId != Environment.ProcessId && CodexProcessMatcher.IsMatch(process))
            .OrderBy(process => process.ProcessId)
            .ToArray();

        if (targets.Length == 0)
        {
            Console.WriteLine(L("No Codex-related processes were found.", "Codex関連のプロセスは見つかりませんでした。"));
            return 0;
        }

        foreach (var target in targets)
        {
            Console.WriteLine($"{target.ProcessId,7}  {target.Name}  {target.ExecutablePath}");
        }

        if (listOnly)
        {
            Console.WriteLine(L($"Found {targets.Length} process(es). No processes were stopped.", $"{targets.Length}件のプロセスが見つかりました。停止はしていません。"));
            return 0;
        }

        var failureCount = 0;
        foreach (var target in targets)
        {
            try
            {
                using var process = Process.GetProcessById(target.ProcessId);
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
                // The process ended after enumeration.
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                failureCount++;
                Console.Error.WriteLine(L($"Could not stop PID {target.ProcessId}: {ex.Message}", $"PID {target.ProcessId}を停止できませんでした: {ex.Message}"));
            }
        }

        Console.WriteLine(L($"Stopped {targets.Length - failureCount} process(es). Failed: {failureCount}.", $"{targets.Length - failureCount}件を停止しました。失敗: {failureCount}件。"));
        return failureCount == 0 ? 0 : 1;
    }

    private static string L(string english, string japanese) => AppLocalization.Text(english, japanese);
}
