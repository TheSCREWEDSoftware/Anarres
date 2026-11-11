using System.ComponentModel;
using System.Diagnostics;

namespace Anarres.App;

// Opens folders with the system's own file manager
public static class SystemShell
{
    // Returns null when it opened, or what went wrong
    public static string? Open(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            ProcessStartInfo start = OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe")
                                     : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open")
                                     : new ProcessStartInfo("xdg-open");
            start.ArgumentList.Add(folder);
            start.UseShellExecute = false;
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or
                                              UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }
}
