using System.Runtime.InteropServices;
using System.Text;
namespace ArmAware.Windows.Discovery;

/// <summary>Finds the program files a Windows Installer product installed, from its own component records.
/// Read-only: opens the cached package database and asks Windows Installer where each component's key file is.</summary>
public static class MsiComponents
{
    private const int MaxComponents = 600;

    public static IReadOnlyList<string> Executables(string productCode, int max = 12)
    {
        if (!Guid.TryParse(productCode, out _)) return [];
        var package = ProductInfo(productCode, "LocalPackage");
        if (string.IsNullOrEmpty(package) || !File.Exists(package)) return [];
        var results = new List<string>();
        if (MsiOpenDatabaseW(package, IntPtr.Zero, out var database) != 0) return [];
        try
        {
            if (MsiDatabaseOpenViewW(database, "SELECT `ComponentId` FROM `Component`", out var view) != 0) return [];
            try
            {
                if (MsiViewExecute(view, IntPtr.Zero) != 0) return [];
                for (var i = 0; i < MaxComponents && results.Count < max && MsiViewFetch(view, out var record) == 0; i++)
                {
                    try
                    {
                        var componentId = RecordString(record, 1);
                        if (string.IsNullOrEmpty(componentId)) continue;
                        var path = ComponentPath(productCode, componentId);
                        if (path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
                            !ExecutableLocator.IsAuxiliary(path) && !results.Contains(path, StringComparer.OrdinalIgnoreCase))
                            results.Add(path);
                    }
                    finally { MsiCloseHandle(record); }
                }
            }
            finally { MsiCloseHandle(view); }
        }
        finally { MsiCloseHandle(database); }
        return results;
    }

    private static string? ProductInfo(string productCode, string property)
    {
        var size = 0u;
        if (MsiGetProductInfoW(productCode, property, null, ref size) is not (0 or 234)) return null; // 234 = ERROR_MORE_DATA
        var buffer = new StringBuilder((int)++size);
        return MsiGetProductInfoW(productCode, property, buffer, ref size) == 0 ? buffer.ToString() : null;
    }

    private static string? ComponentPath(string productCode, string componentId)
    {
        var size = 0u;
        MsiGetComponentPathW(productCode, componentId, null, ref size);
        if (size == 0) return null;
        var buffer = new StringBuilder((int)++size);
        // INSTALLSTATE_LOCAL (3) means the key file is installed on this PC.
        return MsiGetComponentPathW(productCode, componentId, buffer, ref size) == 3 ? buffer.ToString() : null;
    }

    private static string RecordString(IntPtr record, uint field)
    {
        var size = 0u;
        MsiRecordGetStringW(record, field, new StringBuilder(1), ref size);
        var buffer = new StringBuilder((int)++size);
        return MsiRecordGetStringW(record, field, buffer, ref size) == 0 ? buffer.ToString() : "";
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductInfoW(string product, string property, StringBuilder? value, ref uint size);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiGetComponentPathW(string product, string component, StringBuilder? path, ref uint size);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint size);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(IntPtr handle);
}
