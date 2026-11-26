using System.Text.Json.Nodes;
using Itoguruma.Cli;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ClientConfigurationTests : IDisposable
{
    private readonly string _profile;
    private readonly string _cli;
    private readonly string _proxy;
    private const string ServerUrl = "http://127.0.0.1:47631/mcp";
    private readonly ClientConfigurationService _service = new();

    public ClientConfigurationTests()
    {
        _profile = Path.Combine(Path.GetTempPath(), "ItogurumaConfigTests", Guid.NewGuid().ToString("N"));
        _cli = Path.Combine(_profile, "bin", "itoguruma.exe");
        _proxy = Path.Combine(_profile, "bin", "proxy.exe");
    }

    [Fact]
    public void ConfigureCodex_WhenRepeated_PreservesOtherSettingsAndDoesNotDuplicateItoguruma()
    {
        var directory = Path.Combine(_profile, ".codex");
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.toml");
        File.WriteAllText(configPath, "[mcp_servers.other]\ncommand = \"other\"\n\n[mcp_servers.itoguruma]\ncommand = \"old\"\n");
        var hooksPath = Path.Combine(directory, "hooks.json");
        File.WriteAllText(hooksPath, """
            {"hooks":{"UserPromptSubmit":[{"matcher":"Write","hooks":[{"type":"command","command":"other-hook"},{"type":"command","command":"itoguruma.exe hook --project-inbox --consumer-agent codex"}]}]}}
            """);

        _service.Run("configure", "codex", _profile, _cli, _proxy, ServerUrl);
        var repeated = _service.Run("configure", "codex", _profile, _cli, _proxy, ServerUrl);

        Assert.Equal(0, repeated.FilesChanged);
        var config = File.ReadAllText(configPath);
        Assert.Contains("[mcp_servers.other]", config, StringComparison.Ordinal);
        Assert.Equal(1, Count(config, "[mcp_servers.itoguruma]"));
        Assert.Contains(_proxy.Replace("\\", "\\\\", StringComparison.Ordinal), config, StringComparison.Ordinal);
        var hooks = JsonNode.Parse(File.ReadAllText(hooksPath))!.AsObject();
        var promptHooks = hooks["hooks"]!["UserPromptSubmit"]!.AsArray();
        Assert.Equal(2, promptHooks.Count);
        Assert.Contains(promptHooks, entry => entry?["hooks"]?[0]?["command"]?.GetValue<string>() == "other-hook");
        Assert.Single(hooks["hooks"]!["SessionStart"]!.AsArray());
        Assert.Single(hooks["hooks"]!["Stop"]!.AsArray());
    }

    [Fact]
    public void UnconfigureCodex_RemovesOnlyOwnedServerAndHooks()
    {
        ConfigureCodex_WhenRepeated_PreservesOtherSettingsAndDoesNotDuplicateItoguruma();
        _service.Run("unconfigure", "codex", _profile, _cli, string.Empty, string.Empty);

        var config = File.ReadAllText(Path.Combine(_profile, ".codex", "config.toml"));
        Assert.Contains("[mcp_servers.other]", config, StringComparison.Ordinal);
        Assert.DoesNotContain("[mcp_servers.itoguruma]", config, StringComparison.Ordinal);
        var hooks = JsonNode.Parse(File.ReadAllText(Path.Combine(_profile, ".codex", "hooks.json")))!.AsObject();
        var remainingPromptHook = Assert.Single(hooks["hooks"]!["UserPromptSubmit"]!.AsArray());
        Assert.Equal("other-hook", remainingPromptHook?["hooks"]?[0]?["command"]?.GetValue<string>());
        Assert.False(hooks["hooks"]!.AsObject().ContainsKey("SessionStart"));
        Assert.False(hooks["hooks"]!.AsObject().ContainsKey("Stop"));
    }

    [Fact]
    public void ConfigureClaude_WhenRepeated_PreservesOtherSettingsAndDoesNotDuplicateItoguruma()
    {
        Directory.CreateDirectory(Path.Combine(_profile, ".claude"));
        var mcpPath = Path.Combine(_profile, ".claude.json");
        File.WriteAllText(mcpPath, """
            {"mcpServers":{"other":{"type":"stdio","command":"other"}},"projects":{"F:/sample":{"hasTrustDialogAccepted":true}}}
            """);
        var hooksPath = Path.Combine(_profile, ".claude", "settings.json");
        File.WriteAllText(hooksPath, """
            {"env":{"KEEP":"yes"},"hooks":{"UserPromptSubmit":[{"matcher":"Edit","hooks":[{"type":"command","command":"other-hook"}]}]}}
            """);

        _service.Run("configure", "claude", _profile, _cli, _proxy, ServerUrl);
        var repeated = _service.Run("configure", "claude", _profile, _cli, _proxy, ServerUrl);

        Assert.Equal(0, repeated.FilesChanged);
        var mcp = JsonNode.Parse(File.ReadAllText(mcpPath))!.AsObject();
        Assert.Equal("other", mcp["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.Equal(_proxy, mcp["mcpServers"]!["itoguruma"]!["command"]!.GetValue<string>());
        Assert.True(mcp["projects"]!["F:/sample"]!["hasTrustDialogAccepted"]!.GetValue<bool>());
        var hooks = JsonNode.Parse(File.ReadAllText(hooksPath))!.AsObject();
        Assert.Equal("yes", hooks["env"]!["KEEP"]!.GetValue<string>());
        Assert.Equal(2, hooks["hooks"]!["UserPromptSubmit"]!.AsArray().Count);
    }

    [Fact]
    public void UnconfigureClaude_RemovesOnlyOwnedServerAndHooks()
    {
        ConfigureClaude_WhenRepeated_PreservesOtherSettingsAndDoesNotDuplicateItoguruma();
        _service.Run("unconfigure", "claude", _profile, _cli, string.Empty, string.Empty);

        var mcp = JsonNode.Parse(File.ReadAllText(Path.Combine(_profile, ".claude.json")))!.AsObject();
        Assert.False(mcp["mcpServers"]!.AsObject().ContainsKey("itoguruma"));
        Assert.True(mcp["mcpServers"]!.AsObject().ContainsKey("other"));
        var hooks = JsonNode.Parse(File.ReadAllText(Path.Combine(_profile, ".claude", "settings.json")))!.AsObject();
        Assert.Single(hooks["hooks"]!["UserPromptSubmit"]!.AsArray());
    }

    [Fact]
    public void Configure_WhenExistingJsonIsInvalid_LeavesAllFilesUnchanged()
    {
        var directory = Path.Combine(_profile, ".codex");
        Directory.CreateDirectory(directory);
        var invalidHooks = Path.Combine(directory, "hooks.json");
        File.WriteAllText(invalidHooks, "{invalid");

        Assert.ThrowsAny<Exception>(() => _service.Run("configure", "codex", _profile, _cli, _proxy, ServerUrl));

        Assert.False(File.Exists(Path.Combine(directory, "config.toml")));
        Assert.Equal("{invalid", File.ReadAllText(invalidHooks));
    }

    [Fact]
    public void Configure_WhenAFileCannotBeReplaced_RollsBackEarlierFiles()
    {
        var directory = Path.Combine(_profile, ".codex");
        Directory.CreateDirectory(Path.Combine(directory, "hooks.json"));

        Assert.ThrowsAny<Exception>(() => _service.Run("configure", "codex", _profile, _cli, _proxy, ServerUrl));

        Assert.False(File.Exists(Path.Combine(directory, "config.toml")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Unconfigure_WhenNothingIsConfigured_DoesNotCreateOrRewriteFiles()
    {
        Directory.CreateDirectory(Path.Combine(_profile, ".codex"));
        var unrelated = Path.Combine(_profile, ".codex", "config.toml");
        File.WriteAllText(unrelated, "theme = \"dark\"\r\n");
        var before = File.ReadAllBytes(unrelated);

        var codex = _service.Run("unconfigure", "codex", _profile, _cli, string.Empty, string.Empty);
        var claude = _service.Run("unconfigure", "claude", _profile, _cli, string.Empty, string.Empty);

        Assert.Equal(0, codex.FilesChanged);
        Assert.Equal(0, claude.FilesChanged);
        Assert.Equal(before, File.ReadAllBytes(unrelated));
        Assert.False(File.Exists(Path.Combine(_profile, ".claude.json")));
        Assert.False(File.Exists(Path.Combine(_profile, ".claude", "settings.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_profile)) Directory.Delete(_profile, recursive: true);
    }

    private static int Count(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
