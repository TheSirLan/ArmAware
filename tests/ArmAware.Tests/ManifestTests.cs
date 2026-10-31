using System.Net;
using ArmAware.Core.Models;
using ArmAware.Winget.Services;
namespace ArmAware.Tests;

public sealed class ManifestTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string content = request.RequestUri!.Host == "api.github.com"
                ? """[{"name":"Vendor.Editor.installer.yaml"},{"name":"Vendor.Editor.locale.en-US.yaml"}]"""
                : request.RequestUri.AbsolutePath.EndsWith("installer.yaml")
                    ? """
                      PackageIdentifier: Vendor.Editor
                      PackageVersion: '1.0'
                      Scope: user
                      MinimumOSVersion: 10.0.22000.0
                      InstallerType: exe
                      Installers:
                        - Architecture: x64
                        - Architecture: arm64
                          Scope: machine
                        - Architecture: arm64
                      """
                    : """
                      PackageIdentifier: Vendor.Editor
                      PackageVersion: '1.0'
                      PackageName: Editor
                      Publisher: Vendor Inc
                      """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
    [Fact]
    public async Task StructuredMultiInstallerManifestUsesInheritedAndOverriddenFields()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler);
        var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = false });
        var metadata = await repository.GetAsync("Vendor.Editor", "1.0", default);
        Assert.NotNull(metadata); Assert.Equal(3, metadata.Installers.Count); Assert.Equal("Editor", metadata.Name);
        Assert.Equal("machine", metadata.Installers[1].Scope); Assert.Equal("user", metadata.Installers[2].Scope);
        Assert.Equal("10.0.22000.0", metadata.Installers[2].MinimumOs); Assert.Equal(3, handler.Requests);
    }
    [Fact]
    public async Task OfflineAndInvalidSegmentsNeverSendRequests()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler);
        var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = false, UseCache = false });
        Assert.Null(await repository.GetAsync("Vendor.Editor", "1.0", default));
        var online = new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = false });
        Assert.Null(await online.GetAsync("../../escape", "1.0", default));
        Assert.Null(await online.GetAsync("Vendor.Editor", "1.0/../../escape", default));
        Assert.Equal(0, handler.Requests);
    }
    [Fact]
    public async Task ConflictingManifestVersionIsRejected()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler);
        var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = false });
        Assert.Null(await repository.GetAsync("Vendor.Editor", "2.0", default));
    }
    [Fact]
    public async Task InvalidCacheRecordIsAMissAndDoesNotBreakOfflineScan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "armaware-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Vendor.Editor\n1.0")));
        var path = Path.Combine(directory, hash + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{}");
            using var handler = new Handler(); using var client = new HttpClient(handler);
            var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = false, UseCache = true }, directory);
            Assert.Null(await repository.GetAsync("Vendor.Editor", "1.0", default));
            Assert.Empty(await repository.CachedAsync(default));
            Assert.Equal(0, handler.Requests);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
    private sealed class NotFoundHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
    [Fact]
    public async Task NotFoundIsCachedSoLaterScansMakeNoRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "armaware-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new NotFoundHandler(); using var client = new HttpClient(handler);
            var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = true }, directory);
            Assert.Null(await repository.GetAsync("Vendor.Editor", "1.0", default));
            Assert.True(repository.IsCached("Vendor.Editor", "1.0"));
            Assert.Null(await repository.GetAsync("Vendor.Editor", "1.0", default));
            Assert.Equal(1, handler.Requests);
            Assert.Empty(await repository.CachedAsync(default));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task OnlyFreshCacheEntriesCountAsCached()
    {
        var directory = Path.Combine(Path.GetTempPath(), "armaware-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Vendor.Editor\n1.0")));
        var path = Path.Combine(directory, hash + ".json");
        try
        {
            using var client = new HttpClient();
            var repository = new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = true }, directory);
            Assert.False(repository.IsCached("Vendor.Editor", "1.0"));
            await File.WriteAllTextAsync(path, "{}");
            Assert.True(repository.IsCached("Vendor.Editor", "1.0"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
            Assert.False(repository.IsCached("Vendor.Editor", "1.0"));
            Assert.False(new ManifestRepository(client, new() { CheckOnlineMetadata = true, UseCache = false }, directory).IsCached("Vendor.Editor", "1.0"));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
