using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Logging;

namespace WinAppInspector.Actions.Logging;

/// <summary>Append-only JSON-lines operation log (§25). One file, one line per operation; never throws to the caller.</summary>
public sealed class FileOperationLog : IOperationLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _path;
    private readonly ILogger<FileOperationLog> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileOperationLog(string directory, ILogger<FileOperationLog> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, "operations.jsonl");
        _logger = logger;
    }

    public string FilePath => _path;

    public void Dispose() => _gate.Dispose();

    public async Task AppendAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Operation log entry could not be written to {Path}", _path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationLogEntry>> ReadAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
            var entries = new List<OperationLogEntry>();
            foreach (var line in lines.Reverse())
            {
                if (entries.Count >= maxEntries)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<OperationLogEntry>(line, JsonOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Skipping a malformed operation log line");
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Operation log at {Path} could not be read", _path);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }
}
