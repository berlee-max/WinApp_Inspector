using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Analysis.Tests.Resolution;

/// <summary>Fluent fake-data builder so resolver tests read like the requirement examples (§39–41).</summary>
internal sealed class SnapshotBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly List<RegistryUninstallEntry> _registry = [];
    private readonly List<AppxPackageRecord> _packages = [];
    private readonly List<AppDirectory> _directories = [];
    private readonly List<ProcessRecord> _processes = [];
    private readonly List<ServiceRecord> _services = [];
    private readonly List<StartupItemRecord> _startup = [];
    private readonly List<ScheduledTaskRecord> _tasks = [];
    private readonly List<ShortcutRecord> _shortcuts = [];
    private readonly Dictionary<string, ExecutableMetadata> _executables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SignatureInfo> _signatures = new(StringComparer.OrdinalIgnoreCase);

    public WindowsKnownFolders Folders { get; } = WindowsKnownFolders.CreateDefault("User");

    public string Local => Folders.LocalAppData;
    public string Roaming => Folders.RoamingAppData;
    public string ProgramFiles => Folders.ProgramFiles;

    public SnapshotBuilder Registry(
        string displayName,
        string? publisher = null,
        string? installLocation = null,
        string? uninstallString = null,
        string? quietUninstallString = null,
        string? displayIcon = null,
        string? version = null,
        bool systemComponent = false,
        bool windowsInstaller = false,
        string? keyName = null,
        RegistryScope scope = RegistryScope.MachineNative,
        string? parentKeyName = null,
        string? releaseType = null)
    {
        keyName ??= displayName;
        _registry.Add(new RegistryUninstallEntry
        {
            Scope = scope,
            KeyName = keyName,
            KeyPath = Core.Parsing.RegistryUninstallEntryParser.KeyPathOf(scope, keyName),
            DisplayName = displayName,
            DisplayVersion = version,
            Publisher = publisher,
            InstallLocation = installLocation,
            UninstallString = uninstallString,
            QuietUninstallString = quietUninstallString,
            DisplayIcon = displayIcon,
            SystemComponent = systemComponent,
            WindowsInstaller = windowsInstaller,
            ParentKeyName = parentKeyName,
            ReleaseType = releaseType,
        });
        return this;
    }

    public SnapshotBuilder Package(string name, string publisherCn, string? installLocation = null, bool isFramework = false, bool? nonRemovable = null, string signatureKind = "Store", string? displayName = null)
    {
        _packages.Add(new AppxPackageRecord
        {
            Name = name,
            PackageFullName = $"{name}_1.0.0.0_x64__8wekyb3d8bbwe",
            PackageFamilyName = $"{name}_8wekyb3d8bbwe",
            DisplayName = displayName,
            Publisher = $"CN={publisherCn}",
            PublisherDisplayName = publisherCn,
            Version = "1.0.0.0",
            InstallLocation = installLocation,
            IsFramework = isFramework,
            NonRemovable = nonRemovable,
            SignatureKind = signatureKind,
        });
        return this;
    }

    public SnapshotBuilder Directory(string path, ScanRoot? root = null, DateTimeOffset? lastWrite = null, string[]? executables = null, string[]? children = null, int? fileCount = null)
    {
        _directories.Add(new AppDirectory
        {
            Path = path,
            Root = root ?? Folders.RootOf(path),
            LastWriteTime = lastWrite ?? Now.AddDays(-1),
            ExecutablePaths = executables ?? [],
            ChildDirectoryNames = children ?? [],
            TopLevelFileCount = fileCount,
        });
        return this;
    }

    public SnapshotBuilder Exe(string path, string? productName = null, string? companyName = null, string? version = null, string? signer = null, SignatureStatus signatureStatus = SignatureStatus.Valid)
    {
        _executables[path] = new ExecutableMetadata
        {
            Path = path,
            ProductName = productName,
            CompanyName = companyName,
            ProductVersion = version,
            FileVersion = version,
            FileDescription = productName,
        };
        if (signer is not null)
        {
            _signatures[path] = new SignatureInfo { Status = signatureStatus, Publisher = signer, SubjectName = "CN=" + signer };
        }
        else
        {
            _signatures[path] = SignatureInfo.NotSigned;
        }

        return this;
    }

    public SnapshotBuilder Process(string path, int pid = 1000)
    {
        _processes.Add(new ProcessRecord { ProcessId = pid, Name = WindowsPath.GetFileName(path), ExecutablePath = path });
        return this;
    }

    /// <summary>A process whose image path could not be read (access denied on protected processes).</summary>
    public SnapshotBuilder ProcessWithoutPath(string name, int pid)
    {
        _processes.Add(new ProcessRecord { ProcessId = pid, Name = name, ExecutablePath = null });
        return this;
    }

    public SnapshotBuilder Service(string name, string executablePath, string state = "Running")
    {
        _services.Add(new ServiceRecord { Name = name, DisplayName = name, ExecutablePath = executablePath, PathName = executablePath, State = state });
        return this;
    }

    public SnapshotBuilder Startup(string name, string executablePath)
    {
        _startup.Add(new StartupItemRecord
        {
            Name = name,
            Kind = StartupItemKind.RegistryRun,
            Location = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            Command = $"\"{executablePath}\"",
            ExecutablePath = executablePath,
        });
        return this;
    }

    public SnapshotBuilder Task(string name, string executablePath, string path = "\\")
    {
        _tasks.Add(new ScheduledTaskRecord { TaskName = name, TaskPath = path, Execute = executablePath, State = "Ready" });
        return this;
    }

    public SnapshotBuilder Shortcut(string name, string target)
    {
        _shortcuts.Add(new ShortcutRecord { Name = name, ShortcutPath = $@"{Folders.ProgramData}\Microsoft\Windows\Start Menu\Programs\{name}.lnk", TargetPath = target });
        return this;
    }

    public ScanSnapshot Build() => new()
    {
        ScanTime = Now,
        Folders = Folders,
        RegistryEntries = _registry,
        Packages = _packages,
        Directories = _directories,
        Processes = _processes,
        Services = _services,
        StartupItems = _startup,
        ScheduledTasks = _tasks,
        Shortcuts = _shortcuts,
        Executables = _executables,
        Signatures = _signatures,
    };
}
