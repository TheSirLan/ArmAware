namespace ArmAware.Windows.Discovery;

public static class ExecutableLocator
{
    public static string? FromDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0) return null;
            text = text[1..end];
        }
        else
        {
            var comma = text.LastIndexOf(',');
            if (comma >= 0 && int.TryParse(text[(comma + 1)..], out _)) text = text[..comma].Trim();
        }
        return Path.IsPathFullyQualified(text) && text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    public static bool IsAuxiliary(string path)
    {
        // Burn/bootstrapper cache images describe installers, even when their ProductName matches the application.
        if (path.Replace('/', '\\').Contains("\\Package Cache\\", StringComparison.OrdinalIgnoreCase)) return true;
        var name = Path.GetFileNameWithoutExtension(path);
        return new[] { "unins", "uninstall", "setup", "installer", "update", "crash", "helper" }
            .Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
    public static string[] Candidates(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return [];
        var root = Path.GetFullPath(directory);
        if (root.TrimEnd(Path.DirectorySeparatorChar).Length <= 3 ||
            root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return [];
        // Only the installation root, never recursive traversal or following directory junctions.
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return [];
        return Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly).Take(64).Where(x => !IsAuxiliary(x)).Take(12).ToArray();
    }
}
