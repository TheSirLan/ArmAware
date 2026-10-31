using System.Text.RegularExpressions;
using ArmAware.Core.Models;
namespace ArmAware.Core.Classification;

// Display grouping only: never used to infer executable architecture or recommend replacement.
public static partial class SystemToolRules
{
    private static readonly HashSet<string> BundledPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.DesktopAppInstaller", "Microsoft.AutoSuperResolution", "Microsoft.WindowsFeedbackHub",
        "Microsoft.GetHelp", "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.SecHealthUI",
        "Microsoft.WindowsCalculator", "Microsoft.WindowsCamera", "Microsoft.WindowsAlarms", "Microsoft.WindowsNotepad",
        "Microsoft.WindowsSoundRecorder", "Microsoft.Windows.Photos", "Microsoft.ScreenSketch", "Microsoft.ZuneMusic",
        "Microsoft.MicrosoftStickyNotes", "Microsoft.BingWeather", "Microsoft.BingNews", "Microsoft.BingSearch",
        "Microsoft.StartExperiencesApp", "Microsoft.WidgetsPlatformRuntime", "Microsoft.ApplicationCompatibilityEnhancements",
        "Microsoft.XboxGamingOverlay", "Microsoft.XboxSpeechToTextOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI",
        "Microsoft.AV1VideoExtension", "Microsoft.AVCEncoderVideoExtension", "Microsoft.HEIFImageExtension",
        "Microsoft.HEVCVideoExtension", "Microsoft.MPEG2VideoExtension", "Microsoft.RawImageExtension",
        "Microsoft.VP9VideoExtensions", "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension",
        "Microsoft.Office.ActionsServer", "Microsoft.OfficePushNotificationUtility", "Microsoft.SurfaceAppProxy",
        "Microsoft.6365217CE6EB4", // Microsoft Defender Store application, not a publisher-wide rule.
    };
    [GeneratedRegex(@"^Microsoft (?:Visual C\+\+ .+ Redistributable|\.NET (?:SDK|Runtime|Host)|Windows Desktop Runtime|ASP\.NET Core)", RegexOptions.IgnoreCase)]
    private static partial Regex RuntimeName();

    public static string? Evidence(InstalledApp app)
    {
        if (app.IsSystemComponent) return app.SystemComponentEvidence ?? "Declared system component.";
        if (app.PackageIdentity is { } family)
        {
            const string publisher = "_8wekyb3d8bbwe";
            if (family.EndsWith(publisher, StringComparison.OrdinalIgnoreCase))
            {
                var name = family[..^publisher.Length];
                if (BundledPackages.Contains(name) || name.StartsWith("Microsoft.Winget.", StringComparison.OrdinalIgnoreCase))
                    return "Known Windows app or support package identity (display-filter rule).";
            }
            if (family is "MicrosoftWindows.CrossDevice_cw5n1h2txyewy" or "MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy")
                return "Known Windows integration package identity (display-filter rule).";
        }
        if (app.Publisher is "Microsoft Corporation" or "Microsoft Corp." && RuntimeName().IsMatch(app.Name))
            return "Microsoft runtime, redistributable or SDK support entry (display-filter rule).";
        return null;
    }
}
