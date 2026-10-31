using ArmAware.Core.Classification;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArmAware.Tests;

public sealed class ScanEngineTests
{
    private sealed class Discovery : IInstalledAppDiscovery
    {
        public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstalledApp>>([
            new() { Id = "a", Name = "A", ExecutablePaths = ["missing.exe"], PrimaryExecutableKnown = true },
            new() { Id = "b", Name = "B", ExecutablePaths = ["valid.exe"], PrimaryExecutableKnown = true }]);
    }
    private sealed class Resolver(params string[] notes) : IPackageIdentityResolver
    {
        public Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct) => Task.FromResult(new IdentityResolution(apps, notes));
    }
    private sealed class Inspector : IArchitectureInspector
    {
        public Task<ArchitectureInspectionResult> InspectAsync(string path, CancellationToken ct) => Task.FromResult(
            new ArchitectureInspectionResult(path, path.StartsWith("missing") ? BinaryArchitecture.Unknown : BinaryArchitecture.Arm64, Confidence.High, "fixture", "fixture"));
    }
    private sealed class Availability : IArm64AvailabilityService
    {
        public Task<Arm64AvailabilityResult> CheckAsync(InstalledApp app, CancellationToken ct)
            => app.Id == "a" ? throw new HttpRequestException("Simulated unavailable metadata") : Task.FromResult(new Arm64AvailabilityResult(false, false, null, null, Confidence.None, "fixture"));
    }
    private sealed class ProgressCollector : IProgress<ScanProgress>
    {
        public readonly List<ScanProgress> Values = [];
        public void Report(ScanProgress value) { lock (Values) Values.Add(value); }
    }
    [Fact]
    public async Task IndividualFailureDoesNotAbortScanAndStagesAreReported()
    {
        var progress = new ProgressCollector();
        var report = await new ScanEngine(new Discovery(), new Resolver(), new Inspector(), new Availability(),
            new AppClassifier(), new ScanOptions(), NullLogger<ScanEngine>.Instance).ScanAsync(progress, default);
        Assert.Equal(2, report.Applications.Count);
        Assert.Single(report.Warnings);
        Assert.Equal(AppOptimizationStatus.Unknown, report.Applications[0].Status);
        Assert.Equal(AppOptimizationStatus.NativeArm64, report.Applications[1].Status);
        Assert.Equal(5, progress.Values.Select(p => p.Stage).Distinct().Count());
        Assert.Equal(2, progress.Values.Count(p => p.Finding is not null));
    }
    [Fact]
    public async Task ScanWideNotesAreNotAttributedToAnApp()
    {
        var report = await new ScanEngine(new Discovery(), new Resolver("Checked 16 of 40 Winget packages online"), new Inspector(), new Availability(),
            new AppClassifier(), new ScanOptions(), NullLogger<ScanEngine>.Instance).ScanAsync(null, default);
        Assert.Contains("Checked 16 of 40 Winget packages online", report.Warnings);
        Assert.DoesNotContain(report.Warnings, w => w.StartsWith("A: Checked") || w.StartsWith("B: Checked"));
        Assert.All(report.Applications, f => Assert.Empty(f.Application.Warnings));
    }
    [Fact]
    public async Task CancelledScanStopsBeforeInspection()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ScanEngine(new Discovery(), new Resolver(), new Inspector(), new Availability(),
            new AppClassifier(), new ScanOptions(), NullLogger<ScanEngine>.Instance).ScanAsync(null, source.Token));
    }
    [Fact]
    public void CsvNeutralizesFormulasAndQuotesMetadata()
    {
        var app = new InstalledApp { Id = "a", Name = "=1+1,\"test\"" };
        var report = new ScanReport(default, default, "ARM64", [new(app, [], new(false, false, null, null, Confidence.None, ""), AppOptimizationStatus.Unknown, "Unknown", Confidence.None)], [], 0);
        var csv = ReportExporter.Csv(report);
        Assert.Contains("\"'=1+1,\"\"test\"\"\"", csv);
        Assert.Contains("Unknown", ReportExporter.Json(report));
    }
}
