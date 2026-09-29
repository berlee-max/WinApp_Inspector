using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Core.Tests.Parsing;

public class RegistryUninstallEntryParserTests
{
    private static readonly string[] MultiStringPublisher = ["A", "", "B"];

    private static RegistryUninstallEntry ParseChrome() => RegistryUninstallEntryParser.Parse(
        RegistryScope.MachineNative,
        "Google Chrome",
        new Dictionary<string, object?>
        {
            ["DisplayName"] = "Google Chrome",
            ["DisplayVersion"] = "128.0.6613.120",
            ["Publisher"] = "Google LLC",
            ["InstallLocation"] = @"C:\Program Files\Google\Chrome\Application",
            ["InstallDate"] = "20240915",
            ["EstimatedSize"] = 654321,
            ["UninstallString"] = @"""C:\Program Files\Google\Chrome\Application\128.0.6613.120\Installer\setup.exe"" --uninstall --system-level",
            ["DisplayIcon"] = @"C:\Program Files\Google\Chrome\Application\chrome.exe,0",
            ["NoModify"] = 1,
        });

    [Fact]
    public void Parses_string_and_dword_values()
    {
        var entry = ParseChrome();

        entry.Scope.Should().Be(RegistryScope.MachineNative);
        entry.KeyName.Should().Be("Google Chrome");
        entry.KeyPath.Should().Be(@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome");
        entry.DisplayName.Should().Be("Google Chrome");
        entry.DisplayVersion.Should().Be("128.0.6613.120");
        entry.Publisher.Should().Be("Google LLC");
        entry.InstallLocation.Should().Be(@"C:\Program Files\Google\Chrome\Application");
        entry.InstallDate.Should().Be("20240915");
        entry.EstimatedSizeKb.Should().Be(654321);
        entry.UninstallString.Should().StartWith("\"C:\\Program Files\\Google");
        entry.QuietUninstallString.Should().BeNull();
        entry.WindowsInstaller.Should().BeFalse();
        entry.SystemComponent.Should().BeFalse();
    }

    [Fact]
    public void Value_names_are_case_insensitive_and_blank_strings_become_null()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.CurrentUser, "Foo", new Dictionary<string, object?>
        {
            ["displayname"] = "  Foo  ",
            ["PUBLISHER"] = "   ",
            ["InstallLocation"] = "Path\0\0",
        });

        entry.DisplayName.Should().Be("Foo");
        entry.Publisher.Should().BeNull();
        entry.InstallLocation.Should().Be("Path");
    }

    [Fact]
    public void Flags_accept_dword_and_string_forms()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.MachineWow6432, "{X}", new Dictionary<string, object?>
        {
            ["SystemComponent"] = 1,
            ["WindowsInstaller"] = "1",
        });

        entry.SystemComponent.Should().BeTrue();
        entry.WindowsInstaller.Should().BeTrue();
        entry.KeyPath.Should().StartWith(@"HKEY_LOCAL_MACHINE\Software\WOW6432Node\");
    }

    [Fact]
    public void Multi_string_values_are_joined_and_large_dwords_are_unsigned()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.MachineNative, "K", new Dictionary<string, object?>
        {
            ["Publisher"] = MultiStringPublisher,
            ["EstimatedSize"] = unchecked((int)0xFFFFFFF0),
        });

        entry.Publisher.Should().Be("A; B");
        entry.EstimatedSizeKb.Should().Be(0xFFFFFFF0L);
    }

    [Theory]
    [InlineData("20240915", 2024, 9, 15)]
    [InlineData("2024-09-15", 2024, 9, 15)]
    [InlineData("2024/09/15", 2024, 9, 15)]
    [InlineData("9/15/2024", 2024, 9, 15)]
    [InlineData("20240915103000", 2024, 9, 15)]
    public void InstallDate_formats_are_parsed(string raw, int year, int month, int day)
    {
        RegistryUninstallEntryParser.ParseInstallDate(raw).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("20241332")]
    public void Unparseable_InstallDate_is_null(string? raw)
    {
        RegistryUninstallEntryParser.ParseInstallDate(raw).Should().BeNull();
    }

    [Theory]
    [InlineData("{7C39C2D2-6B0A-4C3E-9C4E-9F5A6B7C8D9E}", true)]
    [InlineData("{7c39c2d2-6b0a-4c3e-9c4e-9f5a6b7c8d9e}", true)]
    [InlineData("7C39C2D2-6B0A-4C3E-9C4E-9F5A6B7C8D9E", false)]
    [InlineData("Google Chrome", false)]
    [InlineData("{7C39C2D2-6B0A-4C3E-9C4E-9F5A6B7C8D9E}_is1", false)]
    public void Guid_key_names_are_detected(string keyName, bool expected)
    {
        RegistryUninstallEntryParser.IsGuidKeyName(keyName).Should().Be(expected);
    }

    [Fact]
    public void Msi_product_code_comes_from_msiexec_command_first()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.MachineNative, "{AAAAAAAA-0000-0000-0000-000000000001}", new Dictionary<string, object?>
        {
            ["UninstallString"] = "MsiExec.exe /X{bbbbbbbb-0000-0000-0000-000000000002}",
            ["WindowsInstaller"] = 1,
        });

        RegistryUninstallEntryParser.GetMsiProductCode(entry).Should().Be("{BBBBBBBB-0000-0000-0000-000000000002}");
        RegistryUninstallEntryParser.PreferredUninstallMethod(entry).Should().Be(UninstallMethod.Msi);
    }

    [Fact]
    public void Msi_product_code_falls_back_to_guid_key_when_WindowsInstaller_flag_is_set()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.MachineNative, "{AAAAAAAA-0000-0000-0000-000000000001}", new Dictionary<string, object?>
        {
            ["WindowsInstaller"] = 1,
        });

        RegistryUninstallEntryParser.GetMsiProductCode(entry).Should().Be("{AAAAAAAA-0000-0000-0000-000000000001}");
    }

    [Fact]
    public void Non_msi_entries_have_no_product_code()
    {
        RegistryUninstallEntryParser.GetMsiProductCode(ParseChrome()).Should().BeNull();
        RegistryUninstallEntryParser.PreferredUninstallMethod(ParseChrome()).Should().Be(UninstallMethod.UninstallString);
    }

    [Fact]
    public void Quiet_uninstall_is_preferred_over_everything()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.CurrentUser, "Foo_is1", new Dictionary<string, object?>
        {
            ["UninstallString"] = @"""C:\Foo\unins000.exe""",
            ["QuietUninstallString"] = @"""C:\Foo\unins000.exe"" /SILENT",
        });

