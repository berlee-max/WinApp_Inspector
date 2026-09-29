using WinAppInspector.Analysis.Matching;

namespace WinAppInspector.Analysis.Tests.Matching;

public class PublisherMatcherTests
{
    private readonly PublisherMatcher _matcher = new();

    [Theory]
    [InlineData("Google LLC", "google")]
    [InlineData("Google Inc.", "google")]
    [InlineData("Microsoft Corporation", "microsoft")]
    [InlineData("Foo Technologies Ltd.", "foo technologies")]
    [InlineData("腾讯科技（深圳）有限公司", "腾讯科技 深圳")]
    [InlineData("Company", "company")]
    public void Normalize_removes_legal_suffixes(string input, string expected)
    {
        _matcher.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Google LLC", "Google Inc.", true)]
    [InlineData("Foo Technologies", "Foo Technologies Ltd.", true)]
    [InlineData("Spotify AB", "Spotify", true)]
    [InlineData("Microsoft Corporation", "Microsoft Windows", true)]
    [InlineData("Mozilla Corporation", "Mozilla", true)]
    [InlineData("Google LLC", "Microsoft Corporation", false)]
    [InlineData("Adobe Inc.", "Autodesk, Inc.", false)]
    [InlineData("", "Google", false)]
    [InlineData(null, null, false)]
    public void Matches_recognises_the_same_organisation(string? a, string? b, bool expected)
    {
        _matcher.Matches(a, b).Should().Be(expected);
    }

    [Theory]
    [InlineData("Microsoft Corporation", true)]
    [InlineData("Microsoft Windows", true)]
    [InlineData("CN=Microsoft Corporation", true)]
    [InlineData("Microsoft", true)]
    [InlineData("Micro Focus", false)]
    [InlineData("Google LLC", false)]
    [InlineData(null, false)]
    public void IsMicrosoft(string? publisher, bool expected)
    {
        _matcher.IsMicrosoft(publisher).Should().Be(expected);
    }

    [Theory]
    [InlineData("NVIDIA Corporation", true)]
    [InlineData("Advanced Micro Devices, Inc.", true)]
    [InlineData("AMD", true)]
    [InlineData("Intel Corporation", true)]
    [InlineData("Realtek Semiconductor Corp.", true)]
    [InlineData("HP Inc.", true)]
    [InlineData("Hewlett-Packard Company", true)]
    [InlineData("Lenovo", true)]
    [InlineData("Dell Inc.", true)]
    [InlineData("Google LLC", false)]
    [InlineData("Spotify AB", false)]
    [InlineData("Intelligent Systems", false)]
    public void IsHardwareVendor_covers_section_9_7(string publisher, bool expected)
    {
        _matcher.IsHardwareVendor(publisher).Should().Be(expected);
    }

    [Theory]
    [InlineData("Intel(R) Chipset Device Software", true)]
    [InlineData("Realtek High Definition Audio Driver", true)]
    [InlineData("NVIDIA Graphics Driver 551.23", true)]
    [InlineData("Google Chrome", false)]
    [InlineData(null, false)]
    public void LooksLikeDriver(string? name, bool expected)
    {
        _matcher.LooksLikeDriver(name).Should().Be(expected);
    }
}
