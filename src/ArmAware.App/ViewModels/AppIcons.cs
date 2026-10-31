using ArmAware.Core.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
namespace ArmAware.App.ViewModels;

/// <summary>Loads app icons from local package or executable resources, cached per app.</summary>
public static class AppIcons
{
    private const uint Size = 64;
    private static readonly Dictionary<string, Task<ImageSource?>> Cache = [];

    // UI thread only (BitmapImage); concurrent requests for one app share a task.
    public static Task<ImageSource?> GetAsync(InstalledApp app)
    {
        if (!Cache.TryGetValue(app.Id, out var task)) Cache[app.Id] = task = LoadAsync(app);
        return task;
    }

    private static async Task<ImageSource?> LoadAsync(InstalledApp app)
    {
        try
        {
            if (app.Id.StartsWith("msix:", StringComparison.Ordinal))
            {
                var package = new global::Windows.Management.Deployment.PackageManager().FindPackageForUser(string.Empty, app.Id["msix:".Length..]);
                if (package?.GetLogoAsRandomAccessStreamReference(new global::Windows.Foundation.Size(Size, Size)) is not { } logo) return null;
                using var stream = await logo.OpenReadAsync();
                var image = new BitmapImage();
                await image.SetSourceAsync(stream);
                return image;
            }
            foreach (var path in IconFiles(app))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, Size, ThumbnailOptions.UseCurrentScale);
                if (thumbnail is null || thumbnail.Size == 0) continue;
                var image = new BitmapImage();
                await image.SetSourceAsync(thumbnail);
                return image;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    // DisplayIcon (".exe" or ".ico", optional ",index") first, then inspected program files.
    private static IEnumerable<string> IconFiles(InstalledApp app)
    {
        if (app.DisplayIcon is { Length: > 0 } icon)
        {
            var path = icon.Trim();
            var comma = path.LastIndexOf(',');
            if (comma > 0 && int.TryParse(path[(comma + 1)..].Trim(), out _)) path = path[..comma];
            path = path.Trim().Trim('"');
            if ((path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) && File.Exists(path)) yield return path;
        }
        foreach (var exe in app.ExecutablePaths.Take(2)) if (File.Exists(exe)) yield return exe;
    }
}
