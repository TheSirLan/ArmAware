using System.Runtime.InteropServices;
namespace ArmAware.Windows.Integration;

public enum WingetState { Ready, NotInstalled, Unavailable }

/// <summary>Local check that Windows Package Manager can be used for online lookups. Makes no network requests.</summary>
public static class WingetAvailability
{
    // App Installer is the package that ships winget and its API.
    private const string AppInstallerFamily = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
    public const string AppInstallerStoreUri = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";

    public static (WingetState State, string? Detail) Check()
    {
        try
        {
            if (!new global::Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty, AppInstallerFamily).Any())
                return (WingetState.NotInstalled, null);
            // Creating the manager and reading a configured source is local; connecting is what contacts the network.
            return new Microsoft.Management.Deployment.PackageManager().GetPackageCatalogByName("winget") is null
                ? (WingetState.Unavailable, "The winget source is not configured. Run winget source reset --force in an administrator terminal.")
                : (WingetState.Ready, null);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException)
        { return (WingetState.Unavailable, $"Windows Package Manager could not be started ({ex.Message}). Updating App Installer from the Microsoft Store usually fixes this."); }
    }
}
