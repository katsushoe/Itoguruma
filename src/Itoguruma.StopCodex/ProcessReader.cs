using System.ComponentModel;
using System.Diagnostics;

namespace Itoguruma.StopCodex;

internal static class ProcessReader
{
    public static IReadOnlyList<ProcessInfo> ReadAll()
    {
        var result = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    result.Add(new ProcessInfo(
                        process.Id,
                        $"{process.ProcessName}.exe",
                        process.MainModule?.FileName));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    result.Add(new ProcessInfo(process.Id, $"{process.ProcessName}.exe", null));
                }
            }
        }

        return result;
    }
}
