using ArmAware.App.ViewModels;
using ArmAware.Cli;
using ArmAware.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
namespace ArmAware.App;

public partial class App : Application
{
    private Window? window;
    private ServiceProvider? services;
    public App()
    {
        UnhandledException += (_, e) => LogStartupError(e.Exception);
        InitializeComponent();
    }
    private static void LogStartupError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArmAware");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "startup-error.log"), exception.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        services = new ServiceCollection().AddLogging(b => b.AddDebug()).AddArmAware(SettingsStore.Load())
            .AddSingleton<MainViewModel>().BuildServiceProvider();
        window = new MainWindow(services.GetRequiredService<MainViewModel>());
        window.Closed += (_, _) => { services.GetRequiredService<MainViewModel>().Cancel(); services.Dispose(); };
        window.Activate();
    }
}
