using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Models;

namespace WinAppInspector.UI.Services;

/// <summary>What the last scan produced, so the overview is populated immediately on the next launch (§31, §33).</summary>
public sealed record CachedScan(DateTimeOffset ScanTime, IReadOnlyList<ApplicationEntity> Applications);

/// <summary>JSON cache of resolved applications. Any read or write failure is logged and treated as "no cache".</summary>
public sealed class ScanCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<ScanCache> _logger;

    public ScanCache(string dataDirectory, ILogger<ScanCache> logger)
    {
        _path = Path.Combine(dataDirectory, "last-scan.json");
        _logger = logger;
    }

    public async Task<CachedScan?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var cached = await JsonSerializer.DeserializeAsync<CachedScan>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Loaded {Count} cached applications from {Path}", cached?.Applications.Count ?? 0, _path);
            return cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Scan cache at {Path} could not be read; it will be rebuilt", _path);
            return null;
        }
    }

    public async Task SaveAsync(CachedScan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, scan, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Scan cache could not be written to {Path}", _path);
        }
    }
}
