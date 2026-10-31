using System.Runtime.InteropServices;
using System.Text.Json;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using Microsoft.Management.Deployment;
namespace ArmAware.Windows.Integration;

/// <summary>An installed package as Windows Package Manager correlates it to the winget catalog.</summary>
public sealed record WingetCatalogRecord(IReadOnlyList<string> PackageFamilies, IReadOnlyList<string> ProductCodes, WingetCatalogEntry Entry);

// Uses the Windows Package Manager API (the same data as `winget list` and `winget upgrade`) instead of the GitHub
// manifest repository: one catalog query per scan, no per-package requests, and update availability included.
public sealed class WingetCatalogResolver(ScanOptions options, string? cacheDirectoryOverride = null) : IPackageIdentityResolver
{
    private const string WingetSourceId = "Microsoft.Winget.Source_8wekyb3d8bbwe";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
    private string CachePath => Path.Combine(cacheDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArmAware"), "winget-catalog-v1.json");
    private sealed record CacheFile(DateTimeOffset SavedAt, IReadOnlyList<WingetCatalogRecord> Records);

    public async Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct)
    {
        if (apps.Count == 0) return IdentityResolution.Clean(apps);
        if (!options.CheckOnlineMetadata)
            return LoadCache() is { } cached ? IdentityResolution.Clean(Match(apps, cached.Records, $"cached {cached.SavedAt.LocalDateTime:d}")) : IdentityResolution.Clean(apps);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var records = await Task.Run(() => ReadAsync(timeout.Token), timeout.Token).WaitAsync(timeout.Token);
            SaveCache(records);
            return IdentityResolution.Clean(Match(apps, records, "live"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(apps, ["Windows Package Manager's winget catalog did not respond within 60 seconds, so no apps were checked for ARM64 builds or updates."]); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        { return new(apps, [$"Windows Package Manager's winget catalog was unavailable ({ex.Message}), so no apps were checked for ARM64 builds or updates."]); }
    }

    private static async Task<IReadOnlyList<WingetCatalogRecord>> ReadAsync(CancellationToken ct)
    {
        var manager = new PackageManager();
        var source = manager.GetPackageCatalogByName("winget") ?? throw new InvalidOperationException("the winget source is not configured");
        var options = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs };
        options.Catalogs.Add(source);
        var composite = await manager.CreateCompositePackageCatalog(options).ConnectAsync().AsTask(ct);
        if (composite.Status != ConnectResultStatus.Ok) throw new InvalidOperationException($"connection returned {composite.Status}");
        var result = await composite.PackageCatalog.FindPackagesAsync(new FindPackagesOptions()).AsTask(ct);
        if (result.Status != FindPackagesResultStatus.Ok) throw new InvalidOperationException($"inventory returned {result.Status}");
        var records = new List<WingetCatalogRecord>();
        // The API's collections must be indexed; enumerating them throws.
        for (int i = 0; i < Math.Min(result.Matches.Count, 2000); i++)
        {
            ct.ThrowIfCancellationRequested();
            var package = result.Matches[i].CatalogPackage;
            var installed = package.InstalledVersion;
            var latest = package.DefaultInstallVersion;
            if (installed is null || latest is null || latest.PackageCatalog.Info.Id != WingetSourceId) continue;
            PackageInstallerInfo? sameVersion = null;
            var listed = false;
            string? listedVersion = null;
            for (int v = 0; v < package.AvailableVersions.Count; v++)
            {
                var key = package.AvailableVersions[v];
                if (!SameVersion(key.Version, installed.Version)) continue;
                listed = true;
                listedVersion = key.Version;
                sameVersion = package.GetPackageVersionInfo(key).GetApplicableInstaller(new InstallOptions());
                break;
            }
            var latestInstaller = latest.GetApplicableInstaller(new InstallOptions());
            records.Add(new(installed.PackageFamilyNames.ToArray(), installed.ProductCodes.ToArray(), new(
                package.Id, installed.Version, listed, sameVersion?.Architecture.ToString(), sameVersion?.Scope.ToString(),
                latest.Version, latestInstaller?.Architecture.ToString(), package.IsUpdateAvailable, listedVersion)));
        }
        return records;
    }

    // MSIX and ARP versions often differ only by trailing zero segments (0.102.3001.0 vs 0.102.3001).
    public static bool SameVersion(string a, string b) => PackageVersions.Match(a, b);

    public static IReadOnlyList<InstalledApp> Match(IReadOnlyList<InstalledApp> apps, IReadOnlyList<WingetCatalogRecord> records, string freshness)
        => apps.Select(app =>
        {
            var matches = records.Where(r => InstalledIdentity.Matches(app, r.PackageFamilies, r.ProductCodes)).DistinctBy(r => r.Entry.PackageId).ToArray();
            if (matches.Length != 1) return app;
            var entry = matches[0].Entry;
            if (!SameVersion(app.Version ?? "", entry.InstalledVersion))
                return app with
                {
                    WingetPackageId = entry.PackageId,
                    WingetCatalog = null,
                    Sources = app.Sources.Append($"Windows Package Manager identity: {entry.PackageId}; catalog installation version differs from current inventory, so installer/update metadata was discarded.").ToArray()
                };
            return app with
            {
                WingetPackageId = entry.PackageId,
                WingetCatalog = entry,
                Sources = app.Sources.Append($"Windows Package Manager winget catalog ({freshness}): {entry.PackageId} (exact package family or product code)").ToArray()
            };
        }).ToArray();

    private CacheFile? LoadCache()
    {
        if (!options.UseCache) return null;
        try
        {
            var path = CachePath;
            if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024) return null;
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
            return cache is { Records: not null } && cache.SavedAt <= DateTimeOffset.UtcNow &&
                DateTimeOffset.UtcNow - cache.SavedAt < CacheLifetime ? cache : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void SaveCache(IReadOnlyList<WingetCatalogRecord> records)
    {
        if (!options.UseCache) return;
        try
        {
            var path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new CacheFile(DateTimeOffset.UtcNow, records)));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache is optional. */ }
    }
}
