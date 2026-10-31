using ArmAware.Core.Models;
namespace ArmAware.Windows.Integration;

/// <summary>Exact correlation between Windows Package Manager's installed entries and ArmAware's inventory.</summary>
public static class InstalledIdentity
{
    // MSIX apps match by package family name; registry (ARP) apps by the uninstall key name, which winget reports as the product code.
    public static bool Matches(InstalledApp app, IEnumerable<string> packageFamilies, IEnumerable<string> productCodes) =>
        (app.PackageIdentity is { Length: > 0 } family && packageFamilies.Contains(family, StringComparer.OrdinalIgnoreCase)) ||
        (app.Sources.Any(s => s.Contains("/Uninstall/", StringComparison.Ordinal)) &&
         productCodes.Contains(app.Id.Split('/').Last(), StringComparer.OrdinalIgnoreCase));
}