        RegistryUninstallEntryParser.PreferredUninstallMethod(entry).Should().Be(UninstallMethod.QuietUninstallString);
    }

    [Fact]
    public void Entry_without_uninstall_command_has_no_method()
    {
        var entry = RegistryUninstallEntryParser.Parse(RegistryScope.CurrentUser, "Foo", new Dictionary<string, object?> { ["DisplayName"] = "Foo" });
        RegistryUninstallEntryParser.PreferredUninstallMethod(entry).Should().Be(UninstallMethod.None);
    }

    [Fact]
    public void Visible_entries_match_apps_and_features()
    {
        RegistryUninstallEntryParser.IsVisibleInAppsAndFeatures(ParseChrome()).Should().BeTrue();

        Hidden(new() { ["DisplayName"] = "X", ["SystemComponent"] = 1 }).Should().BeFalse("SystemComponent");
        Hidden(new() { ["DisplayName"] = "X", ["ParentKeyName"] = "OperatingSystem" }).Should().BeFalse("child entry");
        Hidden(new() { ["DisplayName"] = "X", ["ReleaseType"] = "Security Update" }).Should().BeFalse("update");
        Hidden(new() { ["DisplayName"] = "Update for Microsoft Office (KB123)" }).Should().BeFalse("KB name");
        Hidden(new() { ["UninstallString"] = "x" }).Should().BeFalse("no DisplayName");
        Hidden(new() { ["DisplayName"] = "X" }).Should().BeTrue();

        static bool Hidden(Dictionary<string, object?> values) =>
            RegistryUninstallEntryParser.IsVisibleInAppsAndFeatures(RegistryUninstallEntryParser.Parse(RegistryScope.MachineNative, "K", values));
    }
}
