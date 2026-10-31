using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArmAware.Core.Models;
namespace ArmAware.Core.Services;

public static class ReportExporter
{
    public static string Json(ScanReport report) => JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
    public static string Csv(ScanReport report)
    {
        static string Cell(string? value)
        {
            var s = value ?? "";
            if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var output = new StringBuilder("Application,Version,Architectures,Status,PackageId,MicrosoftStoreProductId,Recommendation,LatestVersion,UpdateAvailable\r\n");
        foreach (var f in report.Applications)
            output.AppendLine(string.Join(',', new[] { f.Application.Name, f.Application.Version,
                string.Join('/', f.Architectures.Select(x => x.Architecture).Distinct()), f.Status.ToString(),
                f.Application.WingetPackageId, f.Application.MicrosoftStoreProductId, f.Recommendation,
                f.Application.WingetCatalog?.LatestVersion, f.Application.WingetCatalog?.UpdateAvailable.ToString() }.Select(Cell)));
        return output.ToString();
    }
}
