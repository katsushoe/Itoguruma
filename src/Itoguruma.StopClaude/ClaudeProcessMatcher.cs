namespace Itoguruma.StopClaude;

internal static class ClaudeProcessMatcher
{
    private static readonly HashSet<string> ClaudeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude.exe",
        "claude-code.exe",
    };

    public static bool IsMatch(ProcessInfo process)
    {
        if (ClaudeProcessNames.Contains(process.Name))
        {
            return true;
        }

        return string.Equals(process.Name, "chrome-native-host.exe", StringComparison.OrdinalIgnoreCase)
            && (Contains(process.ExecutablePath, "\\Packages\\Claude_")
                || Contains(process.ExecutablePath, "\\Claude\\ChromeNativeHost\\"));
    }

    private static bool Contains(string? value, string fragment)
    {
        return value?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
    }
}
