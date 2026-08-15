using System.Diagnostics;

namespace Itoguruma.StopClaude;

internal static class Program
{
    private const string ListOption = "--list";

    public static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && !string.Equals(args[0], ListOption, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Usage: stop-claude [--list]");
            return 2;
        }

        var listOnly = args.Length == 1;
        var targets = ProcessReader.ReadAll()
            .Where(process => process.ProcessId != Environment.ProcessId && ClaudeProcessMatcher.IsMatch(process))
            .OrderBy(process => process.ProcessId)
            .ToArray();

        if (targets.Length == 0)
        {
            Console.WriteLine("No Claude-related processes were found.");
            return 0;
        }

        foreach (var target in targets)
        {
            Console.WriteLine($"{target.ProcessId,7}  {target.Name}  {target.ExecutablePath}");
        }

        if (listOnly)
        {
            Console.WriteLine($"Found {targets.Length} process(es). No processes were stopped.");
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
                Console.Error.WriteLine($"Could not stop PID {target.ProcessId}: {ex.Message}");
            }
        }

        Console.WriteLine($"Stopped {targets.Length - failureCount} process(es). Failed: {failureCount}.");
        return failureCount == 0 ? 0 : 1;
    }
}
