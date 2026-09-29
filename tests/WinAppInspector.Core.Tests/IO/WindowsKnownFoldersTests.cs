using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Tests.IO;

public class WindowsKnownFoldersTests
{
    private readonly WindowsKnownFolders _folders = WindowsKnownFolders.CreateDefault("Alice");

    [Fact]
    public void CreateDefault_lays_out_conventional_drive_c()
    {
        _folders.SystemRoot.Should().Be(@"C:\Windows");
        _folders.System32.Should().Be(@"C:\Windows\System32");
        _folders.WindowsApps.Should().Be(@"C:\Program Files\WindowsApps");
        _folders.LocalLowAppData.Should().Be(@"C:\Users\Alice\AppData\LocalLow");
    }

    [Theory]
    [InlineData(@"C:\Program Files\Foo", ScanRoot.ProgramFiles)]
    [InlineData(@"C:\Program Files (x86)\Foo", ScanRoot.ProgramFilesX86)]
    [InlineData(@"C:\ProgramData\Foo", ScanRoot.ProgramData)]
    [InlineData(@"C:\Users\Alice\AppData\Local\Foo", ScanRoot.LocalAppData)]
    [InlineData(@"C:\Users\Alice\AppData\LocalLow\Foo", ScanRoot.LocalLowAppData)]
    [InlineData(@"C:\Users\Alice\AppData\Roaming\Foo", ScanRoot.RoamingAppData)]
    [InlineData(@"C:\Users\Alice\Downloads\Foo", ScanRoot.Custom)]
    [InlineData(@"D:\Tools\Foo", ScanRoot.Custom)]
    [InlineData(@"C:\Users\Bob\AppData\Local\Foo", ScanRoot.Custom)]
    public void RootOf_identifies_the_containing_scan_root(string path, ScanRoot expected)
    {
        _folders.RootOf(path).Should().Be(expected);
    }

    [Fact]
    public void PathOf_round_trips_every_fixed_root()
    {
        foreach (var root in Enum.GetValues<ScanRoot>().Where(r => r != ScanRoot.Custom))
        {
            _folders.RootOf(_folders.PathOf(root)).Should().Be(root);
        }
    }

    [Fact]
    public void PathOf_custom_throws()
    {
        var act = () => _folders.PathOf(ScanRoot.Custom);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromEnvironment_is_windows_only()
    {
        if (OperatingSystem.IsWindows())
        {
            var real = WindowsKnownFolders.FromEnvironment();
            real.SystemRoot.Should().NotBeNullOrEmpty();
            real.RootOf(real.LocalAppData).Should().Be(ScanRoot.LocalAppData);
        }
        else
        {
            var act = () => WindowsKnownFolders.FromEnvironment();
            act.Should().Throw<PlatformNotSupportedException>();
        }
    }
}
