using ArmAware.Core.Classification;
using ArmAware.Core.Models;
namespace ArmAware.Tests;

public sealed class ClassificationTests
{
    private readonly InstalledApp app = new() { Id = "test", Name = "Test", PrimaryExecutableKnown = true };
    private static Arm64AvailabilityResult Available => new(true, true, "Vendor.App", "1", Confidence.High, "fixture");
    private static ArchitectureInspectionResult Image(BinaryArchitecture arch) => new("test.exe", arch, Confidence.High, "fixture", "fixture");
    [Theory]
    [InlineData(BinaryArchitecture.Arm64, true, AppOptimizationStatus.NativeArm64)]
    [InlineData(BinaryArchitecture.X64, true, AppOptimizationStatus.Arm64UpgradeAvailable)]
    [InlineData(BinaryArchitecture.X86, true, AppOptimizationStatus.Arm64UpgradeAvailable)]
    [InlineData(BinaryArchitecture.X64, false, AppOptimizationStatus.Unknown)]
    [InlineData(BinaryArchitecture.Unknown, true, AppOptimizationStatus.Unknown)]
    [InlineData(BinaryArchitecture.AnyCpu, true, AppOptimizationStatus.Unknown)]
    [InlineData(BinaryArchitecture.Arm64EC, true, AppOptimizationStatus.MixedArchitecture)]
    [InlineData(BinaryArchitecture.Arm64X, true, AppOptimizationStatus.MixedArchitecture)]
    public void EvidenceAndHostDetermineStatus(BinaryArchitecture arch, bool armHost, AppOptimizationStatus expected)
        => Assert.Equal(expected, new AppClassifier().Classify(app, [Image(arch)], Available, armHost).Status);
    [Fact]
    public void MixedIsConservative() => Assert.Equal(AppOptimizationStatus.MixedArchitecture,
        new AppClassifier().Classify(app, [Image(BinaryArchitecture.X64), Image(BinaryArchitecture.Arm64)], Available, true).Status);
    [Fact]
    public void NoNetworkCannotCreateUpgrade() => Assert.Equal(AppOptimizationStatus.EmulatedNoKnownAlternative,
        new AppClassifier().Classify(app, [Image(BinaryArchitecture.X64)], Available with { Available = false, Suitable = false }, true).Status);
    [Fact]
    public void CandidateIsNotPrimary() => Assert.Equal(AppOptimizationStatus.Unknown,
        new AppClassifier().Classify(app with { PrimaryExecutableKnown = false }, [Image(BinaryArchitecture.X64)], Available, true).Status);
    [Theory]
    [InlineData(BinaryArchitecture.X64, AppOptimizationStatus.Arm64UpgradeAvailable)]
    [InlineData(BinaryArchitecture.X86, AppOptimizationStatus.Arm64UpgradeAvailable)]
    [InlineData(BinaryArchitecture.Arm64, AppOptimizationStatus.NativeArm64)]
    public void AgreeingCandidatesEstablishArchitectureWithMediumConfidence(BinaryArchitecture architecture, AppOptimizationStatus expected)
    {
        var finding = new AppClassifier().Classify(app with { PrimaryExecutableKnown = false }, [Image(architecture), Image(architecture)], Available, true);
        Assert.Equal(expected, finding.Status);
        Assert.Equal(Confidence.Medium, finding.Confidence);
        Assert.StartsWith("All 2 program files", finding.Recommendation);
        Assert.Contains("classification is inferred", finding.Recommendation);
    }
    [Fact]
    public void InferredUpgradeWithMatchingInstallerEnablesReplacementCommands()
    {
        var installation = app with { PrimaryExecutableKnown = false, Scope = "User", Version = "1", WingetPackageId = "Vendor.App" };
        var finding = new AppClassifier().Classify(installation, [Image(BinaryArchitecture.X64), Image(BinaryArchitecture.X64)], Available, true);
        Assert.Equal(AppOptimizationStatus.Arm64UpgradeAvailable, finding.Status);
        Assert.IsType<ArmAware.Core.Services.WingetSwitchPlan>(ArmAware.Core.Services.WingetSwitchCommands.Create(finding, true));
    }
    [Fact]
    public void CandidatesThatDisagreeOrAreUncertainDoNotAgree()
    {
        var unknownApp = app with { PrimaryExecutableKnown = false };
        Assert.Equal(AppOptimizationStatus.MixedArchitecture, new AppClassifier().Classify(unknownApp, [Image(BinaryArchitecture.X64), Image(BinaryArchitecture.Arm64)], Available, true).Status);
        Assert.Equal(AppOptimizationStatus.Unknown, new AppClassifier().Classify(unknownApp,
            [Image(BinaryArchitecture.X64), Image(BinaryArchitecture.X64) with { Confidence = Confidence.Low }], Available, true).Status);
    }
    [Fact]
    public void UncertainMatchCannotRecommend() => Assert.Equal(AppOptimizationStatus.EmulatedNoKnownAlternative,
        new AppClassifier().Classify(app, [Image(BinaryArchitecture.X64)], Available with { MatchConfidence = Confidence.Medium }, true).Status);
}
