namespace WinAppInspector.Core.Logging;

/// <summary>Operations that must be recorded (§25).</summary>
public enum OperationKind
{
    Scan = 0,
    Uninstall = 1,
    TerminateProcess = 2,
    DisableStartupItem = 3,
    DeleteDirectory = 4,
    DeleteScheduledTask = 5,
    DeleteService = 6,
    DeleteRegistryKey = 7,
    CreateRestorePoint = 8,
}

public enum OperationResult
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    Skipped = 3,
}

/// <summary>One line of the operation log (§25: time, operation, target, result, error).</summary>
public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    OperationKind Operation,
    string Target,
    OperationResult Result,
    string? Error = null);

/// <summary>Append-only operation log, separate from diagnostic logging.</summary>
public interface IOperationLog
{
    Task AppendAsync(OperationLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationLogEntry>> ReadAsync(int maxEntries, CancellationToken cancellationToken = default);
}
