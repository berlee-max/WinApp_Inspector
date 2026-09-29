using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Core.Tests.Parsing;

public class DistinguishedNameTests
{
    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft Corporation")]
    [InlineData("CN=Foo Technologies Ltd.", "Foo Technologies Ltd.")]
    [InlineData("O=Only Org, C=DE", "Only Org")]
    [InlineData("CN=\"Contoso, Inc.\", O=Contoso", "Contoso, Inc.")]
    [InlineData("CN=Contoso\\, Inc., O=Contoso", "Contoso, Inc.")]
    [InlineData("cn=lower case", "lower case")]
    [InlineData("CN=First, CN=Second", "First")]
    [InlineData("C=US", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetCommonName_extracts_CN_or_falls_back_to_O(string? dn, string? expected)
    {
        DistinguishedName.GetCommonName(dn).Should().Be(expected);
    }

    [Fact]
    public void Parse_returns_all_attributes()
    {
        var parts = DistinguishedName.Parse("CN=A; O=B, L=Redmond , S=WA, C=US");

        parts.Should().HaveCount(5);
        parts["O"].Should().Be("B");
        parts["L"].Should().Be("Redmond");
        DistinguishedName.GetOrganization("CN=A, O=B").Should().Be("B");
    }
}
