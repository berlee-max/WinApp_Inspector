using WinAppInspector.Analysis.Matching;

namespace WinAppInspector.Analysis.Tests.Matching;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Google Chrome", "google chrome")]
    [InlineData("GoogleChrome", "google chrome")]
    [InlineData("chrome_x64 v128.0.6613", "chrome")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33130", "microsoft visual c redistributable")]
    [InlineData("7-Zip 23.01 (x64 edition)", "7 zip")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Normalize_strips_versions_architecture_and_noise(string? input, string expected)
    {
        NameNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Google Chrome", "Chrome", true)]
    [InlineData("Google Chrome", "GoogleChrome", true)]
    [InlineData("Spotify", "Spotify AB", true)]
    [InlineData("VLC media player", "VLC", true)]
    [InlineData("Foo Desktop", "Foo", true)]
    [InlineData("Notepad++", "Notepad++ (64-bit x64)", true)]
    [InlineData("Microsoft Edge", "Microsoft Office", false)]
    [InlineData("Foo", "Bar", false)]
    [InlineData("Cache", "Spotify", false)]
    [InlineData("", "Spotify", false)]
    public void IsSimilar_matches_variants_but_not_different_products(string a, string b, bool expected)
    {
        NameNormalizer.IsSimilar(a, b).Should().Be(expected, $"similarity was {NameNormalizer.Similarity(a, b):0.00}");
    }

    [Fact]
    public void Similarity_is_symmetric_and_bounded()
    {
        var ab = NameNormalizer.Similarity("Adobe Acrobat Reader DC", "Acrobat Reader");
        var ba = NameNormalizer.Similarity("Acrobat Reader", "Adobe Acrobat Reader DC");

        ab.Should().Be(ba);
        ab.Should().BeInRange(0, 1);
        NameNormalizer.Similarity("x", "x").Should().Be(1);
    }

    [Fact]
    public void Compact_joins_tokens()
    {
        NameNormalizer.Compact("Google Chrome 128").Should().Be("googlechrome");
        NameNormalizer.Compact("Éditeur Café").Should().Be("editeurcafe");
    }
}
