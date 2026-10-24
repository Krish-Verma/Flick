using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalClip.App.Services;

/// <summary>Independent small pixel copies; no transform retains a larger JPEG decoder.</summary>
public static class FilmstripFrameDecoder
{
    public const int FrameCount = 6, MaxWidth = 160, MaxHeight = 90;
    public const long MaximumRetainedPixelBytes = FrameCount * MaxWidth * MaxHeight * 4;
    public static BitmapSource Decode(Stream input)
    {
        if (!input.CanSeek || input.Length > 512 * 1024) throw new InvalidDataException("Timeline JPEG exceeds its byte limit.");
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.None);
        var source = decoder.Frames[0];
        if (source.PixelWidth > 320 || source.PixelHeight > 180 || source.PixelWidth <= 0 || source.PixelHeight <= 0)
            throw new InvalidDataException("Timeline frame exceeded its decoded bounds.");
        double scale = Math.Min(1, Math.Min((double)MaxWidth / source.PixelWidth, (double)MaxHeight / source.PixelHeight));
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(transformed, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        byte[] pixels = new byte[width * height * 4]; converted.CopyPixels(pixels, width * 4, 0);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }
}
