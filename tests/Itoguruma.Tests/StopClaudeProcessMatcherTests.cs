using Itoguruma.StopClaude;
using Xunit;

namespace Itoguruma.Tests;

public sealed class StopClaudeProcessMatcherTests
{
    [Theory]
    [InlineData("claude.exe", @"C:\Program Files\WindowsApps\Claude_1.0\app\Claude.exe")]
    [InlineData("claude.exe", @"C:\Users\user\AppData\Roaming\Claude\claude-code\2.1\claude.exe")]
    [InlineData("claude-code.exe", @"C:\tools\claude-code.exe")]
    [InlineData("chrome-native-host.exe", @"C:\Users\user\AppData\Local\Packages\Claude_id\LocalCache\Roaming\Claude\ChromeNativeHost\chrome-native-host.exe")]
    public void IsMatch_WhenProcessBelongsToClaude_ReturnsTrue(string name, string path)
    {
        Assert.True(ClaudeProcessMatcher.IsMatch(new ProcessInfo(1, name, path)));
    }

    [Theory]
    [InlineData("chrome-native-host.exe", @"C:\Other\ChromeNativeHost\chrome-native-host.exe")]
    [InlineData("ChatGPT.exe", @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0\app\ChatGPT.exe")]
    [InlineData("node.exe", @"C:\Users\user\.claude\plugins\node.exe")]
    public void IsMatch_WhenProcessDoesNotBelongToClaude_ReturnsFalse(string name, string path)
    {
        Assert.False(ClaudeProcessMatcher.IsMatch(new ProcessInfo(1, name, path)));
    }
}
