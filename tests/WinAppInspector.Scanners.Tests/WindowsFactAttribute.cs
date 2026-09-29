namespace WinAppInspector.Scanners.Tests;

/// <summary>A fact that only runs on Windows; reported as skipped elsewhere so the cross-platform CI job stays green.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows.";
        }
    }
}
