using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace ArmAware.Windows.Discovery;

/// <summary>One-off snapshot of the program files currently running for this user, taken during a scan.
/// Used only to identify which file is an app's main program; nothing is monitored or stored.</summary>
public static class RunningPrograms
{
    private const uint QueryLimitedInformation = 0x1000;

    public static IReadOnlyList<string> ImagePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var handle = OpenProcess(QueryLimitedInformation, false, process.Id);
                if (handle == IntPtr.Zero) continue; // Protected or other users' processes are skipped.
                try
                {
                    var buffer = new StringBuilder(1024); var size = (uint)buffer.Capacity;
                    if (QueryFullProcessImageNameW(handle, 0, buffer, ref size)) paths.Add(buffer.ToString());
                }
                finally { CloseHandle(handle); }
            }
        }
        return paths.ToArray();
    }

    /// <summary>Running programs inside <paramref name="folder"/> (any depth), excluding installers and helpers.</summary>
    public static IReadOnlyList<string> Within(IReadOnlyList<string> running, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return [];
        var root = Path.TrimEndingDirectorySeparator(folder.Trim().Trim('"')) + Path.DirectorySeparatorChar;
        // Broad roots would attribute unrelated programs to an app.
        if (root.Length <= 4 || string.Equals(root, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(root, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return [];
        return running.Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !ExecutableLocator.IsAuxiliary(p)).Take(12).ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
