using System.Text.Json;
using WinAppInspector.Analysis.Export;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Tests.Export;

public class ReportExporterTests
{
    private static readonly DateTimeOffset ScanTime = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly ApplicationEntity[] Apps =
    [
        new()
        {
            Id = "1", Name = "Google Chrome", Version = "128.0", Publisher = "Google LLC", AppType = AppType.ThirdPartyInstalled,
            InstallLocation = @"C:\Program Files\Google\Chrome\Application", PreferredUninstallMethod = UninstallMethod.UninstallString,
            DiskUsageBytes = 512 * 1024 * 1024, Processes = [new ProcessRecord { ProcessId = 1, Name = "chrome.exe" }],
        },
        new()
        {
            Id = "2", Name = "Foo, \"quoted\"", Publisher = "Foo\nTech", AppType = AppType.SuspectedResidue, InstallLocation = @"C:\Users\U\AppData\Local\Foo",
        },
    ];

    private static readonly ReportLabels Labels = new() { Title = "报告 <test>", Name = "软件", Yes = "是", No = "否", SizeText = b => b is null ? "?" : $"{b / 1024 / 1024} MB" };

    [Fact]
    public void Rows_carry_the_section_44_fields()
    {
        var rows = ReportExporter.ToRows(Apps, Labels);

        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("Google Chrome");
        rows[0].IsRunning.Should().BeTrue();
        rows[0].UninstallMethod.Should().Be("UninstallString");
        rows[0].DiskUsageText.Should().Be("512 MB");
        rows[1].UninstallMethod.Should().Be("None");
        rows[1].DiskUsageText.Should().Be("?");
    }

    [Fact]
    public void Csv_has_bom_header_and_escapes_quotes_commas_and_newlines()
    {
        var csv = ReportExporter.Export(Apps, ReportFormat.Csv, Labels, ScanTime);

        csv.Should().StartWith("\uFEFF软件,Version,Publisher,");
        var lines = csv.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
        lines[1].Should().Contain("Google Chrome,128.0,Google LLC,ThirdPartyInstalled,").And.Contain(",是,UninstallString,512 MB");
        lines[2].Should().StartWith("\"Foo, \"\"quoted\"\"\",,\"Foo");
    }

    [Fact]
    public void Json_is_valid_and_includes_scan_time_and_rows()
    {
        var json = ReportExporter.Export(Apps, ReportFormat.Json, Labels, ScanTime);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("title").GetString().Should().Be("报告 <test>");
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("applications").GetArrayLength().Should().Be(2);
        root.GetProperty("applications")[0].GetProperty("Name").GetString().Should().Be("Google Chrome");
        root.GetProperty("applications")[0].GetProperty("DiskUsageBytes").GetInt64().Should().Be(512L * 1024 * 1024);
        root.GetProperty("applications")[1].TryGetProperty("Version", out _).Should().BeFalse("nulls are omitted");
    }

    [Fact]
    public void Html_is_encoded_and_lists_every_row()
    {
        var html = ReportExporter.Export(Apps, ReportFormat.Html, Labels, ScanTime);

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<title>报告 &lt;test&gt;</title>");
        html.Should().Contain("<td>Google Chrome</td>");
        html.Should().Contain("<td>Foo, &quot;quoted&quot;</td>");
        html.Should().NotContain("<test>");
        html.Split("<tr>").Length.Should().Be(4, "header + two rows");
    }
}
