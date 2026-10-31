namespace ArmAware.Core.Models;

public enum BinaryArchitecture { Unknown, X86, X64, Arm64, Arm64EC, Arm64X, AnyCpu }
public enum Confidence { None, Low, Medium, High }
public enum AppOptimizationStatus { NativeArm64, Arm64UpgradeAvailable, EmulatedNoKnownAlternative, MixedArchitecture, Unknown }
public sealed record InstalledApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public string? InstallLocation { get; init; }
    public string? DisplayIcon { get; init; }
    public string? UninstallString { get; init; }
    public string? WingetPackageId { get; init; }
    public string? MicrosoftStoreProductId { get; init; }
    public WingetCatalogEntry? WingetCatalog { get; init; }
    public string? PackageIdentity { get; init; }
    public string? PackageArchitecture { get; init; }
    public bool IsSystemComponent { get; init; }
    public string? SystemComponentEvidence { get; init; }
    public string Scope { get; init; } = "Unknown";
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<string> ExecutablePaths { get; init; } = [];
    public bool PrimaryExecutableKnown { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
/// <summary>What Windows Package Manager's winget catalog reports for an installed app. Architectures are the
/// installer winget would choose on this machine (for example "Arm64" or "X64"); null when none applies.</summary>
public sealed record WingetCatalogEntry(string PackageId, string InstalledVersion, bool InstalledVersionListed,
    string? SameVersionInstallerArchitecture, string? SameVersionInstallerScope,
    string? LatestVersion, string? LatestInstallerArchitecture, bool UpdateAvailable,
    string? ListedVersion = null); // the catalog's spelling of the installed version (for example 3.0.24 for 3.0.24.0)
public sealed record ArchitectureInspectionResult(string Path, BinaryArchitecture Architecture,
    Confidence Confidence, string Method, string Evidence, string? Limitation = null);
public sealed record Arm64AvailabilityResult(bool Available, bool Suitable, string? PackageId,
    string? Version, Confidence MatchConfidence, string Evidence, string? PackageUrl = null);
public sealed record AppFinding(InstalledApp Application, IReadOnlyList<ArchitectureInspectionResult> Architectures,
    Arm64AvailabilityResult Availability, AppOptimizationStatus Status, string Recommendation, Confidence Confidence);
public sealed record ScanProgress(string Stage, int Completed = 0, int? Total = null, AppFinding? Finding = null);
public sealed record ScanReport(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string HostArchitecture,
    IReadOnlyList<AppFinding> Applications, IReadOnlyList<string> Warnings, double ElapsedSeconds);
public sealed class ScanOptions
{
    public bool IncludePerUser { get; set; } = true;
    public bool IncludePackaged { get; set; } = true;
    public bool CheckOnlineMetadata { get; set; }
    public bool UseCache { get; set; } = true;
    public int MaxConcurrency { get; set; } = 4;
}
