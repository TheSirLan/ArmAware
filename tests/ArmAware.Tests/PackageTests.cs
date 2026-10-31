using ArmAware.Core.Models;
using ArmAware.Winget.Services;
using ArmAware.Windows.Discovery;
using ArmAware.Windows.Integration;
namespace ArmAware.Tests;

public sealed class PackageTests
{
    private readonly InstalledApp app = new()
    {
        Id = "LocalMachine/Registry64/Uninstall/Editor_is1",
        Name = "Editor",
        Publisher = "Vendor, Inc.",
        Version = "1.0",
        Scope = "User",
        Sources = ["LocalMachine/Registry64/Uninstall/Editor_is1"]
    };
    private static WingetCatalogEntry Entry(string? same = "Arm64", string? scope = "User", bool listed = true, string? latest = "Arm64", bool update = false, string latestVersion = "1.0")
        => new("Vendor.Editor", "1.0", listed, listed ? same : null, listed ? scope : null, latestVersion, latest, update);
    [Fact]
    public void SameVersionArm64InstallerIsSuitable()
    {
        var result = Arm64AvailabilityService.Evaluate(app with { WingetCatalog = Entry() });
        Assert.True(result.Available); Assert.True(result.Suitable); Assert.Equal(Confidence.High, result.MatchConfidence);
        Assert.Equal("1.0", result.Version);
        Assert.StartsWith("https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/Vendor/Editor/1.0", result.PackageUrl);
    }
    [Fact]
    public void CommandsUseTheCatalogVersionSpelling()
    {
        var result = Arm64AvailabilityService.Evaluate(app with { Version = "1.0.0", WingetCatalog = Entry() with { InstalledVersion = "1.0.0", ListedVersion = "1.0" } });
        Assert.Equal("1.0", result.Version);
        Assert.EndsWith("/Vendor/Editor/1.0", result.PackageUrl);
    }
    [Fact] public void ScopeMismatchIsNotSuitable() => Assert.False(Arm64AvailabilityService.Evaluate(app with { WingetCatalog = Entry(scope: "System") }).Suitable);
    [Fact] public void SystemScopeMatchesMachineInstall() => Assert.True(Arm64AvailabilityService.Evaluate(app with { Scope = "Machine", WingetCatalog = Entry(scope: "System") }).Suitable);
    [Fact] public void X64OnlyIsNotAvailable() => Assert.False(Arm64AvailabilityService.Evaluate(app with { WingetCatalog = Entry(same: "X64", latest: "X64") }).Available);
    [Fact]
    public void NewerArm64VersionIsReportedButNotSuitable()
    {
        var result = Arm64AvailabilityService.Evaluate(app with { WingetCatalog = Entry(same: "X64", latest: "Arm64", update: true, latestVersion: "2.0") });
        Assert.True(result.Available); Assert.False(result.Suitable); Assert.Equal("2.0", result.Version);
        Assert.Contains("2.0", result.Evidence);
    }
    [Fact] public void UnlistedInstalledVersionIsNeverSuitable() => Assert.False(Arm64AvailabilityService.Evaluate(app with { WingetCatalog = Entry(listed: false) }).Suitable);
    [Fact] public void NoCatalogEntryIsUnknown() => Assert.False(Arm64AvailabilityService.Evaluate(app).Available);
    [Theory]
    [InlineData("0.102.3001.0", "0.102.3001", true)]
    [InlineData("1.2.0.0", "1.2", true)]
    [InlineData("1.2.0", "1.2.1", false)]
    [InlineData("10.0", "1.0", false)]
    public void VersionsDifferingOnlyByTrailingZerosMatch(string a, string b, bool same) => Assert.Equal(same, WingetCatalogResolver.SameVersion(a, b));
    [Fact]
    public void CatalogMatchesOnlyExactProductCodeOrFamily()
    {
        var record = new WingetCatalogRecord([], ["Editor_is1"], Entry());
        var matched = WingetCatalogResolver.Match([app, app with { Id = "LocalMachine/Registry64/Uninstall/Other", Sources = ["LocalMachine/Registry64/Uninstall/Other"] }], [record], "live");
        Assert.Equal("Vendor.Editor", matched[0].WingetPackageId);
        Assert.Null(matched[1].WingetPackageId);
        var msix = new InstalledApp { Id = "msix:Editor", Name = "Editor", Version = "1.0", PackageIdentity = "Vendor.Editor_abc" };
        Assert.Equal("Vendor.Editor", WingetCatalogResolver.Match([msix], [new(["Vendor.Editor_abc"], [], Entry())], "live")[0].WingetCatalog?.PackageId);
    }
    [Fact]
    public void AmbiguousCatalogMatchesAreRejected()
    {
        var records = new WingetCatalogRecord[] { new([], ["Editor_is1"], Entry()), new([], ["Editor_is1"], Entry() with { PackageId = "Other.Editor" }) };
        Assert.Null(WingetCatalogResolver.Match([app], records, "live")[0].WingetCatalog);
    }
    [Fact]
    public void ChangedInstalledVersionKeepsIdentityButDiscardsCatalogActions()
    {
        var changed = app with { Version = "2.0" };
        var matched = WingetCatalogResolver.Match([changed], [new([], ["Editor_is1"], Entry(update: true, latestVersion: "3.0"))], "cached")[0];
        Assert.Equal("Vendor.Editor", matched.WingetPackageId);
        Assert.Null(matched.WingetCatalog);
        Assert.False(Arm64AvailabilityService.Evaluate(matched).Suitable);
    }
    [Theory]
    [InlineData("2.0", "1.0", null)]
    [InlineData("1.0", "1.0", "0.9")]
    [InlineData(null, "1.0", null)]
    public void AvailabilityIndependentlyRejectsMismatchedInventory(string? installed, string cachedVersion, string? listed)
    {
        var result = Arm64AvailabilityService.Evaluate(app with
        {
            Version = installed,
            WingetCatalog = Entry() with { InstalledVersion = cachedVersion, ListedVersion = listed }
        });
        Assert.False(result.Suitable);
        Assert.False(result.Available);
    }
    [Fact]
    public async Task OfflineScanUsesFreshCatalogCacheOnly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "armaware-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var offline = new WingetCatalogResolver(new() { CheckOnlineMetadata = false, UseCache = true }, directory);
            Assert.Null((await offline.ResolveAsync([app], default)).Apps[0].WingetCatalog);
            var cache = new { SavedAt = DateTimeOffset.UtcNow, Records = new[] { new WingetCatalogRecord([], ["Editor_is1"], Entry()) } };
            await File.WriteAllTextAsync(Path.Combine(directory, "winget-catalog-v1.json"), System.Text.Json.JsonSerializer.Serialize(cache));
            var resolved = (await offline.ResolveAsync([app], default)).Apps[0];
            Assert.Equal("Vendor.Editor", resolved.WingetPackageId);
            Assert.Contains(resolved.Sources, s => s.Contains("cached"));
            var stale = cache with { SavedAt = DateTimeOffset.UtcNow.AddDays(-8) };
            await File.WriteAllTextAsync(Path.Combine(directory, "winget-catalog-v1.json"), System.Text.Json.JsonSerializer.Serialize(stale));
            Assert.Null((await offline.ResolveAsync([app], default)).Apps[0].WingetCatalog);
            Assert.Null((await new WingetCatalogResolver(new() { CheckOnlineMetadata = false, UseCache = false }, directory).ResolveAsync([app], default)).Apps[0].WingetCatalog);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("Beeper", "Beeper 4.3.264", true)]
    [InlineData("Visual Studio Code", "Microsoft Visual Studio Code (User)", true)]
    [InlineData("Visual Studio Code - Insiders", "Microsoft Visual Studio Code Insiders (User)", true)]
    [InlineData("T3 Code (Nightly)", "T3 Code (Nightly) 0.1.6-nightly.20261030.3917", true)]
    [InlineData("coreutils", "Coreutils for Windows 2026.9.3", true)]
    [InlineData("VLC media player", "VLC media player", true)]
    [InlineData("Microsoft 365 and Office", "Microsoft 365 - en-gb", false)]
    [InlineData("App", "My App Helper", false)]
    [InlineData("Editor", "Other Editor Pro", false)]
    [InlineData(null, "Editor", false)]
    public void DisplayNamesIgnoreVersionsAndLabels(string? product, string display, bool expected)
        => Assert.Equal(expected, WindowsAppDiscovery.NamesMatch(product, display));
    [Fact]
    public void DisplayIconParsingNeverExecutesAnything()
    {
        Assert.Equal(@"C:\Program Files\App\app.exe", ExecutableLocator.FromDisplayIcon("\"C:\\Program Files\\App\\app.exe\",0"));
        Assert.Null(ExecutableLocator.FromDisplayIcon("cmd /c anything"));
        Assert.Null(ExecutableLocator.FromDisplayIcon(@"C:\icon.dll,0"));
    }
    [Fact]
    public void DisplayNameDoesNotDeduplicate()
    {
        var apps = WindowsAppDiscovery.Deduplicate([app with { Id = "HKLM/64/Uninstall/First" }, app with { Id = "HKLM/64/Uninstall/Second" }]);
        Assert.Equal(2, apps.Count);
    }
}
