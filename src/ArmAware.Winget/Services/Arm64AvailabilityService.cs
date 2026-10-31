using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
namespace ArmAware.Winget.Services;

// Decides ARM64 availability from the winget catalog entry attached during identity resolution. No network access.
public sealed class Arm64AvailabilityService : IArm64AvailabilityService
{
    public Task<Arm64AvailabilityResult> CheckAsync(InstalledApp application, CancellationToken cancellationToken)
        => Task.FromResult(Evaluate(application));

    public static Arm64AvailabilityResult Evaluate(InstalledApp app)
    {
        if (app.WingetCatalog is not { } c)
            return app.MicrosoftStoreProductId is { } storeId
                ? new(false, false, storeId, null, Confidence.High,
                    $"Microsoft Store identity {storeId} was verified. Store identity and package architecture do not establish a same-version ARM64 distribution; availability remains unknown.")
                : new(false, false, null, null, Confidence.None, "Windows Package Manager has no winget catalog entry for this app, or online checks are off. Availability is unknown.");
        if (!PackageVersions.Match(app.Version, c.InstalledVersion) ||
            (c.InstalledVersionListed && !PackageVersions.Match(app.Version, c.ListedVersion ?? c.InstalledVersion)))
            return new(false, false, c.PackageId, null, Confidence.None,
                "The catalog record does not match the currently installed version. Rescan online before considering a replacement.");
        // Commands and manifest links must use the catalog's version string, which can differ by trailing ".0" segments.
        var listedVersion = c.ListedVersion ?? c.InstalledVersion;
        var sameIsArm = IsArm64(c.SameVersionInstallerArchitecture);
        var latestIsArm = IsArm64(c.LatestInstallerArchitecture);
        if (c.InstalledVersionListed && sameIsArm)
        {
            var suitable = ScopeCompatible(c.SameVersionInstallerScope, app.Scope);
            return new(true, suitable, c.PackageId, listedVersion, Confidence.High,
                $"Windows Package Manager would install the ARM64 installer of {c.PackageId} {c.InstalledVersion} (your installed version) on this PC." +
                (suitable ? "" : $" Its scope ({c.SameVersionInstallerScope}) differs from this installation ({app.Scope}); review before switching.") +
                " Installer targeting does not prove every payload binary is native.", ManifestUrl(c.PackageId, listedVersion));
        }
        var newer = c.UpdateAvailable && latestIsArm
            ? $" A newer version, {c.LatestVersion}, has an ARM64 installer; updating may switch to it." : "";
        if (c.InstalledVersionListed)
            return new(newer.Length > 0, false, c.PackageId, newer.Length > 0 ? c.LatestVersion : listedVersion, Confidence.High,
                (c.SameVersionInstallerArchitecture is { } arch
                    ? $"For your installed version ({c.InstalledVersion}), Windows Package Manager's best installer on this PC is {arch}, so no ARM64 build is listed."
                    : $"No installer for your installed version ({c.InstalledVersion}) applies to this PC.") + newer,
                ManifestUrl(c.PackageId, listedVersion));
        return new(latestIsArm, false, c.PackageId, c.LatestVersion, Confidence.Medium,
            $"Your installed version ({c.InstalledVersion}) is not listed in the winget catalog, so it cannot be compared exactly. " +
            (latestIsArm ? $"The catalog's version {c.LatestVersion} has an ARM64 installer." : $"The catalog's version {c.LatestVersion} has no ARM64 installer ({c.LatestInstallerArchitecture ?? "none applicable"})."));
    }

    private static bool IsArm64(string? architecture) => string.Equals(architecture, "Arm64", StringComparison.OrdinalIgnoreCase);
    // Windows Package Manager reports System/User/Any; inventory reports Machine/User.
    private static bool ScopeCompatible(string? installerScope, string appScope) => installerScope switch
    {
        null or "Any" or "Unknown" => true,
        "System" => appScope.Equals("Machine", StringComparison.OrdinalIgnoreCase),
        _ => installerScope.Equals(appScope, StringComparison.OrdinalIgnoreCase),
    };
    // The fixed official repository page for this exact package version; opened only when the user asks.
    private static string? ManifestUrl(string id, string version) =>
        id.Contains('.') && id.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' or '+') && version.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' or '+')
            ? $"https://github.com/microsoft/winget-pkgs/tree/master/manifests/{char.ToLowerInvariant(id[0])}/{id.Replace('.', '/')}/{version}" : null;
}
