using System.Text.Json;

namespace Itoguruma.Viewer;

internal sealed record ViewerSettings(bool AutoUpdate = true, bool StartWithWindows = true)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Itoguruma", "viewer-settings.json");

    public static ViewerSettings Load()
        => Load(SettingsPath);

    public static ViewerSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public void Save()
        => Save(SettingsPath);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
