using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using ArmAware.Windows.Integration;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArmAware.Tests;

public sealed class StoreSourceTests
{
    private static InstalledApp App => new() { Id = "msix:Editor", Name = "Editor", PackageIdentity = "Vendor.Editor_abc", Version = "1.0" };
    private sealed class Source(Func<IReadOnlyList<InstalledApp>, CancellationToken, Task<IReadOnlyList<InstalledApp>>> resolve) : IPackageIdentityResolver
    {
        public async Task<IdentityResolution> ResolveAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct) => IdentityResolution.Clean(await resolve(apps, ct));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EitherSourceCanFailWithoutLosingTheOtherSourcesIdentity(bool storeFails)
    {
        var failing = new Source((_, _) => throw new InvalidOperationException("SourceAgreementsNotAccepted"));
        var community = new Source((apps, _) => Task.FromResult<IReadOnlyList<InstalledApp>>(apps.Select(a => a with { WingetPackageId = "Vendor.Editor" }).ToArray()));
        var store = new Source((apps, _) => Task.FromResult<IReadOnlyList<InstalledApp>>(StoreIdentityResolver.Match(apps, [new("9N0DX20HK701", ["Vendor.Editor_abc"], [])])));
        var resolver = new ResilientIdentityResolver(
            [("Community", storeFails ? community : failing), ("Microsoft Store", storeFails ? failing : store)],
            NullLogger<ResilientIdentityResolver>.Instance);
        var resolution = await resolver.ResolveAsync([App], default);
        var result = Assert.Single(resolution.Apps);
        Assert.Single(resolution.Warnings);
        Assert.Empty(result.Warnings);
        if (storeFails) Assert.Equal("Vendor.Editor", result.WingetPackageId);
        else Assert.Equal("9N0DX20HK701", result.MicrosoftStoreProductId);
    }
    [Fact]
    public async Task StoreTimeoutDoesNotAbortInventory()
    {
        var resolver = new ResilientIdentityResolver([("Microsoft Store", new Source((_, _) => throw new TimeoutException("Timed out")))], NullLogger<ResilientIdentityResolver>.Instance);
        var resolution = await resolver.ResolveAsync([App], default);
        Assert.Single(resolution.Apps);
        Assert.Contains("Timed out", Assert.Single(resolution.Warnings));
    }
    [Fact]
    public async Task UserCancellationIsNotConvertedToSourceWarning()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new ResilientIdentityResolver([("Store", new Source((_, ct) => { cancellation.Cancel(); throw new OperationCanceledException(ct); }))], NullLogger<ResilientIdentityResolver>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync([App], cancellation.Token));
    }
    [Fact]
    public void StoreIdentityRequiresAnUnambiguousStableIdentifier()
    {
        var matching = new StorePackageIdentity("9N0DX20HK701", ["Vendor.Editor_abc"], []);
        Assert.Equal("9N0DX20HK701", StoreIdentityResolver.Match([App], [matching])[0].MicrosoftStoreProductId);
        Assert.Null(StoreIdentityResolver.Match([App with { PackageIdentity = "Other_abc" }], [matching])[0].MicrosoftStoreProductId);
        Assert.Null(StoreIdentityResolver.Match([App], [matching, matching with { ProductId = "9NBLGGH4NNS1" }])[0].MicrosoftStoreProductId);
    }
    [Fact]
    public void ExactArpProductCodeMatchesStoreDesktopApps()
    {
        var app = App with { Id = "LocalMachine/Registry64/Uninstall/{abc}", PackageIdentity = null, Sources = ["LocalMachine/Registry64/Uninstall/{abc}"] };
        Assert.Equal("9N0DX20HK701", StoreIdentityResolver.Match([app], [new("9N0DX20HK701", [], ["{abc}"])])[0].MicrosoftStoreProductId);
    }
    [Fact]
    public async Task OfflineModeDoesNotActivateStoreApi()
    {
        var result = await new StoreIdentityResolver(new() { CheckOnlineMetadata = false }).ResolveAsync([App], default);
        Assert.Equal(App, Assert.Single(result.Apps));
        Assert.Empty(result.Warnings);
    }
    [Fact]
    public async Task VerifiedStoreIdentityDoesNotInventArm64Availability()
    {
        var result = await new ArmAware.Winget.Services.Arm64AvailabilityService().CheckAsync(App with { MicrosoftStoreProductId = "9N0DX20HK701" }, default);
        Assert.False(result.Available);
        Assert.False(result.Suitable);
        Assert.Contains("availability remains unknown", result.Evidence);
    }
    [Fact]
    public void StoreProductIdIsExportedSeparatelyFromCommunityId()
    {
        var app = App with { MicrosoftStoreProductId = "9N0DX20HK701" };
        var finding = new AppFinding(app, [], new(false, false, app.MicrosoftStoreProductId, null, Confidence.High, "Unknown availability"), AppOptimizationStatus.Unknown, "Review evidence", Confidence.None);
        var report = new ScanReport(default, default, "Arm64", [finding], [], 0);
        Assert.Contains("9N0DX20HK701", ReportExporter.Json(report));
        Assert.Contains("MicrosoftStoreProductId", ReportExporter.Csv(report));
        Assert.Contains("9N0DX20HK701", ReportExporter.Csv(report));
    }
}
