using WinAppInspector.Core.IO;

namespace WinAppInspector.Core.Tests.IO;

public class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App\", @"C:\Program Files\App")]
    [InlineData(@"C:/Program Files/App", @"C:\Program Files\App")]
    [InlineData(@"  C:\App\\Sub\  ", @"C:\App\Sub")]
    [InlineData(@"""C:\App\app.exe""", @"C:\App\app.exe")]
    [InlineData(@"\\?\C:\Windows\System32", @"C:\Windows\System32")]
    [InlineData(@"\\?\UNC\server\share\dir", @"\\server\share\dir")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:", @"C:\")]
    [InlineData(@"c:\\", @"c:\")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_produces_canonical_form(string? input, string expected)
    {
        WindowsPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32", @"C:\Windows", true)]
    [InlineData(@"c:\windows\system32\drivers", @"C:\WINDOWS", true)]
    [InlineData(@"C:\Windows", @"C:\Windows", true)]
    [InlineData(@"C:\Windows\", @"C:\Windows", true)]
    [InlineData(@"C:\WindowsOld\foo", @"C:\Windows", false)]
    [InlineData(@"C:\Program Files (x86)\App", @"C:\Program Files", false)]
    [InlineData(@"C:\Users\Alice\AppData\Local\App", @"C:\Users\Alice\AppData\Local", true)]
    [InlineData(@"D:\Apps\Foo", @"C:\", false)]
    [InlineData(@"C:\Apps\Foo", @"C:\", true)]
    [InlineData("", @"C:\Windows", false)]
    [InlineData(@"C:\Windows", "", false)]
    public void IsSameOrUnder_uses_segment_boundaries_and_ignores_case(string path, string root, bool expected)
    {
        WindowsPath.IsSameOrUnder(path, root).Should().Be(expected);
    }

    [Fact]
    public void IsUnder_excludes_the_root_itself()
    {
        WindowsPath.IsUnder(@"C:\Windows", @"C:\Windows").Should().BeFalse();
        WindowsPath.IsUnder(@"C:\Windows\System32", @"C:\Windows").Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe", "app.exe")]
    [InlineData(@"C:\Program Files\App\", "App")]
    [InlineData(@"C:\", "")]
    [InlineData("app.exe", "app.exe")]
    public void GetFileName_returns_last_segment(string path, string expected)
    {
        WindowsPath.GetFileName(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App")]
    [InlineData(@"C:\App", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData("app.exe", null)]
    public void GetDirectoryName_returns_parent(string path, string? expected)
    {
        WindowsPath.GetDirectoryName(path).Should().Be(expected);
    }

    [Fact]
    public void Combine_joins_with_single_backslash()
    {
        WindowsPath.Combine(@"C:\", "Users", "Alice").Should().Be(@"C:\Users\Alice");
        WindowsPath.Combine(@"C:\Program Files\", @"\App").Should().Be(@"C:\Program Files\App");
        WindowsPath.Combine(@"C:\Program Files", "").Should().Be(@"C:\Program Files");
    }

    [Theory]
    [InlineData(@"""C:\Program Files\App\unins000.exe"" /SILENT", @"C:\Program Files\App\unins000.exe")]
    [InlineData(@"C:\App\app.exe --flag", @"C:\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.EXE /uninstall", @"C:\Program Files\App\app.EXE")]
    [InlineData(@"MsiExec.exe /X{GUID}", @"MsiExec.exe")]
    [InlineData(@"""C:\App\app.exe""", @"C:\App\app.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractExecutable_handles_quoted_and_unquoted_commands(string? command, string? expected)
    {
        WindowsPath.ExtractExecutable(command).Should().Be(expected);
    }
}
