using System.ComponentModel;
using System.Diagnostics;

namespace Anarres.App;

// Opens folders with the system's own file manager, and web pages in the browser
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

    // Returns null when it opened, or what went wrong; only http and https addresses are opened
    public static string? OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps &&
                                                                    uri.Scheme != Uri.UriSchemeHttp))
            return "Not a web address: " + url;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return exception.Message;
        }
    }
}
