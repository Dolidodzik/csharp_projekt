using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;
using TexasHoldem.Logic.Cards;

namespace PokerApp;

public static class SvgCardBitmap
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? TryLoad(Card card, int maxWidth = 110)
    {
        var path = CardAssetPaths.AbsoluteSvgPath(card);
        if (!File.Exists(path))
            return null;

        if (Cache.TryGetValue(path, out var bmp))
            return bmp;

        try
        {
            using var svg = new SKSvg();
            svg.Load(path);
            if (svg.Picture is null)
                return null;

            var bounds = svg.Picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            var scale = maxWidth / bounds.Width;
            var w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            var h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Scale((float)scale);
            c.DrawPicture(svg.Picture);

            using var image = surface.Snapshot();
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            png.SaveTo(ms);
            ms.Position = 0;
            var bitmap = new Bitmap(ms);
            Cache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
