using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArmAware.Core.Models;
using YamlDotNet.RepresentationModel;
namespace ArmAware.Winget.Services;

public sealed record InstallerMetadata(string Architecture, string? Scope, string? MinimumOs, string? Type);
public sealed record PackageMetadata(string Id, string Version, string? Name, string? Publisher,
    IReadOnlyList<InstallerMetadata> Installers, string Url);

public sealed partial class ManifestRepository(HttpClient client, ScanOptions options, string? cacheDirectoryOverride = null)
{
    private readonly string cacheDirectory = cacheDirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArmAware", "metadata-v1");
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,150}$")]
    private static partial Regex SafeSegment();

    public async Task<IReadOnlyList<PackageMetadata>> CachedAsync(CancellationToken ct)
    {
        var results = new List<PackageMetadata>();
        if (!options.UseCache || !Directory.Exists(cacheDirectory)) return results;
        try
        {
            foreach (var path in Directory.EnumerateFiles(cacheDirectory, "*.json").Take(512))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= TimeSpan.FromDays(7) || new FileInfo(path).Length > 2 * 1024 * 1024) continue;
                try { if (JsonSerializer.Deserialize<PackageMetadata>(await File.ReadAllTextAsync(path, ct)) is { } item && Valid(item)) results.Add(item); }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return results;
    }

    private string CachePath(string id, string version)
        => Path.Combine(cacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n" + version))) + ".json");
    // "Not in the repository at this version" is cached too, so missing packages don't cost requests on every scan.
    private static string MissingPath(string cachePath) => Path.ChangeExtension(cachePath, ".missing");
    private static bool Fresh(string path, long maxBytes) =>
        File.Exists(path) && new FileInfo(path).Length <= maxBytes && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7);
    /// <summary>True when a fresh cache entry (found or not found) exists, so <see cref="GetAsync"/> makes no network requests for it.</summary>
    public bool IsCached(string id, string version)
    {
        if (!options.UseCache || !SafeSegment().IsMatch(id) || !SafeSegment().IsMatch(version)) return false;
        var cachePath = CachePath(id, version);
        return Fresh(cachePath, 2 * 1024 * 1024) || Fresh(MissingPath(cachePath), 0);
    }
    private void CacheMissing(string cachePath)
    {
        if (!options.UseCache) return;
        try { Directory.CreateDirectory(cacheDirectory); File.WriteAllBytes(MissingPath(cachePath), []); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache is optional. */ }
    }
    public async Task<PackageMetadata?> GetAsync(string id, string version, CancellationToken ct)
    {
        if (!SafeSegment().IsMatch(id) || !SafeSegment().IsMatch(version) || !id.Contains('.')) return null;
        var cachePath = CachePath(id, version);
        if (options.UseCache && Fresh(MissingPath(cachePath), 0)) return null;
        if (IsCached(id, version))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<PackageMetadata>(await File.ReadAllTextAsync(cachePath, ct));
                if (cached is not null && Valid(cached) && cached.Id == id && cached.Version == version) return cached;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* A damaged cache is a miss. */ }
        }
        if (!options.CheckOnlineMetadata) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var relative = "manifests/" + char.ToLowerInvariant(id[0]) + "/" + string.Join('/', id.Split('.').Select(Uri.EscapeDataString)) + "/" + Uri.EscapeDataString(version);
            var url = "https://api.github.com/repos/microsoft/winget-pkgs/contents/" + relative;
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { CacheMissing(cachePath); return null; }
            response.EnsureSuccessStatusCode();
            using var listing = JsonDocument.Parse(await ReadBounded(response, timeout.Token));
            var files = listing.RootElement.EnumerateArray().Take(64).Select(x => x.GetProperty("name").GetString()!).ToArray();
            var installerFile = files.FirstOrDefault(x => x.Equals(id + ".installer.yaml", StringComparison.OrdinalIgnoreCase));
            // Singleton manifests use <id>.yaml.
            installerFile ??= files.FirstOrDefault(x => x.Equals(id + ".yaml", StringComparison.OrdinalIgnoreCase));
            if (installerFile is null) { CacheMissing(cachePath); return null; }
            var root = await ReadYaml(relative, installerFile, timeout.Token);
            if (Value(root, "PackageIdentifier") != id || Value(root, "PackageVersion") != version) { CacheMissing(cachePath); return null; }
            var localeFile = files.FirstOrDefault(x => x.Equals(id + ".locale.en-US.yaml", StringComparison.OrdinalIgnoreCase));
            var locale = localeFile is null ? root : await ReadYaml(relative, localeFile, timeout.Token);
            var installers = new List<InstallerMetadata>();
            if (Node(root, "Installers") is YamlSequenceNode sequence)
                foreach (var item in sequence.Children.OfType<YamlMappingNode>().Take(128))
                    installers.Add(new(Value(item, "Architecture") ?? "unknown", Value(item, "Scope") ?? Value(root, "Scope"),
                        Value(item, "MinimumOSVersion") ?? Value(root, "MinimumOSVersion"), Value(item, "InstallerType") ?? Value(root, "InstallerType")));
            var metadata = new PackageMetadata(id, version, Value(locale, "PackageName"), Value(locale, "Publisher"), installers,
                "https://github.com/microsoft/winget-pkgs/tree/master/" + relative);
            if (options.UseCache)
            {
                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(metadata), ct);
                    File.Move(temp, cachePath, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache is optional. */ }
            }
            return metadata;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new HttpRequestException("Package metadata lookup timed out."); }
    }
    private async Task<YamlMappingNode> ReadYaml(string relative, string file, CancellationToken ct)
    {
        using var response = await client.GetAsync("https://raw.githubusercontent.com/microsoft/winget-pkgs/master/" + relative + "/" + Uri.EscapeDataString(file), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var yaml = new YamlStream();
        try { yaml.Load(new StringReader(await ReadBounded(response, ct))); }
        catch (YamlDotNet.Core.YamlException ex) { throw new InvalidDataException("Invalid package YAML.", ex); }
        return yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode mapping ? mapping : throw new InvalidDataException("Expected one manifest mapping.");
    }
    private static async Task<string> ReadBounded(HttpResponseMessage response, CancellationToken ct)
    {
        const int limit = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Metadata exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Metadata exceeds size limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private static YamlNode? Node(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;
    private static bool Valid(PackageMetadata item) => item.Id is not null && item.Version is not null &&
        SafeSegment().IsMatch(item.Id) && SafeSegment().IsMatch(item.Version) && item.Installers is not null &&
        item.Installers.Count <= 128 && item.Installers.All(i => i is not null && i.Architecture is { Length: > 0 and < 32 });
    private static string? Value(YamlMappingNode map, string key) => (Node(map, key) as YamlScalarNode)?.Value;
}
