using ArmAware.Core.Models;
namespace ArmAware.Core.Classification;

public sealed class AppClassifier
{
    public AppFinding Classify(InstalledApp app, IReadOnlyList<ArchitectureInspectionResult> inspections,
        Arm64AvailabilityResult availability, bool arm64Host)
    {
        var architectures = inspections.Where(x => x.Confidence == Confidence.High).Select(x => x.Architecture).Distinct().ToArray();
        var status = AppOptimizationStatus.Unknown;
        var reason = "Insufficient executable evidence. Package and installer architectures do not establish execution behaviour.";
        var confidence = Confidence.None;
        if (architectures.Contains(BinaryArchitecture.Arm64EC) || architectures.Contains(BinaryArchitecture.Arm64X) ||
            architectures.Where(x => x != BinaryArchitecture.Unknown).Distinct().Count() > 1)
        {
            status = AppOptimizationStatus.MixedArchitecture;
            reason = "Hybrid or differing executable architectures were found. Review the individual files before changing this installation.";
            confidence = Confidence.High;
        }
        else
        {
            var allHigh = inspections.Count > 0 && inspections.All(x => x.Confidence == Confidence.High);
            // With no known primary, agreeing high-confidence candidates give a medium-confidence inference.
            var allAgree = !app.PrimaryExecutableKnown && allHigh && inspections.Count >= 2 && architectures.Length == 1;
            if ((app.PrimaryExecutableKnown && allHigh) || allAgree)
            {
                var subject = allAgree ? $"All {inspections.Count} program files found in the install folder" : "The identified application executable";
                var level = allAgree ? Confidence.Medium : Confidence.High;
                if (architectures.SequenceEqual([BinaryArchitecture.Arm64]))
                {
                    status = AppOptimizationStatus.NativeArm64;
                    reason = $"{subject} {(allAgree ? "are" : "is")} ARM64. Dependencies and running processes have not been inspected.";
                    confidence = level;
                }
                else if (architectures.Length == 1 && architectures[0] is BinaryArchitecture.X64 or BinaryArchitecture.X86 && arm64Host)
                {
                    status = availability is { Available: true, Suitable: true, MatchConfidence: Confidence.High }
                        ? AppOptimizationStatus.Arm64UpgradeAvailable : AppOptimizationStatus.EmulatedNoKnownAlternative;
                    reason = $"{subject} {(allAgree ? "target" : "targets")} x86/x64 and would use emulation on this ARM64 host. " +
                        (status == AppOptimizationStatus.Arm64UpgradeAvailable
                            ? "A same-version ARM64 installer is listed; review its scope and installer details before switching."
                            : "No suitable ARM64 distribution was confirmed.");
                    confidence = level;
                }
                if (allAgree && status != AppOptimizationStatus.Unknown)
                    reason += " This classification is inferred from the inspected files; the primary executable was not identified.";
            }
        }
        if (!arm64Host) reason += " This host is not ARM64; do not install ARM64 software here.";
        return new(app, inspections, availability, status, reason, confidence);
    }
}
