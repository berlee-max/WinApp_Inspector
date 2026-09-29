using WinAppInspector.Core.IO;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Core.Tests.Rules;

public class ProtectedPathRuleTests
{
    private readonly ProtectedPathRule _rule = new(WindowsKnownFolders.CreateDefault("Alice"));

    // §11: the explicit list.
    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\SysWOW64")]
    [InlineData(@"C:\Windows\WinSxS")]
    [InlineData(@"C:\Windows\Installer")]
    [InlineData(@"C:\Program Files\WindowsApps")]
    public void Section_11_directories_are_protected(string path)
    {
        _rule.Check(path).IsProtected.Should().BeTrue();
        _rule.ProtectedSubtrees.Should().Contain(p => WindowsPath.AreEqual(p, path));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc", PathProtectionKind.WindowsDirectory)]
    [InlineData(@"c:\WINDOWS\temp\x", PathProtectionKind.WindowsDirectory)]
    [InlineData(@"C:/Windows/WinSxS/foo", PathProtectionKind.WindowsDirectory)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.Foo_1.0_x64__8wekyb3d8bbwe", PathProtectionKind.WindowsApps)]
    public void Children_of_protected_subtrees_are_protected(string path, PathProtectionKind kind)
    {
        var check = _rule.Check(path);
        check.IsProtected.Should().BeTrue();
        check.Kind.Should().Be(kind);
        check.MatchedRoot.Should().NotBeNull();
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"c:")]
    public void Drive_roots_are_protected(string path)
    {
        _rule.Check(path).Kind.Should().Be(PathProtectionKind.DriveRoot);
    }

    [Theory]
    [InlineData(@"C:\Program Files", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Program Files (x86)", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\ProgramData", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Users\Alice\AppData", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Users\Alice\AppData\Local", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Users\Alice\AppData\Roaming\", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Users\Alice\AppData\LocalLow", PathProtectionKind.ScanRoot)]
    [InlineData(@"C:\Users", PathProtectionKind.UserProfileRoot)]
    [InlineData(@"C:\Users\Alice", PathProtectionKind.UserProfileRoot)]
    [InlineData(@"C:\Users\Bob", PathProtectionKind.UserProfileRoot)]
    [InlineData(@"C:\Users\Public", PathProtectionKind.UserProfileRoot)]
    [InlineData(@"C:\Users\Default\", PathProtectionKind.UserProfileRoot)]
    public void Scan_roots_themselves_are_protected(string path, PathProtectionKind kind)
    {
        var check = _rule.Check(path);
        check.IsProtected.Should().BeTrue();
        check.Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Foo")]
    [InlineData(@"C:\Program Files (x86)\Foo\bin")]
    [InlineData(@"C:\ProgramData\Foo")]
    [InlineData(@"C:\Users\Alice\AppData\Local\Foo")]
    [InlineData(@"C:\Users\Alice\AppData\Roaming\Foo\Cache")]
    [InlineData(@"C:\Users\Alice\AppData\LocalLow\Foo")]
    [InlineData(@"C:\Windows2\Foo")]
    [InlineData(@"C:\Program Files\WindowsAppsBackup")]
    [InlineData(@"D:\Portable\Foo")]
    public void Application_directories_under_scan_roots_are_allowed(string path)
    {
        _rule.Check(path).Should().Be(PathProtection.Allowed);
        _rule.IsProtected(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Foo")]
    [InlineData(@"Foo\Bar")]
    [InlineData(@"\\server\share\Foo")]
    public void Relative_empty_and_unc_paths_are_never_deletable(string? path)
    {
        var check = _rule.Check(path);
        check.IsProtected.Should().BeTrue();
        check.Kind.Should().Be(PathProtectionKind.NotAbsolute);
    }

    [Fact]
    public void Custom_system_root_is_honoured()
    {
        var folders = WindowsKnownFolders.CreateDefault() with { SystemRoot = @"D:\WINNT" };
        var rule = new ProtectedPathRule(folders);

        rule.IsProtected(@"D:\WINNT\System32").Should().BeTrue();
        rule.IsProtected(@"C:\Windows\System32").Should().BeFalse("the real system root is D:\\WINNT in this configuration");
    }

    [Fact]
    public void Default_rule_uses_drive_c_layout()
    {
        ProtectedPathRule.Default.IsProtected(@"C:\Windows\System32").Should().BeTrue();
    }
}
