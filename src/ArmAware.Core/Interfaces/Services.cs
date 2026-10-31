using ArmAware.Core.Models;
namespace ArmAware.Core.Interfaces;

public interface IInstalledAppDiscovery
{
    Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken);
}
public interface IArchitectureInspector
{
    Task<ArchitectureInspectionResult> InspectAsync(string executablePath, CancellationToken cancellationToken);
}
public interface IArm64AvailabilityService
{
    Task<Arm64AvailabilityResult> CheckAsync(InstalledApp application, CancellationToken cancellationToken);
}
/// <summary>Resolved apps plus notes about the resolution as a whole (not about any single app).</summary>
public sealed record IdentityResolution(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Warnings)
{
    public static IdentityResolution Clean(IReadOnlyList<InstalledApp> apps) => new(apps, []);
}
public interface IPackageIdentityResolver
{
    Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken);
}
