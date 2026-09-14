using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using SkiaSharp;
using Svg.Skia;

namespace AstraClient.Models;

/// <summary>Shared visual identity for Minecraft loaders across launcher surfaces.</summary>
public sealed class LoaderVisual
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public BitmapImage? ImageSource { get; }
    public Geometry Icon { get; }
    public SolidColorBrush Brush { get; }

    private LoaderVisual(string key, string displayName, string description, string geometry, string color, string? imageFile = null)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        ImageSource = LoadImage(imageFile);
        Icon = Geometry.Parse(geometry);
        Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Icon.Freeze();
        Brush.Freeze();
    }

    public static LoaderVisual For(string? loader)
    {
        return (loader ?? "vanilla").Trim().ToLowerInvariant() switch
        {
            "fabric" => new("fabric", "Fabric", "Lightweight mod loader", "M12,2A10,10 0 1,0 12,22A10,10 0 0,0 12,2M12,5A7,7 0 1,1 12,19A7,7 0 0,1 12,5M10,8H14V12H10M10,14H14V16H10", "#FF62D69B", "fabric.png"),
            "quilt" => new("quilt", "Quilt", "Community mod loader", "M12,2L20,6V18L12,22L4,18V6L12,2M12,5L7,7.5V16.5L12,19L17,16.5V7.5L12,5M10,9H14V13H10", "#FFB989FF", "quilt.svg"),
            "forge" => new("forge", "Forge", "Classic mod loader", "M12,2L15,8L22,9L17,14L18,21L12,18L6,21L7,14L2,9L9,8L12,2M12,7L10.5,10.5L6.5,11L9.5,13.5L8.5,17.5L12,15.5L15.5,17.5L14.5,13.5L17.5,11L13.5,10.5Z", "#FFE38B45", "forge.png"),
            "neoforge" => new("neoforge", "NeoForge", "Modern Forge-compatible loader", "M12,2A10,10 0 1,0 12,22A10,10 0 0,0 12,2M12,5L14,10L19,12L14,14L12,19L10,14L5,12L10,10Z", "#FF64B5D8", "neoforge-mark.svg"),
            _ => new("vanilla", "Vanilla", "Official Minecraft runtime", "M4,3H20V21H4V3M7,6V8H17V6H7M7,11V13H17V11H7M7,16V18H13V16H7", "#FF9AA6B8", "vanilla-dirt.svg")
        };
    }

    private static BitmapImage? LoadImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        try
        {
            var uri = new Uri($"/AstraClient;component/Assets/Loaders/{fileName}", UriKind.Relative);
            using var resource = Application.GetResourceStream(uri)?.Stream;
            if (resource is null) return null;

            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var pngStream = new MemoryStream();
                resource.CopyTo(pngStream);
                pngStream.Position = 0;
                var png = new BitmapImage();
                png.BeginInit();
                png.StreamSource = pngStream;
                png.CacheOption = BitmapCacheOption.OnLoad;
                png.EndInit();
                png.Freeze();
                return png;
            }

            using var svg = new SKSvg();
            svg.Load(resource);
            var picture = svg.Picture;
            if (picture is null) return null;

            var bounds = picture.CullRect;
            var scale = Math.Min(96f / Math.Max(bounds.Width, 1), 96f / Math.Max(bounds.Height, 1));
            using var bitmap = new SKBitmap(96, 96, true);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate((96 - bounds.Width * scale) / 2, (96 - bounds.Height * scale) / 2);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = stream;
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch { return null; }
    }
}
