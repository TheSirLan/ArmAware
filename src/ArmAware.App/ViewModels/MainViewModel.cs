using System.Collections.ObjectModel;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ArmAware.App.ViewModels;

public partial class MainViewModel(ScanEngine engine, ScanOptions options) : ObservableObject
{
    public ScanOptions Options { get; } = options;
    public ObservableCollection<FindingRow> Rows { get; } = [];
    private readonly List<AppFinding> all = [];
    private CancellationTokenSource? cancellation;
    private ScanReport? report;
    public ScanReport? Report { get => report; private set { if (SetProperty(ref report, value)) OnPropertyChanged(nameof(HasReport)); } }
    public bool HasReport => Report is not null;
    private string search = "";
    public string Search { get => search; set { if (SetProperty(ref search, value)) Refresh(); } }
    private ViewSettings view = SettingsStore.LoadViewSettings();
    private void SaveView(ViewSettings updated)
    {
        view = updated;
        try { SettingsStore.SaveViewSettings(view); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SetNotice("Could not save display preference", ex.Message, InfoBarSeverity.Error); }
    }
    public bool HideSystemTools
    {
        get => view.HideSystemTools;
        set { if (value == view.HideSystemTools) return; SaveView(view with { HideSystemTools = value }); OnPropertyChanged(); Refresh(); }
    }
    private bool showWelcome = !SettingsStore.LoadViewSettings().OnboardingComplete;
    public bool ShowWelcome { get => showWelcome; set { if (SetProperty(ref showWelcome, value)) OnPropertyChanged(nameof(ShowMain)); } }
    public bool ShowMain => !ShowWelcome;
    public bool CanLeaveWelcome => view.OnboardingComplete;
    [RelayCommand] private void OpenWelcome() { OnPropertyChanged(nameof(CanLeaveWelcome)); ShowWelcome = true; }
    [RelayCommand] private void CloseWelcome() => ShowWelcome = false;
    public bool ShowStandardEmptyState => ShowEmptyState;
    // Online checks need Windows Package Manager.
    private ArmAware.Windows.Integration.WingetState? wingetState;
    public bool WingetChecking => wingetState is null;
    public bool WingetReady => wingetState == ArmAware.Windows.Integration.WingetState.Ready;
    public bool WingetNeedsSetup => wingetState is ArmAware.Windows.Integration.WingetState.NotInstalled or ArmAware.Windows.Integration.WingetState.Unavailable;
    public bool WingetMissing => wingetState == ArmAware.Windows.Integration.WingetState.NotInstalled;
    private string wingetMessage = "";
    public string WingetMessage { get => wingetMessage; private set { SetProperty(ref wingetMessage, value); } }
    [RelayCommand]
    public async Task CheckWingetAsync()
    {
        SetWingetState(null, "");
        var (state, detail) = await Task.Run(ArmAware.Windows.Integration.WingetAvailability.Check);
        SetWingetState(state, state == ArmAware.Windows.Integration.WingetState.NotInstalled
            ? "Windows Package Manager (winget) isn't installed. ArmAware uses it to find ARM64 versions and updates. It comes with App Installer, which is free from the Microsoft Store."
            : detail ?? "");
    }
    private void SetWingetState(ArmAware.Windows.Integration.WingetState? state, string message)
    {
        wingetState = state; WingetMessage = message;
        foreach (var name in new[] { nameof(WingetChecking), nameof(WingetReady), nameof(WingetNeedsSetup), nameof(WingetMissing) }) OnPropertyChanged(name);
    }
    [RelayCommand]
    private async Task StartFirstScanAsync(string online)
    {
        Options.CheckOnlineMetadata = online == "online";
        ShowWelcome = false;
        await ScanAsync();
    }
    private bool colourKeyOpen;
    public bool ColourKeyOpen
    {
        get => colourKeyOpen;
        set { if (SetProperty(ref colourKeyOpen, value) && !value && !view.ColourKeyDismissed) SaveView(view with { ColourKeyDismissed = true }); }
    }
    private int filterIndex;
    public int FilterIndex { get => filterIndex; set { if (SetProperty(ref filterIndex, value)) Refresh(); } }
    private int sortIndex;
    public int SortIndex { get => sortIndex; set { if (SetProperty(ref sortIndex, value)) Refresh(); } }
    private FindingRow? selected;
    public FindingRow? Selected { get => selected; set { if (SetProperty(ref selected, value)) OnSelectedChanged(); } }
    public bool HasSelection => Selected is not null;
    public bool NoSelection => Selected is null;
    private string stage = "Ready to scan. All inspection runs locally.";
    public string Stage { get => stage; set { SetProperty(ref stage, value); } }
    private string lastScan = "No completed scans";
    public string LastScan { get => lastScan; set { SetProperty(ref lastScan, value); } }
    private bool isScanning;
    public bool IsScanning { get => isScanning; set { if (SetProperty(ref isScanning, value)) { OnPropertyChanged(nameof(IsIdle)); UpdateEmptyState(); } } }
    public bool IsIdle => !IsScanning;
    private double progressValue;
    public double ProgressValue { get => progressValue; set { SetProperty(ref progressValue, value); } }
    private bool progressIndeterminate = true;
    public bool ProgressIndeterminate { get => progressIndeterminate; set { SetProperty(ref progressIndeterminate, value); } }
    private string total = "0";
    public string Total { get => total; set { SetProperty(ref total, value); } }
    private string native = "0";
    public string Native { get => native; set { SetProperty(ref native, value); } }
    private string emulated = "0";
    public string Emulated { get => emulated; set { SetProperty(ref emulated, value); } }
    private string upgrades = "0";
    public string Upgrades { get => upgrades; set { SetProperty(ref upgrades, value); } }
    private string unknown = "0";
    public string Unknown { get => unknown; set { SetProperty(ref unknown, value); } }
    private string mixed = "0";
    public string Mixed { get => mixed; set { SetProperty(ref mixed, value); } }
    private string noticeTitle = "";
    public string NoticeTitle { get => noticeTitle; set { SetProperty(ref noticeTitle, value); } }
    private string noticeMessage = "";
    public string NoticeMessage { get => noticeMessage; set { SetProperty(ref noticeMessage, value); } }
    private InfoBarSeverity noticeSeverity;
    public InfoBarSeverity NoticeSeverity { get => noticeSeverity; set { SetProperty(ref noticeSeverity, value); } }
    private bool noticeOpen;
    public bool NoticeOpen { get => noticeOpen; set { SetProperty(ref noticeOpen, value); } }
    private IReadOnlyList<string> noticeDetails = [];
    public IReadOnlyList<string> NoticeDetails { get => noticeDetails; private set { if (SetProperty(ref noticeDetails, value)) OnPropertyChanged(nameof(HasNoticeDetails)); } }
    public bool HasNoticeDetails => NoticeDetails.Count > 0;
    private void SetNotice(string title, string message, InfoBarSeverity severity, IReadOnlyList<string>? details = null)
    {
        NoticeTitle = title; NoticeMessage = message; NoticeSeverity = severity; NoticeDetails = details ?? []; NoticeOpen = true;
    }
    private void ClearNotice() { NoticeOpen = false; NoticeDetails = []; }
    /// <summary>Raised after the visible rows are rebuilt, so the view can reset its scroll position.</summary>
    public event Action? RowsRefreshed;
    private bool showEmptyState = true;
    public bool ShowEmptyState { get => showEmptyState; set { SetProperty(ref showEmptyState, value); } }
    private string emptyGlyph = "";
    public string EmptyGlyph { get => emptyGlyph; set { SetProperty(ref emptyGlyph, value); } }
    private ImageSource emptyArt = Art.Image("empty-no-scan");
    public ImageSource EmptyArt { get => emptyArt; set { SetProperty(ref emptyArt, value); } }
    private string emptyTitle = "No scan yet";
    public string EmptyTitle { get => emptyTitle; set { SetProperty(ref emptyTitle, value); } }
    private string emptyMessage = "Scan to inventory installed applications and check which ones run natively on ARM64. Nothing is uploaded or changed.";
    public string EmptyMessage { get => emptyMessage; set { SetProperty(ref emptyMessage, value); } }
    private bool showEmptyScanButton = true;
    public bool ShowEmptyScanButton { get => showEmptyScanButton; set { if (SetProperty(ref showEmptyScanButton, value)) OnPropertyChanged(nameof(ShowEmptyGlyph)); } }
    public bool ShowEmptyGlyph => !ShowEmptyScanButton;
    private string resultCount = "";
    public string ResultCount { get => resultCount; set { SetProperty(ref resultCount, value); } }
    public string Host => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    public string HostSummary => $"This PC • {Host.ToUpperInvariant()} host";
    private WingetSwitchPlan? SwitchPlan => Selected is { } selected
        ? WingetSwitchCommands.Create(selected.Finding, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64) : null;
    public string WingetCommand => SwitchPlan?.Install ?? "";
    public bool HasWingetCommand => WingetCommand.Length > 0;
    public string UninstallCommand => SwitchPlan?.Uninstall ?? "";
    public string SwitchCommands => SwitchPlan?.Script ?? "";
    public string SwitchScript => SwitchPlan?.Script ?? "";
    // Copy exactly the guarded script that is shown and executed, including the availability preflight.
    public string SwitchCommandsWithPause => SwitchScript;
    public string SwitchIntro => HasWingetCommand
        ? $"Close {Selected!.Name} and back up its settings and files first. Uninstalling can remove data. If ARM64 installation fails, reinstall the original application with its trusted installer." : "";
    public string UpdateCommand => Selected?.Finding.Application is { WingetCatalog.UpdateAvailable: true, WingetPackageId: { } updateId } &&
        System.Text.RegularExpressions.Regex.IsMatch(updateId, @"^[A-Za-z0-9][A-Za-z0-9._+-]{0,150}$")
        ? $"winget upgrade --exact --id {updateId} --source winget" : "";
    public bool HasUpdateCommand => UpdateCommand.Length > 0;
    public string? PackageUrl => Selected?.Finding.Availability.PackageUrl;
    public bool HasPackageUrl => IsManifestUrl(PackageUrl, out _);
    public static bool IsManifestUrl(string? url, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "github.com" &&
        uri.AbsolutePath.StartsWith("/microsoft/winget-pkgs/tree/master/manifests/", StringComparison.Ordinal);
    public bool ShowSearchAction => Selected?.Finding.Status is AppOptimizationStatus.EmulatedNoKnownAlternative or AppOptimizationStatus.MixedArchitecture && !HasWingetCommand;
    public bool HasActions => HasWingetCommand || HasPackageUrl || ShowSearchAction;
    private void OnSelectedChanged()
    {
        foreach (var name in new[] { nameof(HasSelection), nameof(NoSelection), nameof(WingetCommand), nameof(HasWingetCommand), nameof(UninstallCommand), nameof(SwitchCommands), nameof(SwitchIntro), nameof(PackageUrl), nameof(HasPackageUrl), nameof(ShowSearchAction), nameof(HasActions), nameof(UpdateCommand), nameof(HasUpdateCommand) })
            OnPropertyChanged(name);
    }
    [RelayCommand]
    private void ShowFilter(string index) { if (int.TryParse(index, out var value)) { Search = ""; FilterIndex = value; } }
    private void Refresh()
    {
        var visible = all.Where(f => !HideSystemTools || !f.Application.IsSystemComponent).ToArray();
        var query = visible.Where(f => f.Application.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || (f.Application.Publisher?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false));
        query = query.Where(f => FilterIndex switch
        {
            1 => f.Status == AppOptimizationStatus.Arm64UpgradeAvailable,
            2 => f.Status is AppOptimizationStatus.Arm64UpgradeAvailable or AppOptimizationStatus.EmulatedNoKnownAlternative,
            3 => f.Status == AppOptimizationStatus.NativeArm64,
            4 => f.Status == AppOptimizationStatus.MixedArchitecture,
            5 => f.Status == AppOptimizationStatus.Unknown,
            _ => true
        });
        query = SortIndex switch { 1 => query.OrderBy(f => StatusOrder(f.Status)).ThenBy(f => f.Application.Name, StringComparer.OrdinalIgnoreCase), 2 => query.OrderBy(f => f.Application.Publisher).ThenBy(f => f.Application.Name), _ => query.OrderBy(f => f.Application.Name, StringComparer.OrdinalIgnoreCase) };
        var keep = Selected?.Finding;
        Rows.Clear(); foreach (var f in query) Rows.Add(new(f));
        if (keep is not null) Selected = Rows.FirstOrDefault(r => ReferenceEquals(r.Finding, keep));
        Total = visible.Length.ToString(); Native = visible.Count(f => f.Status == AppOptimizationStatus.NativeArm64).ToString();
        Emulated = visible.Count(f => f.Status is AppOptimizationStatus.Arm64UpgradeAvailable or AppOptimizationStatus.EmulatedNoKnownAlternative).ToString();
        Upgrades = visible.Count(f => f.Status == AppOptimizationStatus.Arm64UpgradeAvailable).ToString(); Unknown = visible.Count(f => f.Status == AppOptimizationStatus.Unknown).ToString();
        Mixed = visible.Count(f => f.Status == AppOptimizationStatus.MixedArchitecture).ToString();
        ResultCount = all.Count == 0 ? "" : Rows.Count == visible.Length ? $"{visible.Length} applications" : $"{Rows.Count} of {visible.Length} applications";
        if (HideSystemTools && all.Count > visible.Length) ResultCount += $" • {all.Count - visible.Length} system components hidden";
        var updates = visible.Count(f => f.Application.WingetCatalog?.UpdateAvailable == true);
        if (updates > 0) ResultCount += $" • {updates} Winget update{(updates == 1 ? "" : "s")} available";
        UpdateEmptyState();
        RowsRefreshed?.Invoke();
    }
    // Actionable statuses first, least informative last.
    private static int StatusOrder(AppOptimizationStatus status) => status switch
    {
        AppOptimizationStatus.Arm64UpgradeAvailable => 0,
        AppOptimizationStatus.EmulatedNoKnownAlternative => 1,
        AppOptimizationStatus.MixedArchitecture => 2,
        AppOptimizationStatus.NativeArm64 => 3,
        _ => 4,
    };
    private void UpdateEmptyState()
    {
        ShowEmptyState = Rows.Count == 0;
        OnPropertyChanged(nameof(ShowStandardEmptyState));
        ShowEmptyScanButton = all.Count == 0 && !IsScanning;
        EmptyArt = Art.Image(all.Count == 0 ? "empty-no-scan" : "empty-no-matches");
        (EmptyGlyph, EmptyTitle, EmptyMessage) = (all.Count, IsScanning) switch
        {
            (0, true) => ("", "Looking for applications…", "Results appear here as each application is inspected."),
            (0, false) => ("", "No scan yet", "Scan to inventory installed applications and check which ones run natively on ARM64. Nothing is uploaded or changed."),
            _ => ("", "No matching applications", "Try a different search term, select All applications, or clear Hide system tools."),
        };
    }
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        if (!view.OnboardingComplete) SaveView(view with { OnboardingComplete = true });
        IsScanning = true; ClearNotice(); Selected = null; all.Clear(); Refresh(); Report = null;
        ProgressIndeterminate = true; ProgressValue = 0;
        cancellation = new();
        try
        {
            SettingsStore.Save(Options);
            var refreshTimer = System.Diagnostics.Stopwatch.StartNew();
            var progress = new Progress<ScanProgress>(p =>
            {
                Stage = p.Total is { } count ? $"{p.Stage} • {p.Completed} of {count}" : p.Stage;
                ProgressIndeterminate = p.Total is not > 0;
                if (p.Total is { } max && max > 0) ProgressValue = 100.0 * p.Completed / max;
                // Throttle rebuilds while scanning.
                if (p.Finding is { } f) { all.Add(f); if (refreshTimer.ElapsedMilliseconds > 250) { Refresh(); refreshTimer.Restart(); } }
            });
            Report = await engine.ScanAsync(progress, cancellation.Token);
            all.Clear(); all.AddRange(Report.Applications); Refresh();
            LastScan = $"Last scan {Report.CompletedAt.LocalDateTime:g} • {Report.ElapsedSeconds:F1}s";
            Stage = "Scan complete";
            if (!view.ColourKeyDismissed && all.Count > 0) ColourKeyOpen = true;
            if (Report.Warnings.Count > 0)
                SetNotice($"Scan finished with {Report.Warnings.Count} note{(Report.Warnings.Count == 1 ? "" : "s")}",
                    "Some sources could not be fully read. Everything else was scanned.", InfoBarSeverity.Informational, Report.Warnings);
        }
        catch (OperationCanceledException) { Stage = "Scan cancelled."; SetNotice("Scan cancelled", "Completed findings remain visible; the report is incomplete.", InfoBarSeverity.Warning); }
        catch (Exception ex) { Stage = "Scan could not finish."; SetNotice("Scan could not finish", ex.Message, InfoBarSeverity.Error); }
        finally { IsScanning = false; cancellation.Dispose(); cancellation = null; }
    }
    [RelayCommand] public void Cancel() => cancellation?.Cancel();
}
public sealed class FindingRow(AppFinding finding) : System.ComponentModel.INotifyPropertyChanged
{
    public AppFinding Finding { get; } = finding;
    private InstalledApp App => Finding.Application;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    // Loaded lazily when a row first binds it; the letter tile shows until then.
    private ImageSource? icon;
    private bool iconRequested;
    public ImageSource? Icon
    {
        get
        {
            if (!iconRequested) { iconRequested = true; _ = LoadIconAsync(); }
            return icon;
        }
    }
    public bool HasIcon => icon is not null;
    public bool NoIcon => icon is null;
    private async Task LoadIconAsync()
    {
        // Raising PropertyChanged inside a binding read crashes WinUI, so defer.
        await Task.Yield();
        if (await AppIcons.GetAsync(App) is not { } loaded) return;
        icon = loaded;
        foreach (var name in new[] { nameof(Icon), nameof(HasIcon), nameof(NoIcon) }) PropertyChanged?.Invoke(this, new(name));
    }
    public string Name => App.Name;
    public string Initial => App.Name.FirstOrDefault(char.IsLetterOrDigit) is var c && c != default ? char.ToUpperInvariant(c).ToString() : "?";
    public string Publisher => App.Publisher ?? "Unknown publisher";
    public string Version => App.Version ?? "Unknown";
    public bool UpdateAvailable => App.WingetCatalog?.UpdateAvailable == true;
    public string UpdateLabel => UpdateAvailable ? $"Update: {App.WingetCatalog!.LatestVersion}" : "";
    public string UpdateSummary => UpdateAvailable ? $"Winget can update {Name} from {Version} to {App.WingetCatalog!.LatestVersion}." : "";
    public string PublisherAndVersion => $"{Publisher} • {Version}";
    public string Architecture => Finding.Architectures.Count == 0 ? "Unknown" : string.Join(" / ", Finding.Architectures.Select(a => ArchitectureLabel(a.Architecture)).Distinct());
    // For Unknown, the inspected file is not confirmed as the main program, so its architecture is only a hint.
    public bool ArchitectureConfirmed => Finding.Status != AppOptimizationStatus.Unknown || Finding.Architectures.All(a => a.Architecture == BinaryArchitecture.Unknown);
    public string ArchitectureDisplay => ArchitectureConfirmed ? Architecture : Architecture + "?";
    public string ArchitectureTooltip => ArchitectureConfirmed ? Architecture : $"{Architecture} was found on a candidate file, but it could not be confirmed as this app's main program.";
    public Brush ArchitectureForeground => (Brush)Application.Current.Resources[ArchitectureConfirmed ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"];
    public string AccessibleName => $"{Name}, {Status}, {(ArchitectureConfirmed ? Architecture : "architecture unconfirmed")}, version {Version}{(UpdateAvailable ? $", update to {App.WingetCatalog!.LatestVersion} available" : "")}";
    public string Availability => Finding.Availability.Available ? (Finding.Availability.Suitable ? "Same-version ARM64" : "ARM64 • review constraints") : "Unconfirmed";
    public string Status => Finding.Status switch { AppOptimizationStatus.NativeArm64 => "Native ARM64", AppOptimizationStatus.Arm64UpgradeAvailable => "ARM64 available", AppOptimizationStatus.EmulatedNoKnownAlternative => "Emulated", AppOptimizationStatus.MixedArchitecture => "Mixed / hybrid", _ => "Unknown" };
    private string StatusKey => Finding.Status switch { AppOptimizationStatus.NativeArm64 => "Native", AppOptimizationStatus.Arm64UpgradeAvailable => "Upgrade", AppOptimizationStatus.EmulatedNoKnownAlternative => "Emulated", AppOptimizationStatus.MixedArchitecture => "Mixed", _ => "Unknown" };
    public string StatusGlyph => (string)Application.Current.Resources["Glyph" + StatusKey];
    public ImageSource StatusArt => Art.Image(StatusKey switch
    {
        "Native" => "status-native",
        "Upgrade" => "status-arm64-available",
        "Emulated" => "status-emulated",
        "Mixed" => "status-mixed",
        _ => "status-unknown",
    });
    public Brush StatusForeground => (Brush)Application.Current.Resources[$"Status{StatusKey}ForegroundBrush"];
    public Brush StatusBackground => (Brush)Application.Current.Resources[$"Status{StatusKey}BackgroundBrush"];
    public string Recommendation => Finding.Recommendation;
    public string Scope => App.Scope;
    public string Location => App.InstallLocation ?? "Unknown";
    public string Package => App.PackageIdentity ?? "Unpackaged / unknown";
    public string PackageArchitecture => App.PackageArchitecture ?? "Unknown";
    public string WingetId => App.WingetPackageId ?? "Unresolved";
    public string MatchConfidence => Finding.Availability.MatchConfidence.ToString();
    public string Confidence => Finding.Confidence.ToString();
    public string AvailabilityEvidence => Finding.Availability.Evidence;
    public string Sources => string.Join('\n', App.Sources.Concat(App.SystemComponentEvidence is { } evidence ? [evidence] : Array.Empty<string>()));
    public IReadOnlyList<ExecutableRow> Executables => Finding.Architectures.Select(a => new ExecutableRow(a)).ToList();
    public bool HasExecutables => Finding.Architectures.Count > 0;
    public bool NoExecutables => Finding.Architectures.Count == 0;
    public string Warnings => string.Join('\n', App.Warnings);
    public bool HasWarnings => App.Warnings.Count > 0;
    internal static string ArchitectureLabel(BinaryArchitecture architecture) => architecture switch
    {
        BinaryArchitecture.X86 => "x86",
        BinaryArchitecture.X64 => "x64",
        BinaryArchitecture.Arm64 => "ARM64",
        BinaryArchitecture.Arm64EC => "ARM64EC",
        BinaryArchitecture.Arm64X => "ARM64X",
        BinaryArchitecture.AnyCpu => "AnyCPU",
        _ => "Unknown",
    };
}
public sealed record ExecutableRow(ArchitectureInspectionResult Result)
{
    public string Path => Result.Path;
    public string FileName => System.IO.Path.GetFileName(Result.Path);
    public string Architecture => FindingRow.ArchitectureLabel(Result.Architecture);
    public string Confidence => $"{Result.Confidence} confidence";
    public string Evidence => $"{Result.Method}: {Result.Evidence}";
    public string Limitation => Result.Limitation ?? "";
    public bool HasLimitation => !string.IsNullOrEmpty(Result.Limitation);
}
public static class SettingsStore
{
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArmAware", "settings.json");
    public static ScanOptions Load()
    {
        try { return File.Exists(PathName) ? System.Text.Json.JsonSerializer.Deserialize<ScanOptions>(File.ReadAllText(PathName)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return new(); }
    }
    public static void Save(ScanOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, System.Text.Json.JsonSerializer.Serialize(options));
    }
    private static string ViewPath => Path.Combine(Path.GetDirectoryName(PathName)!, "view-settings.json");
    public static ViewSettings LoadViewSettings()
    {
        try { return File.Exists(ViewPath) ? System.Text.Json.JsonSerializer.Deserialize<ViewSettings>(File.ReadAllText(ViewPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return new(); }
    }
    public static void SaveViewSettings(ViewSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ViewPath)!);
        File.WriteAllText(ViewPath, System.Text.Json.JsonSerializer.Serialize(settings));
    }
}
public sealed record ViewSettings(bool HideSystemTools = true, bool OnboardingComplete = false, bool ColourKeyDismissed = false);
/// <summary>Illustrations shipped in Assets/Art, loaded once each.</summary>
public static class Art
{
    private static readonly Dictionary<string, ImageSource> Cache = [];
    public static ImageSource Image(string name)
    {
        if (!Cache.TryGetValue(name, out var image))
            Cache[name] = image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/Art/{name}.png"));
        return image;
    }
}

