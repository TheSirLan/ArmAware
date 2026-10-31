using ArmAware.Core.Classification;
using ArmAware.Core.Models;
using ArmAware.Windows.Discovery;
namespace ArmAware.Tests;

public sealed class SystemToolTests
{
    [Theory]
    [InlineData("Microsoft.SecHealthUI_8wekyb3d8bbwe")]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe")]
    [InlineData("Microsoft.HEVCVideoExtension_8wekyb3d8bbwe")]
    [InlineData("MicrosoftWindows.CrossDevice_cw5n1h2txyewy")]
    public void BundledWindowsComponentsAreGrouped(string family) => Assert.NotNull(SystemToolRules.Evidence(new() { Id = "a", Name = "Localized name", PackageIdentity = family }));
    [Theory]
    [InlineData("Microsoft Visual C++ v14 Redistributable (x64)")]
    [InlineData("Microsoft .NET SDK 10.0.403 (arm64)")]
    public void RuntimeInfrastructureIsGrouped(string name) => Assert.NotNull(SystemToolRules.Evidence(new() { Id = "a", Name = name, Publisher = "Microsoft Corporation" }));
    [Theory]
    [InlineData("Microsoft.VisualStudioCode_8wekyb3d8bbwe", "Visual Studio Code")]
    [InlineData("Microsoft.PowerShell_8wekyb3d8bbwe", "PowerShell")]
    [InlineData(null, "Microsoft 365 - en-gb")]
    [InlineData("Microsoft.WindowsCalculator_anotherpublisher", "Windows Calculator")]
    public void PublisherOrSimilarNameAloneDoesNotHideApps(string? family, string name) => Assert.Null(SystemToolRules.Evidence(new() { Id = "a", Name = name, Publisher = "Microsoft Corporation", PackageIdentity = family }));
    [Theory]
    [InlineData(@"C:\ProgramData\Package Cache\{id}\VC_redist.arm64.exe")]
    [InlineData(@"C:\Users\User\AppData\Local\Package Cache\{id}\python-arm64.exe")]
    public void CachedInstallersCannotBePrimaryExecutables(string path) => Assert.True(ExecutableLocator.IsAuxiliary(path));
    [Fact]
    public void NormalPythonExecutableRemainsEligible() => Assert.False(ExecutableLocator.IsAuxiliary(@"C:\Python\python.exe"));
}
