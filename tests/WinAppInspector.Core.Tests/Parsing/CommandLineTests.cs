using WinAppInspector.Core.IO;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Core.Tests.Parsing;

public class CommandLineTests
{
    private readonly WindowsKnownFolders _folders = WindowsKnownFolders.CreateDefault("Alice");

    [Theory]
    [InlineData(@"""C:\Program Files\App\unins000.exe"" /SILENT", @"C:\Program Files\App\unins000.exe", "/SILENT")]
    [InlineData(@"C:\App\app.exe --flag --other", @"C:\App\app.exe", "--flag --other")]
    [InlineData(@"C:\Program Files\App\app.EXE /uninstall", @"C:\Program Files\App\app.EXE", "/uninstall")]
    [InlineData(@"MsiExec.exe /X{GUID}", @"MsiExec.exe", "/X{GUID}")]
    [InlineData(@"rundll32.exe ""C:\x\y.dll"",Entry", @"rundll32.exe", @"""C:\x\y.dll"",Entry")]
    [InlineData(@"C:\Windows\system32\svchost.exe -k netsvcs -p", @"C:\Windows\system32\svchost.exe", "-k netsvcs -p")]
    [InlineData(@"C:\Drivers\foo.sys", @"C:\Drivers\foo.sys", null)]
    [InlineData(@"""C:\App\app.exe""", @"C:\App\app.exe", null)]
    [InlineData(@"C:\App\app.exe", @"C:\App\app.exe", null)]
    public void Executable_and_arguments_are_split(string command, string exe, string? args)
    {
        CommandLine.ExtractExecutable(command).Should().Be(exe);
        CommandLine.ExtractArguments(command).Should().Be(args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("\"")]
    public void Empty_commands_yield_null(string? command)
    {
        CommandLine.ExtractExecutable(command).Should().BeNull();
        CommandLine.ExtractArguments(command).Should().BeNull();
    }

    [Fact]
    public void Environment_variables_are_expanded_case_insensitively_and_unknown_ones_kept()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ProgramFiles"] = @"C:\Program Files" };

        CommandLine.ExpandEnvironmentVariables(@"%PROGRAMFILES%\App\app.exe %Unknown% 100%", env)
            .Should().Be(@"C:\Program Files\App\app.exe %Unknown% 100%");
        CommandLine.ExpandEnvironmentVariables(null, env).Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Foo\FooSvc.exe"" -service", @"C:\Program Files\Foo\FooSvc.exe")]
    [InlineData(@"C:\Program Files\Foo\FooSvc.exe -service", @"C:\Program Files\Foo\FooSvc.exe")]
    [InlineData(@"C:\WINDOWS\system32\svchost.exe -k LocalService -p", @"C:\WINDOWS\system32\svchost.exe")]
    [InlineData(@"%SystemRoot%\System32\svchost.exe -k netsvcs", @"C:\Windows\System32\svchost.exe")]
    [InlineData(@"\SystemRoot\System32\drivers\afd.sys", @"C:\Windows\System32\drivers\afd.sys")]
    [InlineData(@"system32\DRIVERS\foo.sys", @"C:\Windows\system32\DRIVERS\foo.sys")]
    [InlineData(@"System32\drivers\bar.sys", @"C:\Windows\System32\drivers\bar.sys")]
    [InlineData(@"\??\C:\Program Files\Foo\driver.sys", @"C:\Program Files\Foo\driver.sys")]
    [InlineData(@"""%ProgramFiles(x86)%\Foo\svc.exe""", @"C:\Program Files (x86)\Foo\svc.exe")]
    [InlineData(@"svchost.exe -k foo", @"C:\Windows\System32\svchost.exe")]
    public void Service_image_paths_are_resolved(string pathName, string expected)
    {
        CommandLine.ResolveServiceImagePath(pathName, _folders).Should().Be(expected);
    }

    [Fact]
    public void Empty_service_path_yields_null()
    {
        CommandLine.ResolveServiceImagePath(null, _folders).Should().BeNull();
        CommandLine.ResolveServiceImagePath("  ", _folders).Should().BeNull();
    }

    [Theory]
    [InlineData(@"""C:\Users\Alice\AppData\Local\Foo\Foo.exe"" --autostart", @"C:\Users\Alice\AppData\Local\Foo\Foo.exe")]
    [InlineData(@"%LOCALAPPDATA%\Foo\Update.exe --processStart Foo.exe", @"C:\Users\Alice\AppData\Local\Foo\Update.exe")]
    [InlineData(@"""%ProgramFiles%\Foo\Foo.exe"" /background", @"C:\Program Files\Foo\Foo.exe")]
    [InlineData(@"%APPDATA%\Spotify\Spotify.exe --minimized", @"C:\Users\Alice\AppData\Roaming\Spotify\Spotify.exe")]
    public void Startup_commands_are_resolved(string command, string expected)
    {
        CommandLine.ResolveCommandExecutable(command, _folders).Should().Be(expected);
    }

    [Fact]
    public void Known_folders_export_the_usual_environment_variables()
    {
        var env = _folders.ToEnvironment();

        env["SystemRoot"].Should().Be(@"C:\Windows");
        env["programfiles(x86)"].Should().Be(@"C:\Program Files (x86)");
        env["LocalAppData"].Should().Be(@"C:\Users\Alice\AppData\Local");
        env["SystemDrive"].Should().Be("C:");
    }
}
