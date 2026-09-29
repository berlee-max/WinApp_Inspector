using WinAppInspector.Analysis.Search;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Tests.Search;

public class SearchMatcherTests
{
    private static readonly ApplicationEntity WeChat = new()
    {
        Id = "x",
        Name = "微信",
        Publisher = "Tencent Technology (Shenzhen) Company Limited",
        InstallLocation = @"C:\Program Files\Tencent\WeChat",
        MainExecutable = @"C:\Program Files\Tencent\WeChat\WeChat.exe",
        Directories = [new AppDirectory { Path = @"C:\Users\U\AppData\Roaming\Tencent\WeChat", ExecutablePaths = [@"C:\Users\U\AppData\Roaming\Tencent\WeChat\WeChatUpdate.exe"] }],
        Services = [new ServiceRecord { Name = "WeChatSvc" }],
        ScheduledTasks = [new ScheduledTaskRecord { TaskName = "WeChatUpdateTask", TaskPath = "\\" }],
        RegistryEntries = [new RegistryUninstallEntry { Scope = RegistryScope.MachineNative, KeyName = "WeChat", KeyPath = @"HKEY_LOCAL_MACHINE\...\Uninstall\WeChat" }],
    };

    [Theory]
    [InlineData("微信")]
    [InlineData("wechat")]
    [InlineData("Tencent")]
    [InlineData("WeChat.exe")]
    [InlineData("AppData\\Roaming\\Tencent")]
    [InlineData("AppData/Roaming/Tencent")]
    [InlineData("WeChatSvc")]
    [InlineData("UpdateTask")]
    [InlineData("Uninstall\\WeChat")]
    [InlineData("tencent wechat")]
    [InlineData("")]
    [InlineData(null)]
    public void Matches_every_field_from_section_43(string? query)
    {
        SearchMatcher.Matches(WeChat, query).Should().BeTrue();
    }

    [Theory]
    [InlineData("Bing")]
    [InlineData("Microsoft")]
    [InlineData("tencent spotify")]
    public void Does_not_match_unrelated_terms(string query)
    {
        SearchMatcher.Matches(WeChat, query).Should().BeFalse();
    }
}
