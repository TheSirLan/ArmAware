using System.Diagnostics;
using System.Text;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
namespace ArmAware.Tests;

public sealed class SwitchCommandTests
{
    private static AppFinding Finding => new(new InstalledApp
    {
        Id = "fixture",
        Name = "Fixture",
        Version = "1.0",
        Scope = "User",
        WingetPackageId = "Vendor.Fixture",
        PrimaryExecutableKnown = true,
        ExecutablePaths = ["app.exe"]
    },
        [new("app.exe", BinaryArchitecture.X64, Confidence.High, "Fixture", "Fixture")],
        new(true, true, "Vendor.Fixture", "1.0", Confidence.High, "Fixture"), AppOptimizationStatus.Arm64UpgradeAvailable, "Fixture", Confidence.High);
    [Fact]
    public void PlanTargetsTheReviewedScopeAndInstalledVersion()
    {
        var plan = Assert.IsType<WingetSwitchPlan>(WingetSwitchCommands.Create(Finding, true));
        Assert.Contains("--scope user", plan.Uninstall); Assert.Contains("--version 1.0", plan.Uninstall);
        Assert.Contains("--architecture arm64", plan.Install); Assert.Contains("winget show", plan.Script);
        Assert.DoesNotContain("Start-Sleep", plan.Script);
    }
    [Theory]
    [InlineData(BinaryArchitecture.X64)]
    [InlineData(BinaryArchitecture.X86)]
    public void MainProgramAndAgreeingHelperEnableMediumConfidenceSwitch(BinaryArchitecture architecture)
    {
        var finding = Finding with
        {
            Application = Finding.Application with { Name = "VLC media player", WingetPackageId = "VideoLAN.VLC", Version = "3.0.24.0", Scope = "Machine", PrimaryExecutableKnown = false },
            Architectures = [new("vlc.exe", architecture, Confidence.High, "PE", "fixture"), new("vlc-cache-gen.exe", architecture, Confidence.High, "PE", "fixture")],
            Availability = Finding.Availability with { PackageId = "VideoLAN.VLC", Version = "3.0.24" },
            Confidence = Confidence.Medium
        };
        var plan = Assert.IsType<WingetSwitchPlan>(WingetSwitchCommands.Create(finding, true));
        Assert.Contains("--id VideoLAN.VLC", plan.Install);
        Assert.Contains("--scope machine --version 3.0.24.0", plan.Uninstall);
        Assert.Contains("--architecture arm64 --version 3.0.24", plan.Install);
        Assert.Null(WingetSwitchCommands.Create(finding with { Confidence = Confidence.Low }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Architectures = [finding.Architectures[0]] }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Architectures = [finding.Architectures[0], finding.Architectures[1] with { Architecture = BinaryArchitecture.Arm64 }] }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Architectures = [finding.Architectures[0], finding.Architectures[1] with { Architecture = architecture == BinaryArchitecture.X64 ? BinaryArchitecture.X86 : BinaryArchitecture.X64 }] }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Architectures = [finding.Architectures[0], finding.Architectures[1] with { Confidence = Confidence.Low }] }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Availability = finding.Availability with { MatchConfidence = Confidence.Medium } }, true));
        Assert.Null(WingetSwitchCommands.Create(finding with { Availability = finding.Availability with { Suitable = false } }, true));
    }
    [Fact]
    public void UncertainOrChangedFindingsCannotProduceReplacementCommands()
    {
        Assert.Null(WingetSwitchCommands.Create(Finding, false));
        Assert.Null(WingetSwitchCommands.Create(Finding with { Confidence = Confidence.Medium }, true));
        Assert.Null(WingetSwitchCommands.Create(Finding with { Application = Finding.Application with { PrimaryExecutableKnown = false } }, true));
        Assert.Null(WingetSwitchCommands.Create(Finding with { Application = Finding.Application with { Version = "2.0" } }, true));
        Assert.Null(WingetSwitchCommands.Create(Finding with { Application = Finding.Application with { Scope = "Unknown" } }, true));
        Assert.Null(WingetSwitchCommands.Create(Finding with { Availability = Finding.Availability with { PackageId = "Other.App" } }, true));
    }
    [Theory]
    [InlineData("Vendor.App;whoami")]
    [InlineData("Vendor.App$(whoami)")]
    [InlineData("--anything")]
    public void MetadataCannotAddShellSyntax(string id) => Assert.Null(WingetSwitchCommands.Create(Finding with
    { Application = Finding.Application with { WingetPackageId = id }, Availability = Finding.Availability with { PackageId = id } }, true));
    [Theory]
    [InlineData(1, 0, 0, false, false)]
    [InlineData(0, 1, 0, true, false)]
    [InlineData(0, 0, 1, true, true)]
    [InlineData(0, 0, 0, true, true)]
    public async Task ScriptActuallyStopsAtAFailedPreflightOrRemoval(int showExit, int uninstallExit, int installExit, bool expectRemoval, bool expectInstall)
    {
        // Define a PowerShell function so the real winget executable can never be invoked by this test.
        var fake = $$"""
            function winget {
                $phase = $args[0]
                Write-Output "CALL:$phase"
                $global:LASTEXITCODE = switch ($phase) { 'show' { {{showExit}} } 'uninstall' { {{uninstallExit}} } 'install' { {{installExit}} } }
            }
            """;
        var script = fake + "\n" + WingetSwitchCommands.Create(Finding, true)!.Script;
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        var text = await output;
        Assert.Contains("CALL:show", text);
        Assert.Equal(expectRemoval, text.Contains("CALL:uninstall")); Assert.Equal(expectInstall, text.Contains("CALL:install"));
        if (showExit + uninstallExit + installExit != 0) Assert.NotEmpty(await error);
    }
}
