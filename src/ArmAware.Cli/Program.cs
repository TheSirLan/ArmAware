using System.Runtime.InteropServices;
using ArmAware.Cli;
using ArmAware.Core.Models;
using ArmAware.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ArmAware.Windows.Architecture;
using ArmAware.Winget.Services;
using System.Text.Json;

if (args.Length == 2 && args[0] == "inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(await new PeArchitectureInspector().InspectAsync(args[1], default),
        new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    return 0;
}
if (args.Length == 3 && args[0] == "package")
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ArmAware/0.1");
    try
    {
        var metadata = await new ManifestRepository(http, new ScanOptions { CheckOnlineMetadata = true }).GetAsync(args[1], args[2], default);
        Console.WriteLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        return metadata is null ? 3 : 0;
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException) { Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length == 1 && args[0] == "info")
{
    Console.WriteLine($"ArmAware 0.1 development | OS {RuntimeInformation.OSDescription} | Host {RuntimeInformation.OSArchitecture}\nLocal inventory; no telemetry. Online package metadata is opt-in (--online). No installers are executed.");
    return 0;
}
var switches = new[] { "--upgrades-only", "--online", "--no-cache", "--no-packaged", "--machine-only" };
var valid = args.Length > 0 && args[0] == "scan";
for (var i = 1; i < args.Length && valid; i++)
{
    if (args[i] == "--format") valid = ++i < args.Length && args[i] is "json" or "csv" or "text";
    else valid = switches.Contains(args[i]);
}
if (!valid)
{
    Console.Error.WriteLine("Usage: armaware info | scan [--upgrades-only] [--format json|csv|text] [--online] [--no-cache] [--no-packaged] [--machine-only]");
    return 2;
}
var formatIndex = Array.IndexOf(args, "--format");
var format = formatIndex < 0 ? "text" : args.ElementAtOrDefault(formatIndex + 1);
if (format is not ("text" or "json" or "csv")) { Console.Error.WriteLine("Expected --format json|csv|text."); return 2; }
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var services = new ServiceCollection().AddLogging(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace))
    .AddArmAware(new ScanOptions
    {
        CheckOnlineMetadata = args.Contains("--online"),
        UseCache = !args.Contains("--no-cache"),
        IncludePackaged = !args.Contains("--no-packaged"),
        IncludePerUser = !args.Contains("--machine-only")
    });
await using var provider = services.BuildServiceProvider();
try
{
    var report = await provider.GetRequiredService<ScanEngine>().ScanAsync(null, cancellation.Token);
    if (args.Contains("--upgrades-only")) report = report with { Applications = report.Applications.Where(a => a.Status == AppOptimizationStatus.Arm64UpgradeAvailable).ToArray() };
    if (format == "json") Console.WriteLine(ReportExporter.Json(report));
    else if (format == "csv") Console.Write(ReportExporter.Csv(report));
    else
    {
        foreach (var f in report.Applications) Console.WriteLine($"{f.Application.Name} | {f.Application.Version}{(f.Application.WingetCatalog?.UpdateAvailable == true ? $" (update: {f.Application.WingetCatalog.LatestVersion})" : "")} |{string.Join('/', f.Architectures.Select(a => a.Architecture).Distinct())} | {f.Status}\n  {f.Recommendation}");
        Console.WriteLine($"{report.Applications.Count} findings; {report.Warnings.Count} warnings; {report.ElapsedSeconds:F1}s");
    }
    foreach (var warning in report.Warnings) Console.Error.WriteLine(warning);
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Scan cancelled."); return 130; }
catch (Exception ex) { Console.Error.WriteLine("Scan failed: " + ex.Message); return 1; }
