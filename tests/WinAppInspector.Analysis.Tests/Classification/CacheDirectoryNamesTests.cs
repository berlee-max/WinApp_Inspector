using WinAppInspector.Analysis.Classification;

namespace WinAppInspector.Analysis.Tests.Classification;

public class CacheDirectoryNamesTests
{
    // §9.6 list
    [Theory]
    [InlineData("Cache")]
    [InlineData("Caches")]
    [InlineData("Code Cache")]
    [InlineData("GPUCache")]
    [InlineData("Temp")]
    [InlineData("Logs")]
    [InlineData("Crashpad")]
    [InlineData("CrashReports")]
    public void Names_from_the_requirements_are_recognised(string name)
    {
        CacheDirectoryNames.IsCacheOrLogName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("cache")]
    [InlineData("CACHE")]
    [InlineData(" Logs ")]
    public void Matching_is_case_insensitive_and_trims(string name)
    {
        CacheDirectoryNames.IsCacheOrLogName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Spotify")]
    [InlineData("Programs")]
    [InlineData("CacheManager")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_names_are_not_cache(string? name)
    {
        CacheDirectoryNames.IsCacheOrLogName(name).Should().BeFalse();
    }

    [Fact]
    public void Path_check_uses_last_segment()
    {
        CacheDirectoryNames.IsCacheOrLogPath(@"C:\Users\Alice\AppData\Local\Foo\Cache").Should().BeTrue();
        CacheDirectoryNames.IsCacheOrLogPath(@"C:\Users\Alice\AppData\Local\Cache\Foo").Should().BeFalse();
    }
}
