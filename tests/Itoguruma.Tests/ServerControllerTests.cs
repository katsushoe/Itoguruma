using Itoguruma.Viewer;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ServerControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "itoguruma-server-controller-tests", Guid.NewGuid().ToString("N"));

    public ServerControllerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ResolveHealthUri_WhenConfiguredServerUrlExists_UsesItsOrigin()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"),
            """{ "Itoguruma": { "ServerUrl": "http://127.0.0.1:48888/" } }""");

        Assert.Equal("http://127.0.0.1:48888/health", ServerController.ResolveHealthUri(_directory).AbsoluteUri);
    }

    [Fact]
    public void ResolveHealthUri_WhenSettingsAreMissing_UsesDefault()
    {
        Assert.Equal("http://127.0.0.1:47631/health", ServerController.ResolveHealthUri(_directory).AbsoluteUri);
    }

    [Theory]
    [InlineData("""{ "Itoguruma": { "ServerUrl": "http://example.com:47631" } }""")]
    [InlineData("not json")]
    public void ResolveHealthUri_WhenSettingIsNotLoopbackOrInvalid_UsesDefault(string content)
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), content);

        Assert.Equal("http://127.0.0.1:47631/health", ServerController.ResolveHealthUri(_directory).AbsoluteUri);
    }

    [Theory]
    [InlineData("""{"status":"ok","version":"0.3.47"}""", "0.3.47")]
    [InlineData("""{"status":"ok"}""", "")]
    [InlineData("""{"status":"degraded","version":"0.3.47"}""", null)]
    [InlineData("""[]""", null)]
    [InlineData("<html>", null)]
    public void ParseHealthVersion_ReturnsVersionOnlyWhenStatusIsOk(string json, string? expected)
    {
        Assert.Equal(expected, ServerController.ParseHealthVersion(json));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
