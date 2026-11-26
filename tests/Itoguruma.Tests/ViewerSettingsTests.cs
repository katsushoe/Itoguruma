using Itoguruma.Viewer;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ViewerSettingsTests
{
    [Fact]
    public void Load_WhenSettingsFileDoesNotExist_UsesEnabledDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        var settings = ViewerSettings.Load(path);

        Assert.True(settings.AutoUpdate);
        Assert.True(settings.StartWithWindows);
    }

    [Fact]
    public void SaveAndLoad_PreservesBothSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "itoguruma-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var expected = new ViewerSettings(AutoUpdate: false, StartWithWindows: true);

            expected.Save(path);
            var actual = ViewerSettings.Load(path);

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenSettingsFileIsMalformed_UsesEnabledDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "itoguruma-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{");

            var settings = ViewerSettings.Load(path);

            Assert.True(settings.AutoUpdate);
            Assert.True(settings.StartWithWindows);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
