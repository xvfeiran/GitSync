using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VendorSync.UI;

/// <summary>加载应用图标；WPF 窗口用 ImageSource，失败时返回 null 自动降级。</summary>
public static class AppIcon
{
    private static ImageSource? _cached;
    private static bool _attempted;

    public static ImageSource? Load()
    {
        if (_attempted) return _cached;
        _attempted = true;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("VendorSync.app.ico");
            if (stream is null) return null;

            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            _cached = decoder.Frames[0];
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _cached = null;
        }
        return _cached;
    }
}
