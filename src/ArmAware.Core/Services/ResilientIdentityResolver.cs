using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using Microsoft.Extensions.Logging;
namespace ArmAware.Core.Services;

// Each source commits its enrichment only after success. One unavailable source cannot erase another's results.
public sealed class ResilientIdentityResolver(
    IReadOnlyList<(string Name, IPackageIdentityResolver Resolver)> sources,
    ILogger<ResilientIdentityResolver> logger) : IPackageIdentityResolver
{
    public async Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct)
    {
        var warnings = new List<string>();
        foreach (var (name, resolver) in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await resolver.ResolveAsync(apps, ct);
                apps = result.Apps;
                warnings.AddRange(result.Warnings);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Identity source {Source} unavailable", name);
                warnings.Add($"{name} lookup was unavailable ({ex.Message}). Other sources and executable inspection continued.");
            }
        }
        return new(apps, warnings);
    }
}
