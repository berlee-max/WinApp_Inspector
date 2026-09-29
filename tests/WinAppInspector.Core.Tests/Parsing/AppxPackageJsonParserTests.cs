using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Core.Tests.Parsing;

public class AppxPackageJsonParserTests
{
    private const string TwoPackages = """
        [
          {
            "Name": "Microsoft.WindowsCalculator",
            "PackageFullName": "Microsoft.WindowsCalculator_11.2409.0.0_x64__8wekyb3d8bbwe",
            "PackageFamilyName": "Microsoft.WindowsCalculator_8wekyb3d8bbwe",
            "Publisher": "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            "InstallLocation": "C:\\Program Files\\WindowsApps\\Microsoft.WindowsCalculator_11.2409.0.0_x64__8wekyb3d8bbwe\\",
            "IsFramework": false,
            "NonRemovable": false,
            "IsBundle": false,
            "Version": "11.2409.0.0",
            "Architecture": "X64",
            "SignatureKind": "Store"
          },
          {
            "Name": "Microsoft.VCLibs.140.00",
            "PackageFullName": "Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe",
            "PackageFamilyName": "Microsoft.VCLibs.140.00_8wekyb3d8bbwe",
            "Publisher": "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            "InstallLocation": null,
            "IsFramework": true,
            "NonRemovable": true,
            "IsBundle": false,
            "Version": { "Major": 14, "Minor": 0, "Build": 33519, "Revision": 0, "MajorRevision": 0, "MinorRevision": 0 },
            "Architecture": 9,
            "SignatureKind": 4
          }
        ]
        """;

    [Fact]
    public void Parses_an_array_with_string_and_object_shaped_members()
    {
        var packages = AppxPackageJsonParser.Parse(TwoPackages);

        packages.Should().HaveCount(2);

        var calc = packages[0];
        calc.Name.Should().Be("Microsoft.WindowsCalculator");
        calc.PackageFamilyName.Should().Be("Microsoft.WindowsCalculator_8wekyb3d8bbwe");
        calc.PublisherDisplayName.Should().Be("Microsoft Corporation");
        calc.InstallLocation.Should().Be(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2409.0.0_x64__8wekyb3d8bbwe");
        calc.Version.Should().Be("11.2409.0.0");
        calc.Architecture.Should().Be("X64");
        calc.SignatureKind.Should().Be("Store");
        calc.IsFramework.Should().BeFalse();
        calc.NonRemovable.Should().BeFalse();

        var vclibs = packages[1];
        vclibs.Version.Should().Be("14.0.33519.0");
        vclibs.Architecture.Should().Be("X64");
        vclibs.SignatureKind.Should().Be("System");
        vclibs.IsFramework.Should().BeTrue();
        vclibs.NonRemovable.Should().BeTrue();
        vclibs.InstallLocation.Should().BeNull();
    }

    [Fact]
    public void Single_object_output_is_accepted()
    {
        const string json = """{"Name":"A","PackageFullName":"A_1.0.0.0_x64__abc","PackageFamilyName":"A_abc","Publisher":"CN=A Corp"}""";

        var packages = AppxPackageJsonParser.Parse(json);

        packages.Should().ContainSingle().Which.PublisherDisplayName.Should().Be("A Corp");
        packages[0].NonRemovable.Should().BeNull("not present in the output");
    }

    [Fact]
    public void Entries_missing_identity_are_skipped()
    {
        const string json = """[{"Name":"NoFullName"},{"Name":"B","PackageFullName":"B_1","PackageFamilyName":"B_x"}]""";

        AppxPackageJsonParser.Parse(json).Should().ContainSingle().Which.Name.Should().Be("B");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("\"just a string\"")]
    public void Empty_or_non_object_output_yields_no_packages(string? json)
    {
        AppxPackageJsonParser.Parse(json).Should().BeEmpty();
    }
}
