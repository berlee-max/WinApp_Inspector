using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace WinAppInspector.UI.Services;

/// <summary>
/// Last line of defence for exceptions nobody caught: writes a crash log under the data folder and tells the user
/// where it is. Without this a start-up failure (a bad XAML resource, a missing dll) ends the process before any
/// window exists and the user sees nothing at all.
/// </summary>
public static class CrashReporter
{
    /// <summary>Exit code reported by <c>--self-test</c> when the app crashed instead of reaching its window.</summary>
    public const int SelfTestCrashExitCode = 2;

    private static int _reported;

    /// <summary>Writes <paramref name="exception"/> to a timestamped log and returns the log path (or null if even that failed).</summary>
    public static string? WriteLog(Exception exception, string origin)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var text = new StringBuilder()
            .AppendLine($"WinApp Inspector crash report ({origin})")
            .AppendLine($"Time: {DateTimeOffset.Now:O}")
            .AppendLine($"Version: {typeof(CrashReporter).Assembly.GetName().Version}")
            .AppendLine($"Process: {Environment.ProcessPath}")
            .AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")})")
            .AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();

        foreach (var directory in new[] { Path.Combine(App.DataDirectory, "logs"), Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.WriteAllText(path, text);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try the next location
            }
        }

        return null;
    }

    /// <summary>Logs the exception and, unless running the automated self-test, shows a message box with the log path.</summary>
    public static void Report(Exception exception, string origin, bool selfTest)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var path = WriteLog(exception, origin);
        Debug.WriteLine(exception);
        Trace.TraceError("{0}: {1}", origin, exception);

        if (selfTest || Interlocked.Exchange(ref _reported, 1) == 1)
        {
            return;
        }

        // Deliberately not a resource-file string: the resource dictionaries are one of the things that can fail here.
        var message = "WinApp Inspector 启动或运行时发生错误，程序无法继续。\n\n"
            + $"{exception.GetType().Name}: {exception.Message}\n\n"
            + (path is null ? "崩溃日志无法写入。" : $"详细日志已保存到：\n{path}");
        try
        {
            MessageBox.Show(message, "WinApp Inspector", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // no desktop / no message loop: the log file is all we can leave behind
        }
    }
}
