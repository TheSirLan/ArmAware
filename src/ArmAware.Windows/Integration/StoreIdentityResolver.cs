using System.Runtime.InteropServices;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Deployment;
namespace ArmAware.Windows.Integration;

public sealed record StorePackageIdentity(string ProductId, IReadOnlyList<string> PackageFamilies, IReadOnlyList<string> ProductCodes);

public sealed class StoreIdentityResolver(ScanOptions options, ILogger<StoreIdentityResolver>? logger = null) : IPackageIdentityResolver
{
    // COM activation errors: the API is unreachable from this process. Logged, not reported.
    private static readonly int[] ActivationUnavailable = [unchecked((int)0x80040154), unchecked((int)0x80040155), unchecked((int)0x80073D54)];
    public async Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct)
    {
        if (!options.CheckOnlineMetadata || apps.Count == 0) return IdentityResolution.Clean(apps);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var packages = await Task.Run(() => ReadAsync(timeout.Token), timeout.Token).WaitAsync(timeout.Token);
            return IdentityResolution.Clean(Match(apps, packages));
        }
        catch (COMException ex) when (ActivationUnavailable.Contains(ex.HResult))
        {
            logger?.LogInformation(ex, "Windows Package Manager API is not available to this process; Store identity lookup skipped");
            return IdentityResolution.Clean(apps);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("Microsoft Store identity lookup timed out after 20 seconds."); }
    }

    private static async Task<IReadOnlyList<StorePackageIdentity>> ReadAsync(CancellationToken ct)
    {
        // The official activation shim is deployed beside both application entry points.
        var manager = new Microsoft.Management.Deployment.PackageManager();
        var source = manager.GetPackageCatalogByName("msstore") ?? throw new InvalidOperationException("msstore is not configured.");
        // Never accept source agreements or prompt for authentication on the user's behalf.
        var connected = await source.ConnectAsync().AsTask(ct);
        if (connected.Status != ConnectResultStatus.Ok)
            throw new InvalidOperationException($"Store catalog connection returned {connected.Status}. If agreements are required, review them manually with winget list --source msstore.");
        var compositeOptions = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs };
        compositeOptions.Catalogs.Add(source);
        var composite = await manager.CreateCompositePackageCatalog(compositeOptions).ConnectAsync().AsTask(ct);
        if (composite.Status != ConnectResultStatus.Ok) throw new InvalidOperationException($"Store installed catalog returned {composite.Status}.");
        var result = await composite.PackageCatalog.FindPackagesAsync(new FindPackagesOptions()).AsTask(ct);
        if (result.Status != FindPackagesResultStatus.Ok) throw new InvalidOperationException($"Store inventory returned {result.Status}.");
        if (result.WasLimitExceeded) throw new InvalidOperationException("Store inventory was truncated; identity results were not used.");
        var identities = new List<StorePackageIdentity>();
        for (int i = 0; i < Math.Min(result.Matches.Count, 2000); i++)
        {
            ct.ThrowIfCancellationRequested();
            var package = result.Matches[i].CatalogPackage;
            var remote = package.DefaultInstallVersion;
            if (package.InstalledVersion is null || remote is null || remote.PackageCatalog.Info.Id != "StoreEdgeFD") continue;
            // Only stable identifiers from the correlated Store catalog; never display-name guesses.
            identities.Add(new(remote.Id, remote.PackageFamilyNames.ToArray(), remote.ProductCodes.ToArray()));
        }
        return identities;
    }


    public static IReadOnlyList<InstalledApp> Match(IReadOnlyList<InstalledApp> apps, IReadOnlyList<StorePackageIdentity> packages)
        => apps.Select(app =>
        {
            var matches = packages.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.ProductId, "^[A-Z0-9]{12,14}$") &&
                InstalledIdentity.Matches(app, p.PackageFamilies, p.ProductCodes))
                .DistinctBy(p => p.ProductId).ToArray();
            return matches.Length == 1 ? app with
            {
                MicrosoftStoreProductId = matches[0].ProductId,
                Sources = app.Sources.Append("Microsoft Store/msstore: " + matches[0].ProductId + " (exact package family or product code)").ToArray()
            } : app;
        }).ToArray();
}
