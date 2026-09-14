using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AstraClient.Services.Imaging;

/// <summary>
/// Attached behavior for async image loading via ImageCacheService.
/// Usage: imaging:AsyncImageBehavior.Source="{Binding IconUrl}"
/// </summary>
public static class AsyncImageBehavior
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source", typeof(string), typeof(AsyncImageBehavior),
            new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject d, string? value) => d.SetValue(SourceProperty, value);
    public static string? GetSource(DependencyObject d) => (string?)d.GetValue(SourceProperty);

    private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image img) return;
        if (e.NewValue is not string url || string.IsNullOrEmpty(url))
        {
            img.Source = null;
            return;
        }

        if (App.Services.TryGet<ImageCacheService>(out var cache) && cache is not null)
        {
            try
            {
                var bitmap = await cache.GetBitmapAsync(url).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Virtualized controls can reuse Image instances while a request is in flight.
                    if (string.Equals(GetSource(img), url, StringComparison.Ordinal))
                        img.Source = bitmap;
                });
            }
            catch (OperationCanceledException)
            {
                // The item left the viewport or was rebound; keep the current image untouched.
            }
        }
    }
}
