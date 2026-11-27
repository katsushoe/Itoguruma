namespace Itoguruma.Viewer;

using Itoguruma.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instanceMutex = new Mutex(initiallyOwned: true, "Local\\ItogurumaViewer", out var isFirstInstance);
        if (!isFirstInstance) return;
        var installationRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        var configDirectory = Environment.GetEnvironmentVariable("ITOGURUMA_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDirectory))
            Environment.SetEnvironmentVariable("ITOGURUMA_CONFIG_DIR", Path.Combine(installationRoot, "config"));
        AppLocalization.ConfigureFromEnvironment();
        ApplicationConfiguration.Initialize();
        var startHidden = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        var databasePath = args.FirstOrDefault(argument => !string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            var installedDatabase = Path.Combine(installationRoot, "data", "messages.db");
            if (File.Exists(installedDatabase)) databasePath = installedDatabase;
        }
        using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Itoguruma.ico")
            ?? throw new InvalidOperationException("The Itoguruma icon resource is missing.");
        using var icon = new Icon(iconStream);
        using var form = new MainForm(databasePath, startHidden, icon);
        Application.Run(form);
    }
}
