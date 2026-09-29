using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Search;

/// <summary>
/// Free-text search over an application entity (§13.4, §43): name, publisher, product names, executable names,
/// paths, registry keys, service names and scheduled tasks. Case-insensitive substring match on every term.
/// </summary>
public static class SearchMatcher
{
    /// <summary>True when every whitespace-separated term of <paramref name="query"/> occurs somewhere in the entity.</summary>
    public static bool Matches(ApplicationEntity app, string? query)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var haystack = Fields(app).Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).ToList();

        foreach (var term in terms)
        {
            var normalizedTerm = term.Replace('/', '\\');
            if (!haystack.Any(f => f.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string?> Fields(ApplicationEntity app)
    {
        yield return app.Name;
        yield return app.Publisher;
        yield return app.Version;
        yield return app.InstallLocation;
        yield return app.MainExecutable;
        yield return app.AppxPackageFullName;
        yield return app.MsiProductCode;

        foreach (var directory in app.Directories)
        {
            yield return directory.Path;
            yield return WindowsPath.GetFileName(directory.Path);
            foreach (var exe in directory.ExecutablePaths)
            {
                yield return WindowsPath.GetFileName(exe);
            }
        }

        foreach (var exe in app.Executables)
        {
            yield return exe.ProductName;
            yield return exe.CompanyName;
            yield return exe.FileDescription;
            yield return WindowsPath.GetFileName(exe.Path);
        }

        foreach (var entry in app.RegistryEntries)
        {
            yield return entry.KeyPath;
            yield return entry.DisplayName;
            yield return entry.Publisher;
        }

        foreach (var package in app.Packages)
        {
            yield return package.Name;
            yield return package.PackageFamilyName;
            yield return package.PublisherDisplayName;
        }

        foreach (var service in app.Services)
        {
            yield return service.Name;
            yield return service.DisplayName;
        }

        foreach (var task in app.ScheduledTasks)
        {
            yield return task.TaskName;
        }

        foreach (var process in app.Processes)
        {
            yield return process.Name;
        }

        foreach (var item in app.StartupItems)
        {
            yield return item.Name;
        }
    }
}
