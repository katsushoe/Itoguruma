using System.Text.Json;

namespace Itoguruma.Core;

/// <summary>アプリケーション全体の表示言語を管理します。</summary>
public static class AppLocalization
{
    private const string DefaultLanguage = "en";
    private static string _language = DefaultLanguage;

    /// <summary>現在の言語コードを取得します。</summary>
    public static string Language => _language;

    /// <summary>日本語が選択されているかどうかを取得します。</summary>
    public static bool IsJapanese => string.Equals(_language, "ja", StringComparison.Ordinal);

    /// <summary>言語コードを設定します。</summary>
    public static void Configure(string? language) =>
        _language = string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : DefaultLanguage;

    /// <summary>構成ディレクトリの appsettings.json から言語を読み込みます。</summary>
    public static void ConfigureFromEnvironment()
    {
        var configDirectory = Environment.GetEnvironmentVariable("ITOGURUMA_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDirectory)) return;
        var path = Path.Combine(configDirectory, "appsettings.json");
        if (!File.Exists(path)) return;
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("Itoguruma", out var section)
            && section.TryGetProperty("Language", out var value))
        {
            Configure(value.GetString());
        }
    }

    /// <summary>英語または日本語の文字列を返します。</summary>
    public static string Text(string english, string japanese) => IsJapanese ? japanese : english;
}
