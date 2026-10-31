using System.Text.RegularExpressions;
using ArmAware.Core.Models;
namespace ArmAware.Core.Services;

public sealed record WingetSwitchPlan(string Install, string Uninstall, string Script);

public static partial class WingetSwitchCommands
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,150}$")]
    private static partial Regex SafeToken();

    public static WingetSwitchPlan? Create(AppFinding finding, bool arm64Host)
    {
        var app = finding.Application;
        var native = finding.Availability;
        var confirmedPrimary = app.PrimaryExecutableKnown && finding.Confidence == Confidence.High;
        var agreeingCandidates = finding.Confidence == Confidence.Medium && finding.Architectures.Count >= 2 &&
            finding.Architectures.Select(i => i.Architecture).Distinct().Count() == 1;
        if (!arm64Host || finding.Status != AppOptimizationStatus.Arm64UpgradeAvailable || !(confirmedPrimary || agreeingCandidates) ||
            finding.Architectures.Count == 0 || finding.Architectures.Select(i => i.Architecture).Distinct().Count() != 1 ||
            finding.Architectures.Any(i => i.Confidence != Confidence.High || i.Architecture is not (BinaryArchitecture.X86 or BinaryArchitecture.X64)) ||
            native is not { Available: true, Suitable: true, MatchConfidence: Confidence.High } ||
            app.WingetPackageId is not { } id || native.Version is not { } version || app.Version is not { } installedVersion ||
            !string.Equals(id, native.PackageId, StringComparison.OrdinalIgnoreCase) || !PackageVersions.Match(installedVersion, version) ||
            !SafeToken().IsMatch(id) || !SafeToken().IsMatch(version) || !SafeToken().IsMatch(installedVersion)) return null;
        var scope = app.Scope switch { "User" => "user", "Machine" => "machine", _ => null };
        if (scope is null) return null;
        var package = $"--exact --id {id} --source winget --scope {scope}";
        var install = $"winget install {package} --architecture arm64 --version {version}";
        var uninstall = $"winget uninstall {package} --version {installedVersion}";
        // The same script is reviewed, copied and run. Never continue after a failed preflight/removal.
        var script = $$"""
            & {
                $ErrorActionPreference = 'Stop'
                winget show {{package}} --architecture arm64 --version {{version}} --disable-interactivity
                if ($LASTEXITCODE -ne 0) { throw 'ARM64 package preflight failed. Nothing was removed.' }
                {{uninstall}}
                if ($LASTEXITCODE -ne 0) { throw 'Removal failed. The ARM64 installer was not started.' }
                {{install}}
                if ($LASTEXITCODE -ne 0) { throw 'ARM64 installation failed after removal. Reinstall the original application using its trusted installer before retrying.' }
            }
            """;
        return new(install, uninstall, script);
    }
}
