using System.Security;
using System.Xml.Linq;
using ArmAware.Core.Classification;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Management.Deployment;
namespace ArmAware.Windows.Discovery;

public sealed class WindowsAppDiscovery(ScanOptions options, ILogger<WindowsAppDiscovery> logger) : IInstalledAppDiscovery
{
    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken)
        => Task.Run(() => Discover(cancellationToken), cancellationToken);

    private IReadOnlyList<InstalledApp> Discover(CancellationToken ct)
    {
        var apps = new List<InstalledApp>();
        var running = new Lazy<IReadOnlyList<string>>(RunningPrograms.ImagePaths);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                if (hive == RegistryHive.CurrentUser && !options.IncludePerUser) continue;
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var key in uninstall.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var entry = uninstall.OpenSubKey(key);
                            if (entry?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                            string? Read(string field) => entry.GetValue(field) as string;
                            var warnings = new List<string>();
                            var icon = Read("DisplayIcon");
                            var location = Read("InstallLocation");
                            var candidates = Array.Empty<string>();
                            var iconPath = ExecutableLocator.FromDisplayIcon(icon);
                            var source = $"{hive}/{view}/Uninstall/{key}";
                            var sources = new List<string> { source };
                            try
                            {
                                candidates = iconPath is not null && !ExecutableLocator.IsAuxiliary(iconPath)
                                    ? [iconPath] : ExecutableLocator.Candidates(location);
                                // No install folder: an installer-style DisplayIcon still points at the app folder.
                                if (candidates.Length == 0 && string.IsNullOrWhiteSpace(location) && iconPath is not null)
                                    candidates = ExecutableLocator.Candidates(Path.GetDirectoryName(iconPath));
                                // MSI products often record no folder; component records list installed files. Skipped for system components.
                                if (candidates.Length == 0 && entry.GetValue("WindowsInstaller") is int msi && msi == 1 &&
                                    entry.GetValue("SystemComponent") is not 1 &&
                                    MsiComponents.Executables(key) is { Count: > 0 } msiFiles)
                                {
                                    candidates = msiFiles.ToArray();
                                    sources.Add("Windows Installer component records");
                                }
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                            { warnings.Add($"Executable location: {ex.Message}"); }
                            // A DisplayIcon alone is not authoritative: verify the executable product name.
                            var primaryKnown = candidates.Length == 1 && ProductMatches(candidates[0], name);
                            // Still ambiguous: prefer a program from the folder that is running now.
                            if (!primaryKnown && RunningPrograms.Within(running.Value,
                                    string.IsNullOrWhiteSpace(location) && iconPath is not null ? Path.GetDirectoryName(iconPath) : location) is { Count: > 0 } live)
                            {
                                candidates = live.ToArray();
                                primaryKnown = live.Count == 1;
                                sources.Add("Running program at scan time");
                            }
                            // Kernel drivers cannot be emulated, so nothing to check.
                            var isDriver = candidates.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(name, @"\bdriver\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            apps.Add(new InstalledApp
                            {
                                Id = source,
                                Name = name,
                                Version = Read("DisplayVersion"),
                                Publisher = Read("Publisher"),
                                InstallLocation = location,
                                DisplayIcon = icon,
                                UninstallString = Read("UninstallString"),
                                WingetPackageId = Read("WinGetPackageIdentifier"),
                                IsSystemComponent = (entry.GetValue("SystemComponent") is int systemComponent && systemComponent == 1) || isDriver,
                                SystemComponentEvidence = entry.GetValue("SystemComponent") is int flag && flag == 1 ? "Uninstall registry entry declares SystemComponent=1."
                                    : isDriver ? "Device driver package. Drivers cannot run under emulation, so one that loads on this PC is already ARM64." : null,
                                Scope = hive == RegistryHive.CurrentUser ? "User" : "Machine",
                                Sources = sources,
                                ExecutablePaths = candidates,
                                PrimaryExecutableKnown = primaryKnown,
                                Warnings = warnings
                            });
                        }
                        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                        { logger.LogWarning(ex, "Cannot read uninstall entry {Key}", key); }
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    apps.Add(new InstalledApp { Id = $"warning:{hive}:{view}", Name = "Inventory source unavailable", Warnings = [$"{hive}/{view}: {ex.Message}"] });
                }
            }
        if (options.IncludePackaged && options.IncludePerUser) DiscoverPackages(apps, ct);
        return Deduplicate(apps.Select(app => SystemToolRules.Evidence(app) is { } evidence
            ? app with { IsSystemComponent = true, SystemComponentEvidence = evidence } : app));
    }

    private static bool ProductMatches(string path, string name)
    {
        try { return NamesMatch(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName, name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    // Display names often add a version, a bracketed label or a publisher prefix ("Beeper 4.3.264",
    // "Microsoft Visual Studio Code (User)"). Those are removed before comparing, and the product name must then be the
    // whole display name or its start or end. Only used for a single candidate executable.
    public static bool NamesMatch(string? productName, string displayName)
    {
        static string Key(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\([^)]*\)", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bv?\d+(?:\.\d+)+(?:[-+][0-9A-Za-z.\-]+)?", " ");
            return string.Concat(s.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(productName)) return false;
        var product = Key(productName); var display = Key(displayName);
        return product.Length >= 4 && (display == product || display.StartsWith(product, StringComparison.Ordinal) || display.EndsWith(product, StringComparison.Ordinal));
    }

    private void DiscoverPackages(List<InstalledApp> apps, CancellationToken ct)
    {
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;
                    var identity = package.Id;
                    var location = package.InstalledLocation.Path;
                    var warnings = new List<string>();
                    var paths = new List<string>();
                    // Integration ("sparse") packages registered by desktop apps keep their programs in an external folder.
                    string? external = null;
                    try { external = string.IsNullOrWhiteSpace(package.EffectiveExternalPath) ? null : package.EffectiveExternalPath; }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
                    var declaresProgram = false;
                    try
                    {
                        // XML reader prohibits DTD and limits document size; manifest remains untrusted input.
                        using var reader = System.Xml.XmlReader.Create(Path.Combine(location, "AppxManifest.xml"),
                            new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, MaxCharactersInDocument = 4 * 1024 * 1024 });
                        foreach (var element in XDocument.Load(reader).Descendants().Where(x => x.Name.LocalName == "Application").Take(12))
                        {
                            var executable = element.Attribute("Executable")?.Value;
                            declaresProgram |= executable is not null || element.Attribute("StartPage") is not null;
                            if (executable is null || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            if (Within(location, executable) is { } inPackage && (external is null || File.Exists(inPackage))) paths.Add(inPackage);
                            else if (external is not null && Within(external, executable) is { } outside && File.Exists(outside)) paths.Add(outside);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
                    { warnings.Add($"Package executable manifest unavailable: {ex.Message}"); declaresProgram = true; }
                    // Hide companions of separately listed desktop apps, and packages that declare no program.
                    var hiddenEvidence = external is not null
                        ? $"Integration package for a desktop app whose program lives in {external}; the desktop app is listed separately."
                        : !declaresProgram ? "Extension, codec or language pack: the package declares no program to run." : null;
                    var v = identity.Version;
                    apps.Add(new InstalledApp
                    {
                        Id = "msix:" + identity.FullName,
                        Name = string.IsNullOrWhiteSpace(package.DisplayName) ? identity.Name : package.DisplayName,
                        Publisher = package.PublisherDisplayName,
                        Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        PackageIdentity = identity.FamilyName,
                        PackageArchitecture = identity.Architecture.ToString(),
                        IsSystemComponent = package.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System || hiddenEvidence is not null,
                        SystemComponentEvidence = package.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System ? "Windows reports PackageSignatureKind.System (Windows OS signing certificate)." : hiddenEvidence,
                        InstallLocation = location,
                        Scope = "User",
                        Sources = ["PackageManager/current user/" + identity.FullName],
                        ExecutablePaths = paths,
                        PrimaryExecutableKnown = paths.Count > 0,
                        Warnings = warnings
                    });
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or IOException)
                { logger.LogWarning(ex, "Cannot inspect package"); }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { apps.Add(new InstalledApp { Id = "warning:msix", Name = "Packaged inventory unavailable", Warnings = [ex.Message] }); }
    }

    // Resolves a manifest-relative path, refusing anything that escapes the root folder.
    private static string? Within(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public static IReadOnlyList<InstalledApp> Deduplicate(IEnumerable<InstalledApp> apps)
    {
        // Registry views sometimes expose the same entry. Only merge stable keys plus scope/location/version.
        return apps.GroupBy(a => a.PackageIdentity is { } id ? $"msix:{id}:{a.Version}" :
            $"arp:{a.Id.Split('/').Last()}:{a.Scope}:{a.InstallLocation}:{a.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with
            {
                Sources = g.SelectMany(a => a.Sources).Distinct().ToArray(),
                Warnings = g.SelectMany(a => a.Warnings).Distinct().ToArray()
            }).ToArray();
    }
}

