namespace Itoguruma.Viewer;

using Itoguruma.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppLocalization.ConfigureFromEnvironment();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault()));
    }
}
