using WinAppInspector.Analysis.Classification;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Tests.Classification;

public class KnownComponentCatalogTests
{
    private readonly KnownComponentCatalog _catalog = new();

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33130")]
    [InlineData("Microsoft .NET Runtime - 8.0.8 (x64)")]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.8 (x64)")]
    [InlineData("Microsoft Edge WebView2 Runtime")]
    [InlineData("EdgeWebView")]
    [InlineData("Java 8 Update 401")]
    [InlineData("Java(TM) SE Development Kit 17")]
    [InlineData("Python Launcher")]
    [InlineData("DirectX Runtime")]
    [InlineData("Microsoft.VCLibs.140.00")]
    public void Shared_runtime_names_from_section_9_8_are_recognised(string name)
    {
        _catalog.IsSharedRuntimeName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Google Chrome")]
    [InlineData("Spotify")]
    [InlineData("Visual Studio Code")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_apps_are_not_shared_runtimes(string? name)
    {
        _catalog.IsSharedRuntimeName(name).Should().BeFalse();
    }

    [Theory]
    [InlineData(ScanRoot.ProgramFiles, @"C:\Program Files\Common Files", true)]
    [InlineData(ScanRoot.ProgramFiles, @"C:\Program Files\Windows Defender", true)]
    [InlineData(ScanRoot.ProgramFiles, @"C:\Program Files\WindowsPowerShell", true)]
    [InlineData(ScanRoot.ProgramFilesX86, @"C:\Program Files (x86)\Internet Explorer", true)]
    [InlineData(ScanRoot.ProgramFiles, @"C:\Program Files\Google", false)]
    [InlineData(ScanRoot.ProgramData, @"C:\ProgramData\Microsoft", true)]
    [InlineData(ScanRoot.ProgramData, @"C:\ProgramData\Package Cache", true)]
    [InlineData(ScanRoot.ProgramData, @"C:\ProgramData\Spotify", false)]
    [InlineData(ScanRoot.LocalAppData, @"C:\Users\A\AppData\Local\Microsoft", true)]
    [InlineData(ScanRoot.LocalAppData, @"C:\Users\A\AppData\Local\Packages", true)]
    [InlineData(ScanRoot.LocalAppData, @"C:\Users\A\AppData\Local\Temp", true)]
    [InlineData(ScanRoot.LocalAppData, @"C:\Users\A\AppData\Local\Foo", false)]
    [InlineData(ScanRoot.RoamingAppData, @"C:\Users\A\AppData\Roaming\Microsoft", true)]
    [InlineData(ScanRoot.RoamingAppData, @"C:\Users\A\AppData\Roaming\Spotify", false)]
    [InlineData(ScanRoot.LocalLowAppData, @"C:\Users\A\AppData\LocalLow\Microsoft", true)]
    [InlineData(ScanRoot.Custom, @"D:\Microsoft", false)]
    public void System_folders_are_recognised_per_root(ScanRoot root, string path, bool expected)
    {
        _catalog.IsSystemFolder(root, path).Should().Be(expected);
    }
}
