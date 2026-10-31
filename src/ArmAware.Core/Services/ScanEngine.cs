using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ArmAware.Core.Classification;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using Microsoft.Extensions.Logging;
namespace ArmAware.Core.Services;

public sealed class ScanEngine(IInstalledAppDiscovery discovery, IPackageIdentityResolver resolver,
    IArchitectureInspector inspector, IArm64AvailabilityService availability, AppClassifier classifier,
    ScanOptions options, ILogger<ScanEngine> logger)
{
    public async Task<ScanReport> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        ct.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        progress?.Report(new("Discovering applications"));
        var apps = await discovery.DiscoverAsync(ct);
        progress?.Report(new("Resolving package identities"));
        var resolution = await resolver.ResolveAsync(apps, ct);
        apps = resolution.Apps;
        var inspected = new ConcurrentDictionary<string, IReadOnlyList<ArchitectureInspectionResult>>();
        var findings = new ConcurrentBag<AppFinding>();
        var warnings = new ConcurrentBag<string>();
        int completed = 0;
        progress?.Report(new("Inspecting architectures", 0, apps.Count));
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.MaxConcurrency, 1, 8), CancellationToken = ct };
        await Parallel.ForEachAsync(apps, parallel, async (app, token) =>
        {
            var results = new List<ArchitectureInspectionResult>();
            foreach (var path in app.ExecutablePaths.Take(12)) results.Add(await inspector.InspectAsync(path, token));
            inspected[app.Id] = results;
            progress?.Report(new("Inspecting architectures", Interlocked.Increment(ref completed), apps.Count));
        });
        completed = 0;
        progress?.Report(new("Checking ARM64 availability", 0, apps.Count));
        await Parallel.ForEachAsync(apps, parallel, async (app, token) =>
        {
            Arm64AvailabilityResult native;
            try { native = await availability.CheckAsync(app, token); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or InvalidDataException)
            {
                warnings.Add($"{app.Name}: {ex.Message}");
                logger.LogWarning(ex, "Metadata lookup failed for {ApplicationId}", app.Id);
                native = new(false, false, app.WingetPackageId, null, Confidence.None, "Package metadata could not be retrieved.");
            }
            var finding = classifier.Classify(app, inspected[app.Id], native, RuntimeInformation.OSArchitecture == Architecture.Arm64);
            findings.Add(finding);
            progress?.Report(new("Checking ARM64 availability", Interlocked.Increment(ref completed), apps.Count, finding));
        });
        progress?.Report(new("Preparing recommendations"));
        timer.Stop();
        logger.LogInformation("Scanned {Count} applications in {Seconds} seconds", apps.Count, timer.Elapsed.TotalSeconds);
        return new(started, DateTimeOffset.UtcNow, RuntimeInformation.OSArchitecture.ToString(),
            findings.OrderBy(x => x.Application.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            resolution.Warnings.Concat(warnings).Concat(apps.SelectMany(a => a.Warnings.Select(w => $"{a.Name}: {w}"))).ToArray(), timer.Elapsed.TotalSeconds);
    }
}
