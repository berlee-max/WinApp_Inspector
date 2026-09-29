using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinAppInspector.Launcher;

/// <summary>
/// Starts <c>app\WinAppInspector.exe</c> with <c>DOTNET_ROOT</c> pointing at the package's own <c>runtime\</c> folder,
/// so the portable package needs no installed .NET yet keeps its root folder tidy. Arguments are passed through
/// unchanged (Explorer's <c>--analyze "path"</c> included). When <c>runtime\</c> is absent the app falls back to the
/// machine-wide .NET, which is what the lite package relies on.
/// </summary>
internal static partial class Program
{
    private const string AppRelativePath = @"app\WinAppInspector.exe";
    private const string RuntimeFolder = "runtime";
    private const string LauncherVariable = "WINAPPINSPECTOR_LAUNCHER";
    private const string SelfTestSwitch = "--self-test";

    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;

    private static int Main(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var app = Path.Combine(root, AppRelativePath);
        var runtime = Path.Combine(root, RuntimeFolder);

        if (!File.Exists(app))
        {
            // Not from a resource file: the launcher has no localisation layer and this must work with nothing else present.
            Fail($"找不到程序文件：\n{app}\n\n请完整解压便携版压缩包后再运行。");
            return 2;
        }

        var startInfo = new ProcessStartInfo(app)
        {
            UseShellExecute = false,
            WorkingDirectory = root,
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Directory.Exists(runtime))
        {
            startInfo.Environment["DOTNET_ROOT"] = runtime;
            startInfo.Environment["DOTNET_ROOT_X64"] = runtime;
        }

        startInfo.Environment[LauncherVariable] = Environment.ProcessPath ?? Path.Combine(root, "WinAppInspector.exe");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Fail($"无法启动程序：\n{app}");
                return 3;
            }

            // The self-test (CI) needs the app's exit code; a normal launch returns at once.
            if (Array.Exists(args, a => string.Equals(a, SelfTestSwitch, StringComparison.OrdinalIgnoreCase)))
            {
                process.WaitForExit();
                return process.ExitCode;
            }

            return 0;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            Fail($"无法启动程序：\n{app}\n\n{ex.Message}");
            return 3;
        }
    }

    private static void Fail(string text)
    {
        _ = MessageBoxW(IntPtr.Zero, text, "WinApp Inspector", MbOk | MbIconError);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
