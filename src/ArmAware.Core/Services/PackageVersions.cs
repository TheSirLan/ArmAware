namespace ArmAware.Core.Services;

public static class PackageVersions
{
    public static bool Match(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) ||
            a.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || b.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        static string Normalize(string value)
        {
            var parts = value.Trim().Split('.').ToList();
            while (parts.Count > 1 && parts[^1] == "0") parts.RemoveAt(parts.Count - 1);
            return string.Join('.', parts);
        }
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}
