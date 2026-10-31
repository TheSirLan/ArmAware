using ArmAware.Core.Classification;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using ArmAware.Windows.Architecture;
using ArmAware.Windows.Discovery;
using ArmAware.Windows.Integration;
using ArmAware.Winget.Services;
using Microsoft.Extensions.DependencyInjection;
namespace ArmAware.Cli;

public static class Composition
{
    public static IServiceCollection AddArmAware(this IServiceCollection services, ScanOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IInstalledAppDiscovery, WindowsAppDiscovery>();
        services.AddSingleton<IArchitectureInspector, PeArchitectureInspector>();
        services.AddSingleton(_ => new WingetCatalogResolver(options));
        services.AddSingleton<StoreIdentityResolver>();
        services.AddSingleton<IPackageIdentityResolver>(p => new ResilientIdentityResolver(
            [("Winget catalog", p.GetRequiredService<WingetCatalogResolver>()),
             ("Microsoft Store", p.GetRequiredService<StoreIdentityResolver>())],
            p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientIdentityResolver>>()));
        services.AddSingleton<IArm64AvailabilityService, Arm64AvailabilityService>();
        services.AddSingleton<AppClassifier>();
        services.AddSingleton<ScanEngine>();
        return services;
    }
}
