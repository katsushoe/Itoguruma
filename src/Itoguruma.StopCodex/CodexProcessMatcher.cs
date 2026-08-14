namespace Itoguruma.StopCodex;

internal static class CodexProcessMatcher
{
    private static readonly HashSet<string> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Codex.exe",
        "codex.exe",
        "codex-windows-sandbox.exe",
    };

    public static bool IsMatch(ProcessInfo process)
    {
        return ProcessNames.Contains(process.Name)
            || Contains(process.ExecutablePath, "\\OpenAI.Codex_");
    }

    private static bool Contains(string? value, string fragment)
    {
        return value?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
    }
}
