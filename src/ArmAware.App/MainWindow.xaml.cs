using ArmAware.App.ViewModels;
using ArmAware.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
namespace ArmAware.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SizeToDisplay(1360, 880);
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = Scale(980); presenter.PreferredMinimumHeight = Scale(640); }
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsScanning)) DispatcherQueue.TryEnqueue(OnScanningChanged);
            else if (e.PropertyName == nameof(MainViewModel.Selected))
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { DetailScroll.ChangeView(null, 0, null, true); FadeIn(DetailScroll); });
            else if (e.PropertyName == nameof(MainViewModel.ShowWelcome) && ViewModel.ShowWelcome) StartHeroFloat();
        };
        // After a rebuild, scroll to the selection or the top.
        ViewModel.RowsRefreshed += () => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollToCurrent);
        Closed += (_, _) => ViewModel.Cancel();
        _ = ViewModel.CheckWingetAsync();
        SetRowTransitions(enabled: true);
        if (ViewModel.ShowWelcome) StartHeroFloat();
    }
    // Custom storyboards honour the Windows "Animation effects" setting.
    private static bool AnimationsOn => new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private void SetRowTransitions(bool enabled)
    {
        var transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        if (enabled) transitions.Add(new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { IsStaggeringEnabled = true, FromVerticalOffset = 16 });
        ResultsList.ItemContainerTransitions = transitions;
    }
    private void FadeIn(UIElement target)
    {
        if (!AnimationsOn) return;
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, target);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); story.Children.Add(fade); story.Begin();
    }
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? heroFloat;
    private void StartHeroFloat()
    {
        if (!AnimationsOn || heroFloat is not null) return;
        Hero.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
        var bob = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = -8,
            Duration = TimeSpan.FromSeconds(3),
            AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.SineEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(bob, Hero.RenderTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(bob, "Y");
        heroFloat = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); heroFloat.Children.Add(bob); heroFloat.Begin();
    }
    // The scan button disables itself while scanning, so keep focus from jumping.
    private void OnScanningChanged()
    {
        // Only animate rows once results settle.
        SetRowTransitions(enabled: !ViewModel.IsScanning);
        if (ViewModel.IsScanning) { CancelButton.Focus(FocusState.Programmatic); return; }
        if (ViewModel.Rows.Count > 0) { ScrollToCurrent(); ResultsList.Focus(FocusState.Programmatic); }
        else ScanButton.Focus(FocusState.Programmatic);
    }
    private void ScrollToCurrent()
    {
        if (ViewModel.Rows.Count == 0) return;
        ResultsList.UpdateLayout();
        ResultsList.ScrollIntoView(ViewModel.Selected ?? ViewModel.Rows[0], ScrollIntoViewAlignment.Leading);
    }
    // Outline and accessible name for the active status card.
    public Microsoft.UI.Xaml.Media.Brush CardBorder(int filter, int index) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[filter == index ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
    public Thickness CardThickness(int filter, int index) => new(filter == index ? 2 : 1);
    public string CardName(string count, string label, int filter, int index) =>
        $"{count} {label}{(filter == index ? ", showing" : "")}. Select to show these applications.";
    private async void NoticeDetails_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBlock { Text = string.Join("\n\n", ViewModel.NoticeDetails), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = ViewModel.NoticeTitle,
            Content = new ScrollViewer { Content = text, MaxHeight = 420 },
            CloseButtonText = "Close",
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        }.ShowAsync();
    }
    private async void SearchArm64_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var query = Uri.EscapeDataString($"{row.Name} ARM64 download");
        await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.bing.com/search?q=" + query));
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    private int Scale(int logical) => (int)(logical * GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0);
    // AppWindow sizes are physical pixels; scale the logical target and keep it inside the work area.
    private void SizeToDisplay(int width, int height)
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = new global::Windows.Graphics.SizeInt32(Math.Min(Scale(width), (int)(work.Width * 0.92)), Math.Min(Scale(height), (int)(work.Height * 0.92)));
        AppWindow.MoveAndResize(new(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2, size.Width, size.Height));
    }
    private void ShowFeedback(string message, InfoBarSeverity severity)
    {
        Feedback.Message = message; Feedback.Severity = severity; Feedback.IsOpen = true;
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.WingetCommand)) { ShowFeedback("Select a confirmed ARM64 recommendation first.", InfoBarSeverity.Informational); return; }
        var data = new DataPackage(); data.SetText(ViewModel.SwitchCommandsWithPause); Clipboard.SetContent(data);
        ShowFeedback("Switch script copied. It checks ARM64 availability and stops if preflight or removal fails. Review it before running.", InfoBarSeverity.Success);
    }
    private async void GetAppInstaller_Click(object sender, RoutedEventArgs e)
        => await global::Windows.System.Launcher.LaunchUriAsync(new Uri(ArmAware.Windows.Integration.WingetAvailability.AppInstallerStoreUri));
    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string { Length: > 0 } text) return;
        var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data);
        ShowFeedback("Copied. Paste it into a terminal and review it before running.", InfoBarSeverity.Success);
    }
    private async void RunSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row || string.IsNullOrEmpty(ViewModel.SwitchScript)) return;
        await ConfirmAndRunAsync($"Switch {row.Name} to ARM64?",
            $"This checks ARM64 availability, removes the selected version and scope of {row.Name}, then installs ARM64 only if removal succeeds. Back up settings and files first: removal can delete data and installation can fail. Windows may ask for administrator permission.",
            ViewModel.SwitchCommandsWithPause, ViewModel.SwitchScript, "Run in Terminal");
    }
    private async void RunOne_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var (command, title, explanation) = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "uninstall" => (ViewModel.UninstallCommand, $"Remove the x64 version of {row.Name}?",
                $"This uninstalls {row.Name}. Install the ARM64 version afterwards with step 2. Close {row.Name} first."),
            "install" => (ViewModel.WingetCommand, $"Install the ARM64 version of {row.Name}?",
                $"If the x64 version is still installed, winget will usually report it as already installed and change nothing. Remove it first with step 1, or use Run in Terminal to do both."),
            "update" => (ViewModel.UpdateCommand, $"Update {row.Name}?", $"Winget will update {row.Name}. Close it first."),
            _ => ("", "", ""),
        };
        if (command.Length == 0) return;
        await ConfirmAndRunAsync(title, explanation + " Windows may ask for administrator permission.", command, command, "Run in Terminal");
    }
    private async void RunUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row || string.IsNullOrEmpty(ViewModel.UpdateCommand)) return;
        await ConfirmAndRunAsync($"Update {row.Name}?", $"Winget will update {row.Name}. Close it first. Windows may ask for administrator permission.",
            ViewModel.UpdateCommand, ViewModel.UpdateCommand, "Run update");
    }
    // Confirm the exact commands, then run them in a visible terminal.
    private async Task ConfirmAndRunAsync(string title, string explanation, string shownCommands, string script, string runLabel)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"],
            Child = new TextBlock
            {
                Text = shownCommands,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontSize = 12,
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["MonoFontFamily"]
            },
        });
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = runLabel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        // Encoded so shell parsing cannot reinterpret the commands.
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("Write-Host 'ArmAware: running suggested Winget commands' -ForegroundColor Cyan\n" + script));
        try
        {
            var terminal = new System.Diagnostics.ProcessStartInfo("wt.exe") { UseShellExecute = true };
            foreach (var arg in new[] { "new-tab", "--title", "ArmAware", "powershell.exe", "-NoProfile", "-NoExit", "-EncodedCommand", encoded }) terminal.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(terminal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Fall back to a PowerShell console when Windows Terminal is missing.
            var console = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = true };
            foreach (var arg in new[] { "-NoProfile", "-NoExit", "-EncodedCommand", encoded }) console.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(console);
        }
        ShowFeedback("Running in a terminal. Rescan when it finishes to see the result.", InfoBarSeverity.Informational);
    }
    private async void Package_Click(object sender, RoutedEventArgs e)
    {
        if (MainViewModel.IsManifestUrl(ViewModel.PackageUrl, out var uri)) await global::Windows.System.Launcher.LaunchUriAsync(uri);
        else ShowFeedback("No verified package manifest link is available.", InfoBarSeverity.Informational);
    }
    private static Grid Setting(string title, string description, ToggleSwitch toggle)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title });
        text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["FieldLabelStyle"] });
        toggle.OnContent = toggle.OffContent = ""; toggle.MinWidth = 0; toggle.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 8, 12),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(toggle, 1); row.Children.Add(text); row.Children.Add(toggle);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, title);
        return row;
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsScanning) { ShowFeedback("Finish or cancel the scan before changing settings.", InfoBarSeverity.Informational); return; }
        var online = new ToggleSwitch { IsOn = ViewModel.Options.CheckOnlineMetadata };
        var cached = new ToggleSwitch { IsOn = ViewModel.Options.UseCache };
        var users = new ToggleSwitch { IsOn = ViewModel.Options.IncludePerUser };
        var packaged = new ToggleSwitch { IsOn = ViewModel.Options.IncludePackaged };
        var content = new StackPanel { Spacing = 4, MinWidth = 440 };
        content.Children.Add(new TextBlock { Text = "Scanning", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(Setting("Include per-user installations", "Applications installed only for your account.", users));
        content.Children.Add(Setting("Include packaged applications", "Current-user MSIX and Store apps.", packaged));
        content.Children.Add(new TextBlock { Text = "Package metadata", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(Setting("Check online for ARM64 packages", "Uses Windows Package Manager's winget and Microsoft Store catalogs to find ARM64 installers and Winget updates. This sends package IDs, versions, family names/product codes and market information to Microsoft. No full inventory file is uploaded.", online));
        content.Children.Add(Setting("Use cached Winget metadata", "Reuse manifests fetched in the last 7 days.", cached));
        var about = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        about.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/AppLogo.png")), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top });
        content.Children.Add(about);
        about.Children.Add(new TextBlock
        {
            Text = $"ArmAware {typeof(App).Assembly.GetName().Version?.ToString(3)} • {ViewModel.Host.ToUpperInvariant()} host\nLocal inspection, no telemetry. Application changes require you to confirm a command. Settings and cache live in LocalAppData\\ArmAware.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            Style = (Style)Application.Current.Resources["FieldLabelStyle"],
        });
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Settings",
            Content = new ScrollViewer { Content = content },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        var welcome = new Button { Content = "Show welcome screen", Margin = new Thickness(44, 8, 0, 0) };
        welcome.Click += (_, _) => { dialog.Hide(); ViewModel.OpenWelcomeCommand.Execute(null); };
        content.Children.Add(welcome);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.Options.CheckOnlineMetadata = online.IsOn; ViewModel.Options.UseCache = cached.IsOn;
            ViewModel.Options.IncludePerUser = users.IsOn; ViewModel.Options.IncludePackaged = packaged.IsOn;
            try { SettingsStore.Save(ViewModel.Options); ShowFeedback("Settings saved. They apply to the next scan.", InfoBarSeverity.Success); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowFeedback("Could not save settings: " + ex.Message, InfoBarSeverity.Error); }
        }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Report is not { } report) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "ArmAware-scan" };
            picker.FileTypeChoices.Add("JSON report", new List<string> { ".json" });
            picker.FileTypeChoices.Add("CSV report", new List<string> { ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSaveFileAsync() is { } file)
            {
                await global::Windows.Storage.FileIO.WriteTextAsync(file, file.FileType == ".csv" ? ReportExporter.Csv(report) : ReportExporter.Json(report));
                ShowFeedback($"Exported to {file.Path}. It contains installed application metadata and paths; review it before sharing.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { ShowFeedback("Export failed: " + ex.Message, InfoBarSeverity.Error); }
    }
}


